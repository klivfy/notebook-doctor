using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

/// <summary>
/// Leva 8.5: o motor contra um NTFS de verdade. Cria um disco virtual (VHD), apaga arquivos nele, lê a MFT pelo volume cru e confere que o
/// conteúdo recuperado é idêntico ao original. Precisa de administrador (o runner da CI tem); sem ele, ou sem o diskpart, o teste não roda.
/// </summary>
[SupportedOSPlatform("windows")]
public class NtfsIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-ntfs-int-" + Guid.NewGuid().ToString("N"));
    private string? _vhd;
    private bool _attached;

    public NtfsIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (_attached && _vhd is not null) { try { DiskPart($"select vdisk file=\"{_vhd}\"", "detach vdisk"); } catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { } }
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private string DiskPart(params string[] lines)
    {
        string script = Path.Combine(_dir, $"dp-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(script, lines);
        var psi = new ProcessStartInfo("diskpart.exe", $"/s \"{script}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(120_000);
        return output + (p.ExitCode != 0 ? $"\n[exit {p.ExitCode}]" : "");
    }

    // Um arquivo apagado antes de o Windows gravar os dados dele nunca chega ao disco: força a gravação antes de apagar
    private static void WriteDurable(string path, byte[] data)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        fs.Write(data, 0, data.Length);
        fs.Flush(true);
    }

    private static string Describe(byte[] expected, byte[] actual)
    {
        int diff = 0, first = -1;
        for (int i = 0; i < Math.Min(expected.Length, actual.Length); i++)
            if (expected[i] != actual[i]) { diff++; if (first < 0) first = i; }
        return $"tamanho {actual.Length}/{expected.Length}, {diff} bytes diferentes, primeiro em {first}, zeros no começo: {actual.Take(64).All(b => b == 0)}";
    }

    private static char FreeLetter() => "ZYXWV".FirstOrDefault(c => !Directory.Exists(c + @":\"));

    [WindowsFact]
    public void Setor_de_boot_do_volume_cru_bate_com_o_que_o_Windows_diz()
    {
        if (!IsAdmin()) return;
        string root = Path.GetPathRoot(_dir)!;
        if (!string.Equals(new DriveInfo(root).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) return;
        using var raw = new RawVolume(root[0]);
        var boot = new byte[512];
        Assert.Equal(512, raw.Read(0, boot));
        var geo = NtfsBoot.Parse(boot);
        Assert.NotNull(geo);
        using var handle = VolumeApi.OpenVolume(root[0]);
        var data = VolumeApi.GetVolumeData(handle);
        Assert.Equal(data.BytesPerCluster, geo!.BytesPerCluster);
        Assert.InRange(geo.TotalClusters, data.TotalClusters - 8, data.TotalClusters + 8); // o setor de boot não conta o último setor (reserva)
        // leitura em posição qualquer, não alinhada ao setor
        var odd = new byte[100];
        Assert.Equal(100, raw.Read(3, odd));
        Assert.Equal(boot[3..103], odd);
        Assert.True(raw.Length >= data.TotalClusters * data.BytesPerCluster);
    }

    [WindowsFact]
    public void Disco_virtual_arquivos_apagados_voltam_identicos()
    {
        if (!IsAdmin()) return;
        char letter = FreeLetter();
        if (letter == '\0') return;
        _vhd = Path.Combine(_dir, "teste.vhd");
        string made = DiskPart($"create vdisk file=\"{_vhd}\" maximum=100 type=expandable", $"select vdisk file=\"{_vhd}\"", "attach vdisk",
            "create partition primary", "format fs=ntfs quick label=NDTEST", $"assign letter={letter}");
        _attached = true;
        for (int i = 0; i < 60 && !Directory.Exists(letter + @":\"); i++) Thread.Sleep(500);
        if (!Directory.Exists(letter + @":\")) return; // o runner não deixou criar o disco virtual: nada a testar aqui
        string root = letter + @":\";
        Assert.Equal("NTFS", new DriveInfo(root).DriveFormat);

        var photo = new byte[3 * 1024 * 1024 + 123];
        new Random(11).NextBytes(photo);
        photo[0] = 0xFF; photo[1] = 0xD8; photo[2] = 0xFF;
        var report = new byte[200_000];
        new Random(12).NextBytes(report);
        const string noteText = "anotação pequena, guardada dentro da MFT";
        Directory.CreateDirectory(Path.Combine(root, @"Fotos\Antigas"));
        Directory.CreateDirectory(Path.Combine(root, "Doc"));
        WriteDurable(Path.Combine(root, @"Fotos\Antigas\foto.jpg"), photo);
        WriteDurable(Path.Combine(root, @"Doc\relatorio.dat"), report);
        WriteDurable(Path.Combine(root, "nota.txt"), System.Text.Encoding.UTF8.GetBytes(noteText));
        WriteDurable(Path.Combine(root, "vivo.txt"), System.Text.Encoding.UTF8.GetBytes("este fica"));

        File.Delete(Path.Combine(root, @"Fotos\Antigas\foto.jpg"));
        Directory.Delete(Path.Combine(root, @"Fotos\Antigas"));
        File.Delete(Path.Combine(root, @"Doc\relatorio.dat"));
        File.Delete(Path.Combine(root, "nota.txt"));

        // Soltar o disco grava tudo o que o Windows ainda guarda na memória; só então o volume cru mostra o estado verdadeiro
        DiskPart($"select vdisk file=\"{_vhd}\"", "detach vdisk");
        _attached = false;
        DiskPart($"select vdisk file=\"{_vhd}\"", "attach vdisk", "select partition 1", $"assign letter={letter}");
        _attached = true;
        for (int i = 0; i < 60 && !Directory.Exists(root); i++) Thread.Sleep(500);
        Assert.True(Directory.Exists(root), "o disco virtual não voltou depois de religar. diskpart: " + made);

        using var raw = new RawVolume(letter);
        var scan = NtfsScanner.Scan(raw, null, CancellationToken.None);
        VolumeBitmap? bitmap;
        using (var handle = VolumeApi.OpenVolume(letter)) bitmap = VolumeApi.ReadBitmap(handle);
        Assert.NotNull(bitmap);

        DeletedFile Find(string name) => scan.Files.Single(f => f.Name == name);
        var foto = Find("foto.jpg");
        Assert.Equal(photo.Length, foto.Size);
        Assert.Equal(@"Fotos\Antigas", scan.Folders.PathOf(foto.Parent, foto.ParentSequence));
        Assert.Equal(RecoveryChance.Excellent, NtfsChance.Assess(foto, bitmap).Chance);
        Assert.Equal("Doc", scan.Folders.PathOf(Find("relatorio.dat").Parent, Find("relatorio.dat").ParentSequence));
        Assert.True(Find("nota.txt").Resident);
        Assert.DoesNotContain(scan.Files, f => f.Name == "vivo.txt");

        string outRoot = Path.Combine(_dir, "recuperados");
        var results = NtfsRecoveryJob.Run(raw, scan, [foto, Find("relatorio.dat"), Find("nota.txt")], outRoot, null, CancellationToken.None);
        Assert.All(results, r => Assert.True(r.Complete, r.Name + ": " + r.Note));
        var gotPhoto = File.ReadAllBytes(Path.Combine(outRoot, @"Fotos\Antigas\foto.jpg"));
        var gotReport = File.ReadAllBytes(Path.Combine(outRoot, @"Doc\relatorio.dat"));
        Assert.True(SHA256.HashData(photo).SequenceEqual(SHA256.HashData(gotPhoto)), "foto: " + Describe(photo, gotPhoto));
        var reportFile = Find("relatorio.dat");
        Assert.True(SHA256.HashData(report).SequenceEqual(SHA256.HashData(gotReport)),
            $"relatório: {Describe(report, gotReport)}; chance {NtfsChance.Assess(reportFile, bitmap).Chance}; trechos {string.Join(" ", reportFile.Extents.Select(e => $"{e.Vcn}:{e.Lcn}+{e.Clusters}"))}");
        Assert.Equal(noteText, File.ReadAllText(Path.Combine(outRoot, "nota.txt")));
        Assert.Equal("este fica", File.ReadAllText(Path.Combine(root, "vivo.txt"))); // a origem não foi tocada
    }
}
