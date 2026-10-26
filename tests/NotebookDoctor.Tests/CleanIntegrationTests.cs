using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

[SupportedOSPlatform("windows")]
public class CleanIntegrationTests
{
    private static void Junction(string link, string target)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    /// <summary>
    /// Pastas e chaves de mentira de verdade: a regra limpa só o que declarou. Uma pasta vizinha de nome parecido, uma
    /// junção que aponta para fora, um valor poupado pela exceção e a chave de outro programa têm de sobreviver, e o
    /// backup do registro tem de devolver o que saiu.
    /// </summary>
    [WindowsFact]
    public void Limpa_so_o_declarado_e_o_backup_do_registro_restaura()
    {
        string tag = "NDLimpa" + Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(Path.GetTempPath(), tag);
        string cache = Path.Combine(root, "Cache"), decoy = Path.Combine(root, "Cache2"), outside = Path.Combine(root, "Fora");
        string key = $@"HKEY_CURRENT_USER\Software\{tag}";
        var places = WindowsPlaces.Current();
        var store = new BackupStore(Path.Combine(Path.GetTempPath(), tag + "-backups"));
        try
        {
            Directory.CreateDirectory(Path.Combine(cache, "sub"));
            Directory.CreateDirectory(decoy);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(cache, "a.bin"), "aaaa");
            File.WriteAllText(Path.Combine(cache, "sub", "b.bin"), "bb");
            File.WriteAllText(Path.Combine(cache, "keep.dat"), "k");
            File.WriteAllText(Path.Combine(decoy, "c.bin"), "c");
            File.WriteAllText(Path.Combine(outside, "d.bin"), "d");
            Junction(Path.Combine(cache, "atalho"), outside);

            using (var k = RegistryPaths.Create($@"{key}\Recent")) { k.SetValue("a", "1"); k.SetValue("b", 2, RegistryValueKind.DWord); }
            using (var k = RegistryPaths.Create($@"{key}\Opts")) { k.SetValue("LastFile1", "x"); k.SetValue("Pinned", "p"); k.SetValue("Theme", "dark"); }

            var rule = Winapp2Parser.Parse($"""
                [{tag} *]
                Detect=HKCU\Software\{tag}
                FileKey1=%TestRoot%\Cache|*.*|RECURSE
                RegKey1=HKCU\Software\{tag}\Recent
                RegKey2=HKCU\Software\{tag}\Opts|*
                ExcludeKey1=FILE|%TestRoot%\Cache|keep.dat
                ExcludeKey2=REG|HKCU\Software\{tag}\Opts|Pinned;Theme
                """).Single();
            var sys = new WindowsCleanSystem();
            var engine = new CleanEngine(sys, new CleanContext(places, EnvExpander.FromPlaces(places).With("TestRoot", root), Environment.OSVersion.Version));
            var entry = engine.Plan(rule)!;
            Assert.Equal(2, entry.FileCount); // a.bin e sub\b.bin; keep.dat é poupado e a junção não é seguida
            Assert.Equal(2, entry.RegCount);  // a chave Recent inteira e só o valor LastFile1
            entry.Selected = true;

            RuleCleanResult result;
            string session;
            using (var backup = store.Begin("teste de limpeza"))
            {
                session = backup.Id;
                result = CleanExecutor.Run([entry], places, backup, isAdmin: false);
            }

            Assert.False(File.Exists(Path.Combine(cache, "a.bin")));
            Assert.False(File.Exists(Path.Combine(cache, "sub", "b.bin")));
            Assert.True(File.Exists(Path.Combine(cache, "keep.dat")));
            Assert.True(File.Exists(Path.Combine(decoy, "c.bin")));
            Assert.True(File.Exists(Path.Combine(outside, "d.bin"))); // o destino da junção continua inteiro
            Assert.Equal(2, result.Files);
            Assert.Equal(6, result.Bytes);
            Assert.Equal(2, result.Keys);
            using (var k = RegistryPaths.Open($@"{key}\Opts"))
            {
                Assert.Null(k!.GetValue("LastFile1"));
                Assert.Equal("p", k.GetValue("Pinned"));
                Assert.Equal("dark", k.GetValue("Theme"));
            }
            Assert.Null(RegistryPaths.Open($@"{key}\Recent"));

            store.Restore(session);
            using (var k = RegistryPaths.Open($@"{key}\Recent"))
            {
                Assert.Equal("1", k!.GetValue("a"));
                Assert.Equal(2, k.GetValue("b"));
            }
            using (var k = RegistryPaths.Open($@"{key}\Opts")) Assert.Equal("x", k!.GetValue("LastFile1"));
        }
        finally
        {
            try { RegistryPaths.DeleteTree(key); } catch (Exception ex) when (ex is IOException or ArgumentException) { }
            if (Directory.Exists(Path.Combine(cache, "atalho"))) Directory.Delete(Path.Combine(cache, "atalho")); // só o link
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(store.Root)) Directory.Delete(store.Root, recursive: true);
        }
    }

    [WindowsFact]
    public void O_executor_recusa_pasta_protegida_mesmo_com_o_plano_adulterado()
    {
        string tag = "NDProt" + Guid.NewGuid().ToString("N")[..8];
        var places = WindowsPlaces.Current();
        string docs = Path.Combine(places.UserProfile, "Documents");
        var store = new BackupStore(Path.Combine(Path.GetTempPath(), tag + "-backups"));
        try
        {
            // Um plano montado à mão, como se alguém tivesse editado o arquivo de regras depois da análise
            var forged = new CleanPlanEntry
            {
                Rule = new CleanRule { Name = "Forjada" },
                Files = [new FileTarget(docs, ["*.*"], true, false, 0, [], []), new FileTarget(places.Windows, ["*.*"], true, false, 0, [], [])],
                Registry = [new RegTarget(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services", null), new RegTarget(@"HKEY_CURRENT_USER\Software", null)],
                Selected = true,
            };
            using var backup = store.Begin("forjado");
            var result = CleanExecutor.Run([forged], places, backup, isAdmin: true);
            Assert.Equal(0, result.Files);
            Assert.Equal(0, result.Keys);
            Assert.Empty(backup.Manifest.Items);
        }
        finally
        {
            if (Directory.Exists(store.Root)) Directory.Delete(store.Root, recursive: true);
        }
    }

    /// <summary>C6: cancelar não lança exceção; o executor para e devolve o que já fez, para a sessão entrar em Ações.</summary>
    [WindowsFact]
    public void Cancelar_devolve_o_resultado_parcial_sem_excecao()
    {
        string tag = "NDCancel" + Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(Path.GetTempPath(), tag);
        var places = WindowsPlaces.Current();
        var store = new BackupStore(Path.Combine(Path.GetTempPath(), tag + "-backups"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "a.bin"), "a");
            var entry = new CleanPlanEntry
            {
                Rule = new CleanRule { Name = "Cancelada" },
                Files = [new FileTarget(root, ["*.bin"], false, false, 0, [], [])],
                Selected = true,
            };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            using var backup = store.Begin("cancelada");
            var result = CleanExecutor.Run([entry], places, backup, isAdmin: false, ct: cts.Token);
            Assert.True(result.Cancelled);
            Assert.Equal(0, result.Files);
            Assert.True(File.Exists(Path.Combine(root, "a.bin")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(store.Root)) Directory.Delete(store.Root, recursive: true);
        }
    }

    [WindowsFact]
    public void Arquivos_inuteis_saem_e_o_resto_fica()
    {
        string tag = "NDJunk" + Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(Path.GetTempPath(), tag);
        var places = WindowsPlaces.Current();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "a.tmp"), "xx");
            File.WriteAllText(Path.Combine(root, "sub", "b.chk"), "y");
            File.WriteAllText(Path.Combine(root, "nota.txt"), "importante");
            File.WriteAllText(Path.Combine(root, "desktop.ini"), "[.ShellClassInfo]");
            File.WriteAllText(Path.Combine(root, "velho.bak"), "z");

            var items = JunkRules.Scan(new WindowsCleanSystem(), places, [root], [], DateTime.Now);
            Assert.Equal(3, items.Count);
            Assert.False(items.Single(i => i.Path.EndsWith("velho.bak")).Selected);

            var result = CleanExecutor.DeleteJunk(items, places);
            Assert.Equal(2, result.Files);
            Assert.False(File.Exists(Path.Combine(root, "a.tmp")));
            Assert.False(File.Exists(Path.Combine(root, "sub", "b.chk")));
            Assert.True(File.Exists(Path.Combine(root, "nota.txt")));
            Assert.True(File.Exists(Path.Combine(root, "desktop.ini")));
            Assert.True(File.Exists(Path.Combine(root, "velho.bak"))); // desmarcado
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public void Modulos_da_Limpeza_de_Disco_existem_e_sem_Downloads()
    {
        var modules = DiskCleanupRunner.Available();
        Assert.DoesNotContain(modules, m => m.Key is "DownloadsFolder" or "Recycle Bin");
    }

    [WindowsFact]
    public void Regras_proprias_rodam_no_Windows_de_verdade_sem_erro()
    {
        var places = WindowsPlaces.Current();
        var engine = new CleanEngine(new WindowsCleanSystem(), new CleanContext(places, EnvExpander.FromPlaces(places), Environment.OSVersion.Version));
        var plan = engine.PlanAll(BuiltInRules.All());
        Assert.All(plan, e => Assert.True(e.HasWork));
        Assert.All(plan.SelectMany(e => e.Files), t => Assert.True(CleanSafety.DirAllowed(t.Dir, t.Recurse, t.RemoveSelf, t.Patterns, places)));
    }
}
