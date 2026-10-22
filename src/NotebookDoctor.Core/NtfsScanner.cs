namespace NotebookDoctor.Services;

/// <summary>O que impede recuperar o conteúdo de um arquivo apagado com segurança.</summary>
[Flags]
public enum NtfsLimit
{
    None = 0,
    Compressed = 1,
    Encrypted = 2,
    /// <summary>Os atributos estão espalhados por registros de extensão (arquivo muito fragmentado ou com muitos nomes).</summary>
    SpreadOut = 4,
    /// <summary>A lista de trechos está estragada ou aponta para fora do volume.</summary>
    BadRuns = 8,
    NoData = 16,
}

public enum RecoveryChance { Excellent, Partial, Unknown, Poor, Unsupported }

/// <summary>Um arquivo cujo registro foi marcado como apagado, mas ainda está na MFT.</summary>
public sealed record DeletedFile(long Record, ushort Sequence, string Name, long Parent, ushort ParentSequence, long Size, long InitializedSize, DateTime? Modified,
    byte[]? ResidentData, IReadOnlyList<Extent> Extents, NtfsLimit Limit)
{
    public bool Resident => ResidentData is not null;
    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();
}

public sealed record RecoveryAssessment(RecoveryChance Chance, long UsedClusters, long TotalClusters);

/// <summary>As pastas (inclusive as apagadas) por número de registro, para refazer o caminho de cada arquivo.</summary>
public sealed class NtfsFolders
{
    public const long RootRecord = 5;
    private readonly Dictionary<long, (string Name, long Parent, ushort ParentSequence, ushort Sequence, bool InUse)> _dirs = [];

    public int Count => _dirs.Count;

    public void Add(MftRecord dir, NtfsName name) => _dirs[dir.Number] = (name.Name, name.Parent, name.ParentSequence, dir.Sequence, dir.InUse);

    /// <summary>
    /// O caminho da pasta, de cima para baixo, separado por <c>\</c>. Vazio quando o arquivo estava na raiz. Quando algum degrau se perdeu (o
    /// registro foi reaproveitado por outra pasta, o que o número de sequência denuncia), o resto vira <see cref="Lost"/>.
    /// </summary>
    public string PathOf(long parent, ushort parentSequence)
    {
        var parts = new List<string>();
        long cur = parent;
        ushort seq = parentSequence;
        for (int depth = 0; depth < 64; depth++)
        {
            if (cur == RootRecord) return string.Join('\\', parts.AsEnumerable().Reverse());
            if (!_dirs.TryGetValue(cur, out var d) || !SequenceFits(d.Sequence, seq, d.InUse))
            {
                parts.Add(Lost);
                return string.Join('\\', parts.AsEnumerable().Reverse());
            }
            parts.Add(d.Name);
            cur = d.Parent;
            seq = d.ParentSequence;
        }
        parts.Add(Lost); // laço ou profundidade absurda
        return string.Join('\\', parts.AsEnumerable().Reverse());
    }

    public const string Lost = "[pasta perdida]";

    // Pasta em uso: a sequência é a mesma da referência. Pasta apagada: o NTFS soma 1 à sequência ao apagar.
    private static bool SequenceFits(ushort recordSeq, ushort refSeq, bool inUse) =>
        recordSeq == refSeq || (!inUse && recordSeq == (ushort)(refSeq + 1));
}

public sealed record ScanProgress(long RecordsDone, long RecordsTotal, int Found);

public sealed record NtfsScan(NtfsGeometry Geometry, IReadOnlyList<DeletedFile> Files, NtfsFolders Folders, long RecordsRead, int SkippedEmpty, bool Truncated);

/// <summary>
/// Leva 8.5 (V01/V02, opção B): percorre a MFT de um volume NTFS só em leitura e lista os arquivos apagados. Um registro apagado continua na
/// MFT (com nome, tamanho e onde os dados estavam) até o NTFS reaproveitá-lo para outro arquivo.
/// </summary>
public static class NtfsScanner
{
    public const int MaxFiles = 200_000;
    private const int ChunkBytes = 1 << 20;
    private const int FirstUserRecord = 24; // os registros 0 a 23 são do próprio NTFS ($MFT, $LogFile, $Bitmap…)

