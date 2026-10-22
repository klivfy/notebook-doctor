using System.Numerics;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

/// <summary>Um trecho contíguo de um arquivo no disco: <see cref="Lcn"/> -1 é um buraco (arquivo esparso), sem clusters.</summary>
public sealed record Extent(long Vcn, long Lcn, long Clusters)
{
    public bool Allocated => Lcn >= 0;
}

public enum DefragAction { Analyze, Defrag, ReTrim }

/// <summary>O que dá para fazer numa unidade, e o motivo quando não dá.</summary>
public sealed record DefragOptions(bool Analyze, bool Defrag, bool ReTrim, string Reason);

/// <summary>
/// Leva 8.4 (D01–D03): regras de desfragmentação. Desfragmentar é só para HD: em SSD não traz ganho e gasta escritas, então nas unidades
/// de estado sólido (ou quando o app não consegue saber) o que sobra é analisar e, no SSD, refazer o TRIM.
/// </summary>
public static class DefragRules
{
    public static DefragOptions Check(DriveFacts d)
    {
        if (!d.Ready) return new(false, false, false, "A unidade não está pronta");
        if (d.Kind is not (DriveKind.Fixed or DriveKind.Removable)) return new(false, false, false, "Não é um disco local: rede e CD não entram");
        if (!d.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) return new(false, false, false, "Só unidades NTFS");
        return d.SolidState switch
        {
            true => new(true, false, true, "É um SSD (ou memória flash): desfragmentar não ajuda e gasta escritas. O app refaz o TRIM em vez disso"),
            false => new(true, true, false, ""),
            null => new(true, false, false, "Não consegui saber se é HD ou SSD, então não desfragmento por precaução"),
        };
    }

    /// <summary>Só uma letra de A a Z: é o que entra no script do PowerShell.</summary>
    public static bool ValidLetter(string? letter) => letter is { Length: 1 } && char.IsAsciiLetter(letter[0]);

    public static string Script(string letter, DefragAction action)
    {
        if (!ValidLetter(letter)) throw new ArgumentException("Letra de unidade inválida.", nameof(letter));
        string flag = action switch { DefragAction.Analyze => "-Analyze", DefragAction.Defrag => "-Defrag", _ => "-ReTrim" };
        // 4>&1 junta o fluxo "verbose" na saída normal, que é onde o Windows conta o andamento e o resultado
        return $"$ErrorActionPreference='Stop'; Optimize-Volume -DriveLetter {char.ToUpperInvariant(letter[0])} {flag} -Verbose 4>&1 | Out-String -Stream";
    }

    public static int? ParsePercent(string line) => RecoveryRules.ParsePercent(line);

    // ---------- arquivo e pasta (D02) ----------

    private static readonly string[] NeverMove = ["pagefile.sys", "hiberfil.sys", "swapfile.sys"];

    /// <summary>Se o app pode tentar mover os clusters deste arquivo. Atributos vêm do sistema de arquivos.</summary>
    public static (bool Ok, string Reason) CanMove(string path, FileAttributes attributes, long length, long clusterBytes)
    {
        string name = Paths.Leaf(path);
        if (name.StartsWith('$') || NeverMove.Contains(name, StringComparer.OrdinalIgnoreCase)) return (false, "Arquivo do sistema de arquivos");
        if (attributes.HasFlag(FileAttributes.ReparsePoint)) return (false, "É um link");
        if (attributes.HasFlag(FileAttributes.SparseFile)) return (false, "Arquivo esparso");
        if (attributes.HasFlag(FileAttributes.Encrypted)) return (false, "Criptografado pelo NTFS");
        if (OneDriveRule.IsOnlineOnly((int)attributes)) return (false, "Só na nuvem");
        if (length <= clusterBytes) return (false, "Cabe num cluster: não fragmenta");
        return (true, "");
    }

    /// <summary>Quantos pedaços separados o arquivo tem: trechos que se emendam no disco contam como um só.</summary>
    public static int Fragments(IReadOnlyList<Extent> extents)
    {
        int count = 0;
        long nextLcn = -2;
        foreach (var e in extents.Where(e => e.Allocated).OrderBy(e => e.Vcn))
        {
            if (e.Lcn != nextLcn) count++;
            nextLcn = e.Lcn + e.Clusters;
        }
        return count;
    }

    public static long TotalClusters(IReadOnlyList<Extent> extents) => extents.Where(e => e.Allocated).Sum(e => e.Clusters);

    public static bool HasHoles(IReadOnlyList<Extent> extents) => extents.Any(e => !e.Allocated);

    public const int MoveChunkClusters = 32768;

    /// <summary>
    /// Os passos para juntar o arquivo numa corrida livre que começa em <paramref name="target"/>: um (vcn, lcn de destino, clusters) por pedaço
    /// de até <see cref="MoveChunkClusters"/>, na ordem do arquivo, de modo que no fim ele fique em clusters seguidos.
    /// </summary>
    public static List<(long Vcn, long Lcn, long Clusters)> MoveSteps(IReadOnlyList<Extent> extents, long target)
    {
        var steps = new List<(long, long, long)>();
        long dest = target;
        foreach (var e in extents.Where(e => e.Allocated).OrderBy(e => e.Vcn))
            for (long done = 0; done < e.Clusters; done += MoveChunkClusters)
            {
                long n = Math.Min(MoveChunkClusters, e.Clusters - done);
                steps.Add((e.Vcn + done, dest, n));
                dest += n;
            }
        return steps;
    }
}

