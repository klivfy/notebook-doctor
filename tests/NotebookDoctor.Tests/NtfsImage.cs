using System.Buffers.Binary;
using System.Text;
using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

/// <summary>Uma imagem de volume na memória, para testar o motor sem disco de verdade.</summary>
internal sealed class MemoryVolume(byte[] data) : IVolumeReader
{
    public long Length => data.Length;

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset >= data.Length) return 0;
        int n = (int)Math.Min(buffer.Length, data.Length - offset);
        data.AsSpan((int)offset, n).CopyTo(buffer);
        return n;
    }
}

/// <summary>Monta, byte a byte, um volume NTFS pequeno: setor de boot, MFT com registros e clusters de dados.</summary>
internal sealed class NtfsImage
{
    public const int Bps = 512, Spc = 8, Bpc = 4096, RecordSize = 1024, TotalClusters = 256, MftLcn = 4, MftRecords = 64;
    public readonly byte[] Bytes = new byte[TotalClusters * Bpc];

    public NtfsImage()
    {
        "NTFS    "u8.CopyTo(Bytes.AsSpan(3));
        BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(0x0B), Bps);
        Bytes[0x0D] = Spc;
        BinaryPrimitives.WriteInt64LittleEndian(Bytes.AsSpan(0x28), (long)TotalClusters * Spc);
        BinaryPrimitives.WriteInt64LittleEndian(Bytes.AsSpan(0x30), MftLcn);
        Bytes[0x40] = unchecked((byte)-10); // 2^10 = 1024 bytes por registro
        PutRecord(0, Rec.Build(1, 1, 0, Rec.FileName(5, 5, "$MFT"), Rec.NonResident(0x80, "", MftRecords * RecordSize, MftRecords * RecordSize, 0, (MftLcn, MftRecords * RecordSize / Bpc))));
        PutRecord(5, Rec.Build(5, 3, 0, Rec.FileName(5, 5, ".")));
    }

    public void PutRecord(int number, byte[] record) => record.CopyTo(Bytes, MftLcn * Bpc + number * RecordSize);

    public void PutClusters(long lcn, byte[] data) => data.CopyTo(Bytes, lcn * Bpc);

    public void Fill(long lcn, long clusters, byte value) => Array.Fill(Bytes, value, (int)(lcn * Bpc), (int)(clusters * Bpc));

    /// <summary>Bitmap com estes clusters usados. Os do setor de boot e da MFT (0 a 19) entram sempre.</summary>
    public static VolumeBitmap Bitmap(params long[] used)
    {
        var bits = new byte[TotalClusters / 8];
        foreach (long c in Enumerable.Range(0, 20).Select(i => (long)i).Concat(used)) bits[c >> 3] |= (byte)(1 << (int)(c & 7));
        return new VolumeBitmap(bits, TotalClusters);
    }

    public NtfsGeometry Geometry => NtfsBoot.Parse(Bytes)!;
}

internal static class Rec
{
    public static long Ticks(DateTime utc) => utc.ToFileTimeUtc();

    public static byte[] Build(ushort sequence, ushort flags, long baseRecord, params byte[][] attributes) =>
        Build(sequence, flags, baseRecord, corruptFixup: false, attributes);

