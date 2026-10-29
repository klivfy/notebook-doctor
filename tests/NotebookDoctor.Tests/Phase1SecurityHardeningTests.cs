using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.RegularExpressions;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

/// <summary>
/// Fase 1 (SEC-01, SEC-02, SEC-03, SEC-04): unificação de SafeDelete contra TOCTOU/junções/hardlinks,
/// endurecimento da verificação de assinatura de código (EKU + revogação), isolamento/bloqueio de handle
/// do instalador de atualização e qualificação absoluta de executáveis do sistema.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Phase1SecurityHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-sec1-" + Guid.NewGuid().ToString("N")[..8]);

    public Phase1SecurityHardeningTests()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(_dir)) return;
        foreach (var link in Directory.EnumerateDirectories(_dir, "*", SearchOption.AllDirectories)
                     .Where(d => new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList())
            Directory.Delete(link);
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private static void Junction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo(SystemExe.System32("cmd.exe"), $"/c mklink /J \"{link}\" \"{target}\"")
        { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    // ---------- SEC-01: SafeDelete, CleanExecutor.DeleteJunk e DuplicateRemover ----------

    [WindowsFact]
    public void FileAtExactPath_recusa_arquivo_alcancado_por_juncao()
    {
        string outside = Path.Combine(_dir, "protegida");
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "dados.tmp");
        File.WriteAllText(victim, "intacto");

        string junction = Path.Combine(_dir, "atalho");
        Junction(junction, outside);

        Assert.False(SafeDelete.FileAtExactPath(Path.Combine(junction, "dados.tmp"), ignoreReadOnly: true));
        Assert.True(File.Exists(victim));
    }

    [WindowsFact]
    public void ReparsePoint_remove_apenas_o_atalho_e_preserva_o_destino()
    {
        string root = Path.Combine(_dir, "raiz");
        string outside = Path.Combine(_dir, "destino");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "importante.txt");
        File.WriteAllText(victim, "preservado");

        string link = Path.Combine(root, "juncao");
        Junction(link, outside);

        string safeRoot = SafeDelete.Root(root)!;
        Assert.True(SafeDelete.ReparsePoint(link, safeRoot));
        Assert.False(Directory.Exists(link));
        Assert.True(Directory.Exists(outside));
        Assert.True(File.Exists(victim));
    }

    [WindowsFact]
    public void VerifyExclusiveFileHandle_recusa_juncao_e_hardlink()
    {
        string realDir = Path.Combine(_dir, "real");
        Directory.CreateDirectory(realDir);
        string realFile = Path.Combine(realDir, "unico.bin");
        File.WriteAllBytes(realFile, [1, 2, 3, 4]);

        using (var fs = new FileStream(realFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(SafeDelete.VerifyExclusiveFileHandle(fs.SafeFileHandle, realFile));

        // Via junção: o caminho final diverge do caminho esperado
        string junction = Path.Combine(_dir, "via-juncao");
        Junction(junction, realDir);
        string viaLink = Path.Combine(junction, "unico.bin");
        using (var fs = new FileStream(viaLink, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(SafeDelete.VerifyExclusiveFileHandle(fs.SafeFileHandle, viaLink));

        // Com hardlink: NumberOfLinks > 1 permite modificar o mesmo conteúdo por outro caminho
        string hardlink = Path.Combine(realDir, "espelho.bin");
        Assert.True(CreateHardLinkW(hardlink, realFile, IntPtr.Zero));
        using (var fs = new FileStream(realFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(SafeDelete.VerifyExclusiveFileHandle(fs.SafeFileHandle, realFile));
    }

    [WindowsFact]
    public void DeleteJunk_recusa_item_quando_pasta_vira_juncao_entre_busca_e_exclusao()
    {
        string scanRoot = Path.Combine(_dir, "junk");
        string sub = Path.Combine(scanRoot, "sub");
        string outside = Path.Combine(_dir, "vitima");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(outside);

        File.WriteAllText(Path.Combine(sub, "alvo.tmp"), "lixo");
        string victim = Path.Combine(outside, "alvo.tmp");
        File.WriteAllText(victim, "nao-apagar");

        var places = WindowsPlaces.Current();
        var items = JunkRules.Scan(new WindowsCleanSystem(), places, [scanRoot], [], DateTime.Now);
        Assert.Single(items);

        // Simula ataque TOCTOU: troca "sub" por uma junção apontando para "vitima" antes de apagar
        Directory.Delete(sub, recursive: true);
        Junction(sub, outside);

        var res = CleanExecutor.DeleteJunk(items, places);
        Assert.Equal(0, res.Files);
        Assert.Equal(1, res.Skipped);
        Assert.True(File.Exists(victim));
    }

    [WindowsFact]
    public void DuplicateRemover_recusa_copia_quando_pasta_vira_juncao_entre_busca_e_exclusao()
    {
        string keepDir = Path.Combine(_dir, "fica");
        string delDir = Path.Combine(_dir, "sai");
        string outside = Path.Combine(_dir, "protegido");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(delDir);
        Directory.CreateDirectory(outside);

        byte[] content = "conteudo-identico-para-teste"u8.ToArray();
        string keeper = Path.Combine(keepDir, "doc.dat");
        string dup = Path.Combine(delDir, "doc.dat");
        string victim = Path.Combine(outside, "doc.dat");
        File.WriteAllBytes(keeper, content);
        File.WriteAllBytes(dup, content);
        File.WriteAllBytes(victim, content);

        var now = DateTime.UtcNow;
        var group = new DuplicateGroup(content.Length,
        [
            new DuplicateFile(keeper, now.AddMinutes(-10), now.AddMinutes(-10)),
            new DuplicateFile(dup, now, now),
        ]);

        // Troca "sai" por junção para "protegido" antes da exclusão
        Directory.Delete(delDir, recursive: true);
        Junction(delDir, outside);

        var res = DuplicateRemover.Remove([(group, [dup])], WindowsPlaces.Current(), KeepRule.Oldest, permanent: true);
        Assert.Equal(0, res.Files);
        Assert.Single(res.Skipped);
        Assert.True(File.Exists(victim));
        Assert.True(File.Exists(keeper));
    }

    // ---------- SEC-02: PublisherSignature (EKU + pin obrigatório) & Authenticode ----------

    [Fact]
    public void PublisherSignature_exige_EKU_de_assinatura_de_codigo_quando_informado()
    {
        var klivfy = new[] { ("CN", "Klivfy"), ("O", "Klivfy") };
        Assert.True(PublisherSignature.HasCodeSigningEku(["1.3.6.1.5.5.7.3.3"]));
        Assert.False(PublisherSignature.HasCodeSigningEku([]));
        Assert.False(PublisherSignature.HasCodeSigningEku(["1.3.6.1.5.5.7.3.1"])); // TLS Server Auth

        Assert.True(PublisherSignature.Trusted(klivfy, "AABB", ekuOids: ["1.3.6.1.5.5.7.3.3"]));
        Assert.False(PublisherSignature.Trusted(klivfy, "AABB", ekuOids: ["1.3.6.1.5.5.7.3.1"]));
        Assert.False(PublisherSignature.Trusted(klivfy, "AABB", ekuOids: []));
    }

    [Fact]
    public void PublisherSignature_com_requirePin_recusa_lista_vazia()
    {
        var klivfy = new[] { ("CN", "Klivfy"), ("O", "Klivfy") };
        Assert.False(PublisherSignature.Trusted(klivfy, "AABB", pinned: [], requirePin: true));
        Assert.True(PublisherSignature.Trusted(klivfy, "AABB", pinned: ["AABB"], requirePin: true));
    }

    [WindowsFact]
    public void WinVerifyTrust_valida_binario_oficial_do_Windows_mas_recusa_assinante_diferente_da_Klivfy()
    {
        string signedBinary = typeof(object).Assembly.Location;
        Assert.True(File.Exists(signedBinary));
        Assert.True(Updater.HasValidAuthenticode(signedBinary));
        Assert.False(Updater.IsSignedByPublisher(signedBinary)); // assinado pela Microsoft, não pela Klivfy

        string unsignedExe = Path.Combine(_dir, "sem-assinatura.exe");
        File.WriteAllBytes(unsignedExe, [0x4D, 0x5A, 0x90, 0x00]);
        Assert.False(Updater.HasValidAuthenticode(unsignedExe));
        Assert.False(Updater.IsSignedByPublisher(unsignedExe));
    }

    // ---------- SEC-03: pasta isolada com ACL restrita e handle bloqueado contra TOCTOU ----------

    [WindowsFact]
    public void CreateSecureUpdateDirectory_protege_ACL_e_recusa_juncao()
    {
        string baseDir = Path.Combine(_dir, "appdata");
        string runDir = Updater.CreateSecureUpdateDirectory(baseDir);
        Assert.True(Directory.Exists(runDir));

        var acl = new DirectoryInfo(runDir).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected); // herança desativada

        var rules = acl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToList();
        using var identity = WindowsIdentity.GetCurrent();
        var allowedSids = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
            new(WellKnownSidType.LocalSystemSid, null),
        };
        if (identity.User is { } userSid) allowedSids.Add(userSid);
        Assert.All(rules, r => Assert.Contains((SecurityIdentifier)r.IdentityReference, allowedSids));

        // Se a pasta "updates" for trocada por uma junção, recusa imediatamente
        string poisonedBase = Path.Combine(_dir, "poisoned");
        string outside = Path.Combine(_dir, "alvo-externo");
        Directory.CreateDirectory(poisonedBase);
        Directory.CreateDirectory(outside);
        Junction(Path.Combine(poisonedBase, "updates"), outside);
        Assert.Throws<InvalidOperationException>(() => Updater.CreateSecureUpdateDirectory(poisonedBase));
    }

    [WindowsFact]
    public async Task VerifyAndLockAsync_impede_escrita_e_exclusao_ate_o_Dispose()
    {
        string baseDir = Path.Combine(_dir, "upd");
        string runDir = Updater.CreateSecureUpdateDirectory(baseDir);
        string installerPath = Path.Combine(runDir, "NotebookDoctor-1.2.0-setup.exe");
        byte[] payload = "instalador-simulado-integro"u8.ToArray();
        await File.WriteAllBytesAsync(installerPath, payload);
        string sha256 = Convert.ToHexString(SHA256.HashData(payload));

        using (var verified = await Updater.VerifyAndLockAsync(installerPath, sha256, (_, _) => true))
        {
            // Enquanto o instalador verificado está aberto, ninguém consegue sobrescrever nem apagar (anti-TOCTOU)
            Assert.Throws<IOException>(() => File.OpenWrite(verified.FilePath));
            Assert.Throws<IOException>(() => File.Delete(verified.FilePath));

            // Mas o Windows Loader / WinVerifyTrust consegue abrir para leitura compartilhada
            await using var reader = new FileStream(verified.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Assert.Equal(payload.Length, reader.Length);
        }

        // Após o Dispose, o bloqueio é liberado
        Assert.True(SafeDelete.FileAtExactPath(installerPath));
    }

    [WindowsFact]
    public async Task VerifyAndLockAsync_apaga_arquivo_quando_hash_ou_assinatura_falha()
    {
        string runDir = Updater.CreateSecureUpdateDirectory(Path.Combine(_dir, "upd-fail"));
        string badHashFile = Path.Combine(runDir, "bad-hash.exe");
        await File.WriteAllBytesAsync(badHashFile, [1, 2, 3]);
        string wrongSha = new('A', 64);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Updater.VerifyAndLockAsync(badHashFile, wrongSha, (_, _) => true));
        Assert.False(File.Exists(badHashFile));

        string badSigFile = Path.Combine(runDir, "bad-sig.exe");
        byte[] bytes = [10, 20, 30];
        await File.WriteAllBytesAsync(badSigFile, bytes);
        string rightSha = Convert.ToHexString(SHA256.HashData(bytes));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Updater.VerifyAndLockAsync(badSigFile, rightSha, (_, _) => false));
        Assert.False(File.Exists(badSigFile));
    }

    // ---------- SEC-04: SystemExe e ausência de binários do sistema sem caminho absoluto ----------

    [WindowsFact]
    public void SystemExe_resolve_caminhos_absolutos_e_recusa_traversal()
    {
        Assert.True(Path.IsPathRooted(SystemExe.System32("cmd.exe")));
        Assert.True(File.Exists(SystemExe.System32("cmd.exe")));
        Assert.True(File.Exists(SystemExe.Explorer));
        Assert.True(File.Exists(SystemExe.RegEdit));
        Assert.True(File.Exists(SystemExe.PowerShell));
        Assert.Equal(SystemExe.System32("cmd.exe"), SystemExe.ResolveAppAliasOrSystem("cmd.exe"), ignoreCase: true);

        Assert.Throws<ArgumentException>(() => SystemExe.System32(@"..\cmd.exe"));
        Assert.Throws<ArgumentException>(() => SystemExe.System32(@"C:\Windows\System32\cmd.exe"));
        Assert.Throws<ArgumentException>(() => SystemExe.WindowsRoot(@"sub\explorer.exe"));
        Assert.Throws<ArgumentException>(() => SystemExe.ResolveAppAliasOrSystem(@"..\winget.exe"));
    }

    [Fact]
    public void Nenhum_binario_do_Windows_e_iniciado_por_nome_solto_no_codigo_fonte()
    {
        var bareBinaries = new Regex(
            @"(?:Process\.Start|new\s+(?:System\.Diagnostics\.)?ProcessStartInfo)\s*\(\s*""(?:powershell|pwsh|cmd|explorer|regedit|reg|schtasks|sc|powercfg|cleanmgr|defrag|pnputil|sfc|dism|wevtutil|ipconfig|control|msiexec|SystemPropertiesProtection)(?:\.exe)?""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            string code = File.ReadAllText(file);
            foreach (Match m in bareBinaries.Matches(code))
                offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: {m.Value}");
        }

        Assert.True(offenders.Count == 0, "Binários do sistema chamados sem caminho absoluto:\n" + string.Join("\n", offenders));
    }
}
