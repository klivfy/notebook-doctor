using System.Text;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Services;

// ---------- apagar arquivos de vez (R21) ----------

public sealed record EraseBlock(string Path, string Reason);

/// <summary>O que vai ser apagado de vez: arquivos e pastas (a pasta só sai se ficar vazia), com o que ficou de fora e por quê.</summary>
public sealed class ErasePlan
{
    public List<string> Files { get; init; } = [];
    /// <summary>Pastas que a pessoa escolheu e subpastas delas, da mais funda para a mais rasa.</summary>
    public List<string> Dirs { get; init; } = [];
    public List<EraseBlock> Blocked { get; init; } = [];
    public long Bytes { get; set; }
    /// <summary>A lista foi cortada no limite de arquivos: apagar de vez não faz "só uma parte" sem a pessoa saber.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Leva 8.2 (R21): o que pode ser apagado de vez. Apagar de vez não tem volta (nem Lixeira, nem recuperação), então a lista de bloqueios é
/// larga: o sistema, os programas, as pastas-raiz do perfil, a nuvem (a cópia lá continua) e o próprio app.
/// </summary>
public static class EraseRules
{
    public const int MaxFiles = 100_000;

    /// <summary>Motivo para NÃO apagar este caminho, ou null. Pastas de documentos só estão protegidas por inteiro: o que há dentro pode sair.</summary>
    public static string? Blocked(string path, KnownPlaces places, IEnumerable<string> cloudFolders, IEnumerable<string> appFolders)
    {
        string p = Paths.Clean(path);
        if (p.Length == 0) return "Caminho vazio";
        if (p.StartsWith(@"\\", StringComparison.Ordinal)) return "Pasta de rede: não dá para garantir o que acontece com os dados";
        if (p.Length <= 3 || !Regex.IsMatch(p, @"^[A-Za-z]:\\")) return "A raiz da unidade ou um caminho que não é de disco local";
        foreach (var dir in new[] { places.Windows, places.ProgramFiles, places.ProgramFilesX86, places.CommonFiles, places.CommonFilesX86, places.ProgramData })
            if (dir.Length > 0 && Paths.IsUnder(p, dir)) return "Pasta do Windows ou de programas";
        if (Regex.IsMatch(p, @"^[A-Za-z]:\\(System Volume Information|\$Recycle\.Bin|Recovery|Boot)(\\|$)", RegexOptions.IgnoreCase)) return "Pasta de sistema do disco";
        // A pasta toda, ou uma pasta que contém uma dessas (o perfil, AppData, Documentos, Área de Trabalho…), não pode ser escolhida
        foreach (var root in places.Roots)
            if (Paths.Same(p, root) || Paths.IsUnder(root, p)) return "Pasta importante do seu perfil: escolha o que há dentro dela";
        foreach (var cloud in cloudFolders)
            if (cloud.Length > 0 && Paths.IsUnder(p, cloud)) return "Pasta do OneDrive: a cópia na nuvem continua e volta para cá";
        foreach (var app in appFolders)
            if (app.Length > 0 && (Paths.IsUnder(p, app) || Paths.IsUnder(app, p))) return "É do próprio Notebook Doctor";
        return null;
    }

    /// <summary>
    /// Monta o plano: arquivos (dentro das pastas escolhidas, sem seguir links) e pastas a esvaziar. O que o sistema de arquivos não deixa ver,
    /// fica de fora da lista; o executor confere tudo de novo, arquivo por arquivo.
    /// </summary>
    /// <param name="realPath">Caminho de verdade, depois de seguir links e atalhos de pasta (o Windows diz). Se for diferente do escolhido, o caminho passa por um link e fica de fora: apagar "por dentro" de um atalho de pasta acabaria noutro lugar.</param>
    public static ErasePlan Plan(IEnumerable<string> chosen, ILeftoverSystem sys, KnownPlaces places, IEnumerable<string> cloudFolders, IEnumerable<string> appFolders,
                                 Func<string, string?>? realPath = null)
    {
        var cloud = cloudFolders.ToList();
        var apps = appFolders.ToList();
        var plan = new ErasePlan();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in chosen)
        {
            string path = Paths.Clean(raw);
            if (Blocked(path, places, cloud, apps) is { } reason) { plan.Blocked.Add(new EraseBlock(path, reason)); continue; }
            if (realPath is not null)
            {
                string? real = realPath(path);
                if (real is null) { plan.Blocked.Add(new EraseBlock(path, "Não existe mais")); continue; }
                if (!Paths.Same(real, path)) { plan.Blocked.Add(new EraseBlock(path, "O caminho passa por um link ou atalho de pasta")); continue; }
            }
            if (sys.FileExists(path))
            {
                if (seen.Add(path)) { plan.Files.Add(path); plan.Bytes += sys.Size(path); }
            }
            else if (sys.DirectoryExists(path))
            {
                var dirs = new List<string>();
                CollectDirs(path, sys, dirs, plan);
                foreach (var d in dirs) plan.Dirs.Add(d);
                foreach (var file in sys.Files(path))
                {
                    if (plan.Files.Count >= MaxFiles) { plan.Truncated = true; break; }
                    if (seen.Add(file)) { plan.Files.Add(file); plan.Bytes += sys.Size(file); }
                }
            }
            else plan.Blocked.Add(new EraseBlock(path, "Não existe mais"));
        }
        // Da subpasta mais funda para a mais rasa: uma pasta só sai depois das que estão dentro dela
        plan.Dirs.Sort((a, b) => b.Length.CompareTo(a.Length));
        return plan;
    }