/// <summary>
/// Leitura dos blocos devolvidos pelo Windows: <c>FSCTL_GET_RETRIEVAL_POINTERS</c> (onde o arquivo está) e <c>FSCTL_GET_VOLUME_BITMAP</c>
/// (o que está usado no volume).
/// </summary>
public static class VolumeStructures
{
    /// <summary>
    /// RETRIEVAL_POINTERS_BUFFER: contagem de trechos (4 bytes, mais 4 de enchimento), VCN inicial (8) e, a partir do byte 16, pares
    /// (próximo VCN, LCN). Devolve os trechos e o próximo VCN (para continuar quando o Windows entregar só uma parte).
    /// </summary>
    public static (List<Extent> Extents, long NextVcn) ParseRetrievalPointers(byte[] buffer, int length)
    {
        var list = new List<Extent>();
        if (length < 16) return (list, 0);
        int count = BitConverter.ToInt32(buffer, 0);
        long vcn = BitConverter.ToInt64(buffer, 8);
        long next = vcn;
        for (int i = 0; i < count; i++)
        {
            int off = 16 + i * 16;
            if (off + 16 > length) break;
            long nextVcn = BitConverter.ToInt64(buffer, off), lcn = BitConverter.ToInt64(buffer, off + 8);
            if (nextVcn <= vcn) break; // dado estragado: sem avançar não há trecho
            list.Add(new Extent(vcn, lcn, nextVcn - vcn));
            vcn = nextVcn;
            next = nextVcn;
        }
        return (list, next);
    }
}

/// <summary>O mapa de clusters usados do volume (1 bit por cluster, o bit menos significativo primeiro).</summary>
public sealed class VolumeBitmap(byte[] bits, long totalClusters)
{
    public long TotalClusters { get; } = totalClusters;
    private readonly byte[] _bits = bits;

    public bool IsUsed(long lcn) => lcn < 0 || lcn >= TotalClusters || (_bits[lcn >> 3] & (1 << (int)(lcn & 7))) != 0;

    public long UsedClusters
    {
        get
        {
            long used = 0;
            long fullBytes = TotalClusters >> 3;
            for (long i = 0; i < fullBytes; i++) used += BitOperations.PopCount(_bits[i]);
            for (long lcn = fullBytes << 3; lcn < TotalClusters; lcn++) if (IsUsed(lcn)) used++;
            return used;
        }
    }

    /// <summary>
    /// A primeira corrida de <paramref name="clusters"/> clusters livres seguidos a partir de <paramref name="from"/>; -1 se não houver.
    /// Os clusters que o arquivo ocupa agora já contam como usados no bitmap, então a corrida nunca cai em cima dele.
    /// </summary>
    public long FindFreeRun(long clusters, long from = 0)
    {
        if (clusters <= 0) return -1;
        long run = 0, start = from;
        for (long lcn = Math.Max(0, from); lcn < TotalClusters; lcn++)
        {
            // pula de byte em byte quando está tudo ocupado
            if ((lcn & 7) == 0 && lcn + 8 <= TotalClusters && _bits[lcn >> 3] == 0xFF) { run = 0; lcn += 7; start = lcn + 1; continue; }
            if (IsUsed(lcn)) { run = 0; start = lcn + 1; continue; }
            if (++run >= clusters) return start;
        }
        return -1;
    }

    /// <summary>Quanto de cada região está usado (0 a 1), em <paramref name="cells"/> regiões do mesmo tamanho: o mapa da unidade (D03).</summary>
    public double[] Map(int cells)
    {
        var result = new double[Math.Max(1, cells)];
        if (TotalClusters <= 0) return result;
        for (int i = 0; i < result.Length; i++)
        {
            long from = TotalClusters * i / result.Length, to = TotalClusters * (i + 1) / result.Length;
            long used = 0;
            long lcn = from;
            for (; lcn < to && (lcn & 7) != 0; lcn++) if (IsUsed(lcn)) used++;
            for (; lcn + 8 <= to; lcn += 8) used += BitOperations.PopCount(_bits[lcn >> 3]);
            for (; lcn < to; lcn++) if (IsUsed(lcn)) used++;
            result[i] = to > from ? (double)used / (to - from) : 0;
        }
        return result;
    }

    /// <summary>
    /// VOLUME_BITMAP_BUFFER: LCN inicial (8 bytes, múltiplo de 8), quantidade de bits (8 bytes) e, a partir do byte 16, os bits.
    /// Devolve o LCN inicial e os bytes de bitmap deste pedaço.
    /// </summary>
    public static (long StartingLcn, long BitmapSize, byte[] Bytes) ParseChunk(byte[] buffer, int length)
    {
        if (length < 16) return (0, 0, []);
        long start = BitConverter.ToInt64(buffer, 0), size = BitConverter.ToInt64(buffer, 8);
        int available = length - 16;
        int bytes = (int)Math.Min(available, (size + 7) / 8);
        var data = new byte[Math.Max(0, bytes)];
        Array.Copy(buffer, 16, data, 0, data.Length);
        return (start, size, data);
    }
}
