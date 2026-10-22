using System.Buffers.Binary;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>Quem lê os bytes do volume. No Windows é o volume cru, só leitura; nos testes, uma imagem na memória.</summary>
public interface IVolumeReader
{
    long Length { get; }

    /// <summary>Lê até <c>buffer.Length</c> bytes a partir de <paramref name="offset"/>. Devolve quantos vieram (menos que o pedido só no fim do volume).</summary>
    int Read(long offset, Span<byte> buffer);
}

public sealed class NtfsException(string message) : Exception(message);

/// <summary>Medidas do volume NTFS, tiradas do setor de boot.</summary>
public sealed record NtfsGeometry(int BytesPerSector, int BytesPerCluster, long TotalClusters, long MftLcn, int RecordSize)
{
    public long MftOffset => MftLcn * BytesPerCluster;
}

/// <summary>
/// Leva 8.5 (V01/V02, opção B): leitura do formato do NTFS, feita a partir da documentação pública (Microsoft Learn e a documentação do projeto
/// Linux-NTFS). Tudo aqui só interpreta bytes que outro código leu; nada abre disco nem grava.
/// </summary>
public static class NtfsBoot
{
    /// <summary>Null se o setor não é de um volume NTFS ou traz medidas impossíveis.</summary>
    public static NtfsGeometry? Parse(ReadOnlySpan<byte> sector)
    {
        if (sector.Length < 0x48) return null;
        if (!sector.Slice(3, 8).SequenceEqual("NTFS    "u8)) return null;
        int bps = BinaryPrimitives.ReadUInt16LittleEndian(sector[0x0B..]);
        if (bps is < 512 or > 4096 || (bps & (bps - 1)) != 0) return null;
        int spcRaw = sector[0x0D];
        // Acima de 128 o byte guarda o expoente negativo: 256 - valor = potência de 2 (clusters grandes em discos de setor grande)
        long spc = spcRaw <= 128 ? spcRaw : 1L << (256 - spcRaw);
        if (spc == 0 || (spc & (spc - 1)) != 0) return null;
        long bpc = bps * spc;
        if (bpc > 64L << 20) return null;
        long totalSectors = BinaryPrimitives.ReadInt64LittleEndian(sector[0x28..]);
        long mftLcn = BinaryPrimitives.ReadInt64LittleEndian(sector[0x30..]);
        if (totalSectors <= 0) return null;
        long totalClusters = totalSectors / spc;
        if (mftLcn <= 0 || mftLcn >= totalClusters) return null;
        sbyte perRecord = (sbyte)sector[0x40];
        long recordSize = perRecord > 0 ? perRecord * bpc : perRecord >= -16 ? 1L << -perRecord : 0;
        if (recordSize is < 256 or > 65536 || (recordSize & (recordSize - 1)) != 0) return null;
        return new NtfsGeometry(bps, (int)bpc, totalClusters, mftLcn, (int)recordSize);
    }
}

/// <summary>Lista de trechos de um atributo que não cabe no registro (os "data runs").</summary>
public static class NtfsRunlist
{
    public const int MaxRuns = 200_000;

    /// <summary>
    /// Cada trecho começa com um byte: o nibble baixo é o tamanho do campo "quantos clusters" e o alto, o do campo "onde" (com sinal, relativo
    /// ao trecho anterior; tamanho zero é um buraco de arquivo esparso). Termina em byte zero. <c>Ok</c> falso quando o dado está estragado.
    /// </summary>
    public static (List<Extent> Extents, bool Ok) Parse(ReadOnlySpan<byte> data, long startVcn)
    {
        var list = new List<Extent>();
        long vcn = startVcn, lcn = 0;
        int i = 0;
        while (i < data.Length)
        {
            byte header = data[i++];
            if (header == 0) return (list, true);
            int lenSize = header & 0x0F, offSize = header >> 4;
            if (lenSize is 0 or > 8 || offSize > 8 || i + lenSize + offSize > data.Length || list.Count >= MaxRuns) return (list, false);
            ulong length = 0;
            for (int b = 0; b < lenSize; b++) length |= (ulong)data[i + b] << (8 * b);
            i += lenSize;
            if (length == 0 || length > long.MaxValue / 2) return (list, false);
            if (offSize == 0)
            {
                list.Add(new Extent(vcn, -1, (long)length));
            }
            else
            {
                long delta = 0;
                for (int b = 0; b < offSize; b++) delta |= (long)data[i + b] << (8 * b);
                if ((data[i + offSize - 1] & 0x80) != 0 && offSize < 8) delta |= -1L << (8 * offSize); // estende o sinal
                i += offSize;
                lcn += delta;
                if (lcn < 0) return (list, false);
                list.Add(new Extent(vcn, lcn, (long)length));
            }
            vcn += (long)length;
        }
        return (list, true); // acabou o espaço sem o zero final: o Windows aceita
    }
}