    public static NtfsScan Scan(IVolumeReader volume, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var boot = new byte[512];
        if (volume.Read(0, boot) < 512) throw new NtfsException("Não consegui ler o início da unidade.");
        var geo = NtfsBoot.Parse(boot) ?? throw new NtfsException("Esta unidade não parece NTFS.");

        // O registro 0 é o do próprio $MFT: o atributo de dados dele diz onde a tabela inteira está (ela pode estar em vários pedaços)
        var first = new byte[geo.RecordSize];
        if (volume.Read(geo.MftOffset, first) < first.Length) throw new NtfsException("Não consegui ler a tabela de arquivos (MFT).");
        var mft = MftRecord.Parse(first, geo.BytesPerSector, 0);
        var data = mft?.MainData;
        if (mft is null || data is null || !data.NonResident || !data.RunsOk || data.Runs.Count == 0)
            throw new NtfsException("A tabela de arquivos (MFT) está ilegível.");

        long totalRecords = data.RealSize / geo.RecordSize;
        var files = new List<DeletedFile>();
        var folders = new NtfsFolders();
        int skippedEmpty = 0;
        bool truncated = false;
        long number = 0;
        var buffer = new byte[ChunkBytes - ChunkBytes % geo.RecordSize];
        long reported = 0;

        foreach (var run in data.Runs)
        {
            if (run.Lcn < 0) { number += run.Clusters * geo.BytesPerCluster / geo.RecordSize; continue; }
            if (run.Lcn + run.Clusters > geo.TotalClusters) throw new NtfsException("A tabela de arquivos (MFT) aponta para fora da unidade.");
            long runBytes = run.Clusters * geo.BytesPerCluster, done = 0;
            while (done < runBytes && number < totalRecords)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(buffer.Length, Math.Min(runBytes - done, (totalRecords - number) * geo.RecordSize));
                int got = volume.Read(run.Lcn * geo.BytesPerCluster + done, buffer.AsSpan(0, want));
                for (int at = 0; at + geo.RecordSize <= got; at += geo.RecordSize, number++)
                {
                    if (number < FirstUserRecord) continue;
                    // Triagem barata antes de montar o registro: só importam pastas (para os caminhos) e registros apagados
                    if (buffer[at] != 'F' || buffer[at + 1] != 'I' || buffer[at + 2] != 'L' || buffer[at + 3] != 'E') continue;
                    ushort flags = (ushort)(buffer[at + 0x16] | buffer[at + 0x17] << 8);
                    bool inUse = (flags & 1) != 0, dir = (flags & 2) != 0;
                    if (inUse && !dir) continue;
                    var record = MftRecord.Parse(buffer.AsSpan(at, geo.RecordSize).ToArray(), geo.BytesPerSector, number);
                    if (record is null || record.BaseRecord != 0) continue;
                    var name = record.BestName();
                    if (name is null) continue;
                    if (dir) { folders.Add(record, name); continue; }
                    if (inUse) continue;
                    var file = Describe(record, name, geo);
                    if (file is null) { skippedEmpty++; continue; }
                    if (files.Count >= MaxFiles) { truncated = true; continue; }
                    files.Add(file);
                }
                if (got < want) break;
                done += want;
                if (number - reported >= 16_384) { reported = number; progress?.Report(new ScanProgress(number, totalRecords, files.Count)); }
            }
            if (number >= totalRecords) break;
        }
        progress?.Report(new ScanProgress(totalRecords, totalRecords, files.Count));
        return new NtfsScan(geo, files, folders, number, skippedEmpty, truncated);
    }

    private static DeletedFile? Describe(MftRecord record, NtfsName name, NtfsGeometry geo)
    {
        var limit = NtfsLimit.None;
        if (record.HasAttributeList) limit |= NtfsLimit.SpreadOut;
        var data = record.MainData;
        if (data is null)
        {
            if (!limit.HasFlag(NtfsLimit.SpreadOut)) return null; // sem dados e sem lista: pasta vazia ou arquivo sem conteúdo
            return new DeletedFile(record.Number, record.Sequence, name.Name, name.Parent, name.ParentSequence, name.RealSize, name.RealSize, record.Modified, null, [], limit | NtfsLimit.NoData);
        }
        if (data.Compressed) limit |= NtfsLimit.Compressed;
        if (data.Encrypted || (name.Flags & 0x4000) != 0) limit |= NtfsLimit.Encrypted;

        if (!data.NonResident)
            return data.Resident.Length == 0 ? null : new DeletedFile(record.Number, record.Sequence, name.Name, name.Parent, name.ParentSequence, data.Resident.Length, data.Resident.Length,
                record.Modified, data.Resident, [], limit);

        if (data.RealSize == 0) return null;
        if (!data.RunsOk || data.Runs.Any(r => r.Allocated && (r.Lcn + r.Clusters > geo.TotalClusters))) limit |= NtfsLimit.BadRuns;
        long clusters = data.Runs.Sum(r => r.Clusters);
        if (clusters * geo.BytesPerCluster < data.RealSize) limit |= NtfsLimit.BadRuns; // trechos não cobrem o tamanho: registro incompleto
        return new DeletedFile(record.Number, record.Sequence, name.Name, name.Parent, name.ParentSequence, data.RealSize, Math.Min(data.InitializedSize, data.RealSize),
            record.Modified, null, data.Runs, limit);
    }
}