    private static void CollectDirs(string dir, ILeftoverSystem sys, List<string> dirs, ErasePlan plan)
    {
        dirs.Add(dir);
        if (dirs.Count > 50_000) { plan.Truncated = true; return; }
        foreach (var child in sys.Directories(dir)) CollectDirs(child, sys, dirs, plan);
    }

    /// <summary>Nome aleatório de 8 a 12 letras e números, sem extensão, para renomear antes de apagar.</summary>
    public static string RandomName(Random rng)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var sb = new StringBuilder();
        for (int i = 0, n = rng.Next(8, 13); i < n; i++) sb.Append(alphabet[rng.Next(alphabet.Length)]);
        return sb.ToString();
    }

    /// <summary>Avisos que a pessoa precisa ler antes de apagar de vez: o que o app não consegue garantir.</summary>
    public static List<string> Warnings(bool? solidState, bool anyCompressedOrEncrypted)
    {
        var list = new List<string>();
        if (solidState != false)
            list.Add("Em SSD, pen drive e cartão de memória não há garantia: o disco decide onde grava, e cópias antigas podem sobrar. Para dados sensíveis, use BitLocker e apague a chave.");
        if (anyCompressedOrEncrypted)
            list.Add("Arquivos comprimidos, esparsos ou criptografados pelo NTFS podem deixar blocos antigos em outro lugar do disco. Depois, limpe também o espaço livre.");
        list.Add("Cópias em outro lugar (backup, e-mail, nuvem, Lixeira) não são tocadas.");
        return list;
    }
}

// ---------- limpar o espaço livre (R20) ----------

public static class WipeRules
{
    public const long BlockBytes = 256L << 20;

    /// <summary>
    /// Quanto deixar livre. Na unidade do sistema, o Windows precisa de folga para continuar funcionando (atualizações, arquivo de paginação, o
    /// próprio app); nas outras, só um pouco.
    /// </summary>
    public static long Reserve(bool systemDrive, long totalBytes) =>
        systemDrive ? Math.Max(512L << 20, totalBytes / 100) : Math.Min(64L << 20, Math.Max(8L << 20, totalBytes / 200));

    /// <summary>Quanto ainda vai ser escrito: o espaço livre menos a reserva, nunca negativo.</summary>
    public static long Target(long freeBytes, long reserve, long? max = null) =>
        Math.Min(Math.Max(0, freeBytes - reserve), max ?? long.MaxValue);

    /// <summary>Tamanho do próximo arquivo temporário: um bloco, ou o que falta.</summary>
    public static long NextBlock(long freeBytes, long reserve, long writtenSoFar, long? max = null)
    {
        long left = Target(freeBytes, reserve, max.HasValue ? max.Value - writtenSoFar : null);
        return Math.Min(BlockBytes, left);
    }

    public static int Percent(long done, long target) => target <= 0 ? 100 : (int)Math.Clamp(done * 100 / target, 0, 100);

    /// <summary>Pasta temporária dentro da unidade: na do sistema, a pasta Temp do usuário; nas outras, uma pasta na raiz.</summary>
    public static string TempDir(string driveRoot, string userTemp, Guid id)
    {
        string root = Paths.Clean(driveRoot);
        string name = $"NotebookDoctor-limpeza-{id:N}";
        return Paths.IsUnder(userTemp, root) ? Paths.Join(userTemp, name) : Paths.Join(root, name);
    }

    public static List<string> Warnings(bool systemDrive, bool? solidState)
    {
        var list = new List<string>();
        if (systemDrive) list.Add("Enquanto roda, o disco do sistema fica quase cheio: o Windows pode reclamar de pouco espaço. Feche os outros programas e não deixe o PC dormir.");
        if (solidState != false) list.Add("Em SSD, pen drive e cartão de memória não há garantia: o disco decide onde grava. Isso reduz o que dá para recuperar, mas não é apagamento garantido.");
        list.Add("Arquivos muito pequenos guardados na própria tabela do disco (MFT) não são cobertos.");
        list.Add("Pode levar horas em disco grande. Dá para cancelar a qualquer momento: o que foi escrito é apagado.");
        return list;
    }
}

// ---------- apagar uma unidade externa inteira (C11) ----------

public enum DriveKind { Unknown, Fixed, Removable, Network, CdRom, Ram }