/// <summary>Um atributo de um registro da MFT.</summary>
public sealed record NtfsAttribute(uint Type, string Name, ushort Flags, bool NonResident, byte[] Resident, IReadOnlyList<Extent> Runs, bool RunsOk,
    long StartVcn, long AllocatedSize, long RealSize, long InitializedSize)
{
    public const uint StandardInformation = 0x10, AttributeList = 0x20, FileName = 0x30, Data = 0x80;
    public bool Compressed => (Flags & 0x0001) != 0;
    public bool Encrypted => (Flags & 0x4000) != 0;
    public bool Sparse => (Flags & 0x8000) != 0;
}

/// <summary>Nome guardado no atributo $FILE_NAME. O espaço de nomes diz se é o nome longo (Win32), o curto (DOS) ou os dois.</summary>
public sealed record NtfsName(long Parent, ushort ParentSequence, string Name, byte Namespace, long RealSize, uint Flags)
{
    public bool IsDosOnly => Namespace == 2;
}

/// <summary>Um registro da MFT (a "ficha" de um arquivo ou pasta), já sem os marcadores de integridade.</summary>
public sealed class MftRecord
{
    public long Number { get; init; }
    public ushort Sequence { get; init; }
    public bool InUse { get; init; }
    public bool IsDirectory { get; init; }
    /// <summary>Diferente de zero nos registros de extensão: guardam atributos de outro registro e não são arquivos por si.</summary>
    public long BaseRecord { get; init; }
    public IReadOnlyList<NtfsAttribute> Attributes { get; init; } = [];

    public bool HasAttributeList => Attributes.Any(a => a.Type == NtfsAttribute.AttributeList);

    /// <summary>O fluxo de dados sem nome (o conteúdo do arquivo).</summary>
    public NtfsAttribute? MainData => Attributes.FirstOrDefault(a => a.Type == NtfsAttribute.Data && a.Name.Length == 0 && a.StartVcn == 0);

    public DateTime? Modified
    {
        get
        {
            var si = Attributes.FirstOrDefault(a => a.Type == NtfsAttribute.StandardInformation && !a.NonResident);
            if (si is null || si.Resident.Length < 16) return null;
            return FileTime(BinaryPrimitives.ReadInt64LittleEndian(si.Resident.AsSpan(8)));
        }
    }

    /// <summary>Vários nomes (longo e curto, vários links): fica o melhor para mostrar, preferindo o nome longo.</summary>
    public NtfsName? BestName()
    {
        NtfsName? best = null;
        foreach (var a in Attributes.Where(a => a.Type == NtfsAttribute.FileName && !a.NonResident))
        {
            var n = ParseName(a.Resident);
            if (n is null) continue;
            if (best is null || Rank(n) > Rank(best)) best = n;
        }
        return best;
    }

    private static int Rank(NtfsName n) => n.Namespace switch { 1 or 3 => 3, 0 => 2, _ => 1 };

    public static DateTime? FileTime(long ticks) =>
        ticks > 0 && ticks < 2_650_467_743_990_000_000L ? DateTime.FromFileTimeUtc(ticks) : null;