public static class NtfsChance
{
    /// <summary>
    /// O quanto do arquivo dá para esperar de volta. Dentro da MFT (arquivo pequeno), os dados sobrevivem enquanto o registro não é reaproveitado.
    /// Fora dela, o que conta é se algum cluster dele já foi dado a outro arquivo (o bitmap do volume diz): se não, os dados estão como estavam.
    /// </summary>
    public static RecoveryAssessment Assess(DeletedFile file, VolumeBitmap? bitmap)
    {
        if (file.Limit != NtfsLimit.None || file.Size <= 0) return new(RecoveryChance.Unsupported, 0, 0);
        if (file.Resident) return new(RecoveryChance.Excellent, 0, 0);
        long total = 0, used = 0;
        foreach (var e in file.Extents.Where(e => e.Allocated))
        {
            total += e.Clusters;
            if (bitmap is null) continue;
            used += UsedIn(bitmap, e);
        }
        if (total == 0) return new(RecoveryChance.Unsupported, 0, 0);
        if (bitmap is null) return new(RecoveryChance.Unknown, 0, total);
        var chance = used == 0 ? RecoveryChance.Excellent : used >= total ? RecoveryChance.Poor : RecoveryChance.Partial;
        return new(chance, used, total);
    }

    private static long UsedIn(VolumeBitmap bitmap, Extent e)
    {
        long used = 0;
        for (long c = e.Lcn; c < e.Lcn + e.Clusters; c++) if (bitmap.IsUsed(c)) used++;
        return used;
    }

    /// <summary>
    /// Afina a avaliação com o que se vê no começo do arquivo: um começo que não combina com a extensão (outro arquivo escreveu ali) ou que é só
    /// zeros (o SSD apagou os blocos pelo TRIM) derruba a chance.
    /// </summary>
    public static RecoveryChance Refine(RecoveryChance chance, SignatureCheck signature, bool allZero)
    {
        if (chance is RecoveryChance.Unsupported or RecoveryChance.Poor) return chance;
        if (allZero) return RecoveryChance.Poor;
        if (signature == SignatureCheck.Mismatch) return chance == RecoveryChance.Excellent ? RecoveryChance.Partial : RecoveryChance.Poor;
        return chance;
    }

    public static int Order(RecoveryChance c) => c switch
    {
        RecoveryChance.Excellent => 0, RecoveryChance.Partial => 1, RecoveryChance.Unknown => 2, RecoveryChance.Poor => 3, _ => 4,
    };
}