    public static byte[] Build(ushort sequence, ushort flags, long baseRecord, bool corruptFixup, params byte[][] attributes)
    {
        var r = new byte[NtfsImage.RecordSize];
        "FILE"u8.CopyTo(r);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(4), 0x30);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(6), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x10), sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x14), 0x38);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x16), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x1C), NtfsImage.RecordSize);
        BinaryPrimitives.WriteInt64LittleEndian(r.AsSpan(0x20), baseRecord);
        int at = 0x38;
        foreach (var a in attributes) { a.CopyTo(r, at); at += a.Length; }
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(at), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x18), (uint)(at + 8));

        // Marcador de integridade: os 2 últimos bytes de cada setor vão para a tabela e dão lugar ao marcador
        r[0x30] = 0x01; r[0x31] = 0x00;
        for (int s = 1; s <= 2; s++)
        {
            int pos = s * 512 - 2;
            r[0x30 + s * 2] = r[pos]; r[0x31 + s * 2] = r[pos + 1];
            r[pos] = 0x01; r[pos + 1] = 0x00;
        }
        if (corruptFixup) r[510] = 0x77;
        return r;
    }

    private static byte[] Header(uint type, string name, int total, bool nonResident, ushort flags)
    {
        var a = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(a, type);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(4), (uint)total);
        a[8] = nonResident ? (byte)1 : (byte)0;
        a[9] = (byte)name.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(12), flags);
        return a;
    }

    private static int Align8(int n) => (n + 7) & ~7;

    public static byte[] Resident(uint type, string name, byte[] content, ushort flags = 0)
    {
        int nameBytes = name.Length * 2, contentAt = Align8(0x18 + nameBytes);
        var a = Header(type, name, Align8(contentAt + content.Length), false, flags);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(10), 0x18);
        Encoding.Unicode.GetBytes(name).CopyTo(a, 0x18);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(0x10), (uint)content.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(0x14), (ushort)contentAt);
        content.CopyTo(a, contentAt);
        return a;
    }

    public static byte[] NonResident(uint type, string name, long real, long initialized, ushort flags, params (long Lcn, long Clusters)[] runs)
    {
        var encoded = EncodeRuns(runs);
        int runAt = Align8(0x40 + name.Length * 2);
        var a = Header(type, name, Align8(runAt + encoded.Length), true, flags);
        long clusters = runs.Sum(r => r.Clusters);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(10), 0x40);
        Encoding.Unicode.GetBytes(name).CopyTo(a, 0x40);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x18), clusters - 1);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(0x20), (ushort)runAt);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x28), clusters * NtfsImage.Bpc);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x30), real);
        BinaryPrimitives.WriteInt64LittleEndian(a.AsSpan(0x38), initialized);
        encoded.CopyTo(a, runAt);
        return a;
    }

    /// <summary>Lcn -1 é um buraco. Cada trecho guarda a distância até o anterior, com sinal.</summary>
    public static byte[] EncodeRuns(params (long Lcn, long Clusters)[] runs)
    {
        var list = new List<byte>();
        long previous = 0;
        foreach (var (lcn, length) in runs)
        {
            var len = Unsigned(length);
            if (lcn < 0) { list.Add((byte)len.Length); list.AddRange(len); continue; }
            var off = Signed(lcn - previous);
            previous = lcn;
            list.Add((byte)(off.Length << 4 | len.Length));
            list.AddRange(len);
            list.AddRange(off);
        }
        list.Add(0);
        return [.. list];
    }

    private static byte[] Unsigned(long v)
    {
        var l = new List<byte>();
        do { l.Add((byte)(v & 0xFF)); v >>= 8; } while (v > 0);
        return [.. l];
    }

    private static byte[] Signed(long v)
    {
        for (int n = 1; n <= 8; n++)
        {
            if (n < 8 && (v < -(1L << (8 * n - 1)) || v >= 1L << (8 * n - 1))) continue;
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = (byte)(v >> (8 * i));
            return b;
        }
        throw new InvalidOperationException();
    }

    public static byte[] StdInfo(DateTime modifiedUtc)
    {
        var c = new byte[0x48];
        BinaryPrimitives.WriteInt64LittleEndian(c.AsSpan(8), Ticks(modifiedUtc));
        return Resident(0x10, "", c);
    }

    public static byte[] FileName(long parent, ushort parentSequence, string name, byte ns = 1, long real = 0, uint flags = 0)
    {
        var c = new byte[0x42 + name.Length * 2];
        BinaryPrimitives.WriteUInt64LittleEndian(c, (ulong)parent | (ulong)parentSequence << 48);
        BinaryPrimitives.WriteInt64LittleEndian(c.AsSpan(0x30), real);
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(0x38), flags);
        c[0x40] = (byte)name.Length;
        c[0x41] = ns;
        Encoding.Unicode.GetBytes(name).CopyTo(c, 0x42);
        return Resident(0x30, "", c);
    }

    public static byte[] AttributeList() => Resident(0x20, "", new byte[32]);
}