    private static NtfsName? ParseName(byte[] c)
    {
        if (c.Length < 0x42) return null;
        ulong parent = BinaryPrimitives.ReadUInt64LittleEndian(c);
        int chars = c[0x40];
        if (0x42 + chars * 2 > c.Length) return null;
        return new NtfsName((long)(parent & 0xFFFF_FFFF_FFFFUL), (ushort)(parent >> 48), Encoding.Unicode.GetString(c, 0x42, chars * 2), c[0x41],
            BinaryPrimitives.ReadInt64LittleEndian(c.AsSpan(0x30)), BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(0x38)));
    }

    /// <summary>
    /// Lê um registro de <paramref name="record"/> (os bytes são alterados: o Windows troca os dois últimos bytes de cada setor por um marcador, e
    /// aqui eles voltam ao valor verdadeiro). Null se não é um registro, se o marcador não confere (gravação interrompida) ou se tem medidas impossíveis.
    /// </summary>
    public static MftRecord? Parse(byte[] record, int sectorSize, long number)
    {
        if (record.Length < 48 || record[0] != 'F' || record[1] != 'I' || record[2] != 'L' || record[3] != 'E') return null;
        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4));
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(6));
        if (usaCount < 2 || usaOffset < 0x2A || usaOffset + usaCount * 2 > record.Length || usaCount - 1 > record.Length / sectorSize + 1) return null;
        for (int s = 1; s < usaCount; s++)
        {
            int pos = s * sectorSize - 2;
            if (pos + 2 > record.Length) break;
            if (record[pos] != record[usaOffset] || record[pos + 1] != record[usaOffset + 1]) return null;
            record[pos] = record[usaOffset + s * 2];
            record[pos + 1] = record[usaOffset + s * 2 + 1];
        }

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x16));
        int usedSize = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(0x18)), (uint)record.Length);
        int limit = usedSize >= 48 ? usedSize : record.Length;
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x14));
        var attrs = new List<NtfsAttribute>();
        while (offset + 8 <= limit && attrs.Count < 64)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset));
            if (type == 0xFFFFFFFF) break;
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset + 4));
            if (length < 24 || (length & 7) != 0 || (long)offset + length > record.Length) break;
            var attr = ParseAttribute(record, offset, length, type);
            if (attr is not null) attrs.Add(attr);
            offset += length;
        }
        return new MftRecord
        {
            Number = number,
            Sequence = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x10)),
            InUse = (flags & 1) != 0,
            IsDirectory = (flags & 2) != 0,
            BaseRecord = (long)(BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(0x20)) & 0xFFFF_FFFF_FFFFUL),
            Attributes = attrs,
        };
    }

    private static NtfsAttribute? ParseAttribute(byte[] r, int at, int length, uint type)
    {
        bool nonResident = r[at + 8] != 0;
        int nameLen = r[at + 9], nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(at + 10));
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(at + 12));
        string name = "";
        if (nameLen > 0)
        {
            if (nameOffset + nameLen * 2 > length) return null;
            name = Encoding.Unicode.GetString(r, at + nameOffset, nameLen * 2);
        }
        if (!nonResident)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(at + 0x10));
            int contentOffset = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(at + 0x14));
            if (size < 0 || contentOffset < 0x18 || (long)contentOffset + size > length) return null;
            return new NtfsAttribute(type, name, flags, false, r.AsSpan(at + contentOffset, size).ToArray(), [], true, 0, size, size, size);
        }
        if (length < 0x40) return null;
        long startVcn = BinaryPrimitives.ReadInt64LittleEndian(r.AsSpan(at + 0x10));
        int runOffset = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(at + 0x20));
        long allocated = BinaryPrimitives.ReadInt64LittleEndian(r.AsSpan(at + 0x28));
        long real = BinaryPrimitives.ReadInt64LittleEndian(r.AsSpan(at + 0x30));
        long initialized = BinaryPrimitives.ReadInt64LittleEndian(r.AsSpan(at + 0x38));
        if (runOffset < 0x40 || runOffset > length || startVcn < 0 || real < 0 || initialized < 0 || allocated < 0) return null;
        var (runs, ok) = NtfsRunlist.Parse(r.AsSpan(at + runOffset, length - runOffset), startVcn);
        return new NtfsAttribute(type, name, flags, true, [], runs, ok, startVcn, allocated, real, initialized);
    }
}