/// <summary>Fatos de uma unidade, lidos pelo Windows: o suficiente para decidir se dá para formatá-la com segurança.</summary>
public sealed record DriveFacts(string Letter, DriveKind Kind, bool Ready, bool IsSystem, bool HostsApp, int BusType, string FileSystem, string Label, long TotalBytes, long FreeBytes, bool? SolidState)
{
    public string Root => Letter + @":\";
}

public static class DriveWipeRules
{
    // STORAGE_BUS_TYPE: 4 = IEEE 1394, 7 = USB, 12 = SD, 13 = MMC
    public static readonly int[] ExternalBuses = [4, 7, 12, 13];
    private static readonly string[] Formats = ["NTFS", "FAT32", "exFAT", "FAT"];
    public const long SlowFormatBytes = 512L * 1024 * 1024 * 1024;

    /// <summary>Se a unidade pode ser apagada inteira, ou o motivo de não poder. Só externa, pronta, fora do sistema e fora do app.</summary>
    public static (bool Allowed, string Reason) Check(DriveFacts d)
    {
        if (d.Letter.Length != 1 || !char.IsAsciiLetter(d.Letter[0])) return (false, "Letra de unidade inválida");
        if (d.IsSystem) return (false, "É a unidade do Windows");
        if (d.HostsApp) return (false, "É a unidade onde o Notebook Doctor está");
        if (!d.Ready) return (false, "A unidade não está pronta");
        if (d.Kind is not (DriveKind.Removable or DriveKind.Fixed)) return (false, "Não é um disco local: rede e CD não entram");
        if (!ExternalBuses.Contains(d.BusType)) return (false, "Não é uma unidade externa (USB, cartão): discos internos não entram");
        if (!Formats.Contains(d.FileSystem, StringComparer.OrdinalIgnoreCase)) return (false, "Sistema de arquivos que o app não reconhece");
        return (true, "");
    }

    /// <summary>Rótulo seguro para o <c>/V:</c>: letras, números, espaço, hífen e sublinhado; FAT aceita 11 caracteres em maiúsculas.</summary>
    public static string SafeLabel(string? label, string fileSystem)
    {
        string clean = Regex.Replace(label ?? "", @"[^A-Za-z0-9 _\-]", "").Trim();
        bool fat = fileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase);
        if (fat) clean = clean.ToUpperInvariant();
        int max = fat ? 11 : 32;
        if (clean.Length > max) clean = clean[..max].Trim();
        return clean.Length > 0 ? clean : "DISCO";
    }

    /// <summary>
    /// Argumentos do <c>format.com</c>: formatação completa (sem /Q), uma passada de zeros em todo setor (<c>/P:1</c>), mesmo sistema de
    /// arquivos e rótulo seguro. Falha se a unidade não passa em <see cref="Check"/>.
    /// </summary>
    public static string[] FormatArgs(DriveFacts d)
    {
        var (ok, reason) = Check(d);
        if (!ok) throw new InvalidOperationException(reason);
        string fs = d.FileSystem.Equals("exFAT", StringComparison.OrdinalIgnoreCase) ? "exFAT" : d.FileSystem.ToUpperInvariant();
        return [d.Letter.ToUpperInvariant() + ":", $"/FS:{fs}", "/P:1", "/Y", $"/V:{SafeLabel(d.Label, d.FileSystem)}"];
    }

    /// <summary>"12 por cento concluído." → 12; texto que não começa com número → null (o idioma muda o resto da frase).</summary>
    public static int? ParseProgress(string line)
    {
        var m = Regex.Match(line, @"^\s*(\d{1,3})\s+\S");
        return m.Success && int.Parse(m.Groups[1].Value) is var n and >= 0 and <= 100 ? n : null;
    }

    /// <summary>O que a pessoa precisa digitar para confirmar: a letra e, se a unidade tem nome, o nome.</summary>
    public static (string Letter, string? Label) Confirmation(DriveFacts d) =>
        (d.Letter.ToUpperInvariant(), string.IsNullOrWhiteSpace(d.Label) ? null : d.Label.Trim());

    public static bool Confirmed(DriveFacts d, string typedLetter, string typedLabel)
    {
        var (letter, label) = Confirmation(d);
        string l = typedLetter.Trim().TrimEnd(':', '\\');
        if (!string.Equals(l, letter, StringComparison.OrdinalIgnoreCase)) return false;
        return label is null || string.Equals(typedLabel.Trim(), label, StringComparison.OrdinalIgnoreCase);
    }

    public static List<string> Warnings(DriveFacts d)
    {
        var list = new List<string> { "Tudo o que está na unidade será apagado e não dá para voltar atrás. Confira a letra e o nome." };
        if (d.SolidState != false) list.Add("Em pen drive, cartão e SSD não há garantia de apagar os blocos antigos: o disco decide onde grava.");
        if (d.TotalBytes >= SlowFormatBytes) list.Add("Unidade grande: a formatação completa pode levar muitas horas.");
        list.Add("Precisa de administrador, e não dá para cancelar depois que começa.");
        return list;
    }
}
