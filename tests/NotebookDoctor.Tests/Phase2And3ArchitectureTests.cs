using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

/// <summary>
/// Fases 2, 3 e 4 (ARC-01, ARC-02, ARC-03, ARC-04, ARC-05, ARC-06):
/// invariantes estáticas (zero async void fora de event handlers, zero catch mudo em bibliotecas/serviços),
/// rotação e concorrência do ErrorJournal, isolamento e thread-safety de ProcessProtectionPolicy,
/// desacoplamento dos serviços puros do Windows sem WPF e IPC seguro de instância única.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Phase2And3ArchitectureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nd-arc-" + Guid.NewGuid().ToString("N")[..8]);

    public Phase2And3ArchitectureTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static IEnumerable<string> SourceFiles(string relativeSubPath) =>
        Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, relativeSubPath), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string StripStringsAndComments(string code)
    {
        // Remove raw strings """...""", verbatim strings @"...", regular strings "..." e comentários
        code = Regex.Replace(code, @"\$*""""""[\s\S]*?""""""", "\"\"");
        code = Regex.Replace(code, @"(?:@\$|\$@|@)""(?:""""|[^""])*""", "\"\"");
        code = Regex.Replace(code, @"\$?""(?:\\.|[^""\\\r\n])*""", "\"\"");
        code = Regex.Replace(code, @"//[^\r\n]*", "");
        code = Regex.Replace(code, @"/\*[\s\S]*?\*/", "");
        return code;
    }

    // ---------- ARC-02: Zero async void fora de event handlers ----------

    [Fact]
    public void Nenhum_metodo_async_void_fora_de_event_handlers_existe_no_codigo_fonte()
    {
        var sigRegex = new Regex(@"\basync\s+void\s+(\w+)\s*\(([^)]*)\)", RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var file in SourceFiles("src"))
        {
            string clean = StripStringsAndComments(File.ReadAllText(file));
            foreach (Match m in sigRegex.Matches(clean))
            {
                string method = m.Groups[1].Value;
                string parameters = m.Groups[2].Value;
                if (!parameters.Contains("EventArgs", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: async void {method}({parameters})");
            }
        }

        Assert.True(offenders.Count == 0, "Métodos async void fora de event handlers encontrados:\n" + string.Join("\n", offenders));
    }

    // ---------- ARC-03: Zero catch vazio em Core, Windows e Services + ErrorJournal ----------

    [Fact]
    public void Nenhum_catch_vazio_sem_filtro_existe_nas_bibliotecas_e_servicos()
    {
        var bareCatch = new Regex(@"\bcatch\s*\{", RegexOptions.Compiled);
        var offenders = new List<string>();

        foreach (var area in new[] { "src/NotebookDoctor.Core", "src/NotebookDoctor.Windows", "src/NotebookDoctor/Services" })
        {
            foreach (var file in SourceFiles(area))
            {
                string clean = StripStringsAndComments(File.ReadAllText(file));
                foreach (Match m in bareCatch.Matches(clean))
                    offenders.Add($"{Path.GetRelativePath(RepoPaths.Root, file)}: {m.Value}");
            }
        }

        Assert.True(offenders.Count == 0, "Blocos catch mudos sem tipo encontrados:\n" + string.Join("\n", offenders));
    }

    [WindowsFact]
    public void ErrorJournal_grava_contexto_e_rotaciona_quando_excede_512KB()
    {
        string logDir = Path.Combine(_dir, "logs");
        string logFile = Path.Combine(logDir, "erros.log");
        string prevFile = Path.Combine(logDir, "erros.anterior.log");

        ErrorJournal.Record(new InvalidOperationException("falha inicial"), "ModuloTeste", logDir);
        Assert.True(File.Exists(logFile));
        string initial = File.ReadAllText(logFile, Encoding.UTF8);
        Assert.Contains("[ModuloTeste]", initial);
        Assert.Contains("falha inicial", initial);

        // Preenche acima do limite de 512 KB para acionar a rotação automática
        File.WriteAllText(logFile, new string('X', 520 * 1024), Encoding.UTF8);
        ErrorJournal.Record(new IOException("apos rotacao"), "Rotacao", logDir);

        Assert.True(File.Exists(prevFile));
        Assert.True(new FileInfo(prevFile).Length >= 520 * 1024);
        string current = File.ReadAllText(logFile, Encoding.UTF8);
        Assert.Contains("[Rotacao]", current);
        Assert.Contains("apos rotacao", current);
        Assert.True(new FileInfo(logFile).Length < 4096);
    }

    [WindowsFact]
    public async Task ErrorJournal_suporta_escritas_concorrentes_sem_perder_entradas()
    {
        string logDir = Path.Combine(_dir, "concurrent-logs");
        const int writers = 24;

        await Task.WhenAll(Enumerable.Range(0, writers).Select(i =>
            Task.Run(() => ErrorJournal.Record(new InvalidOperationException($"erro-{i:D3}"), $"Worker-{i:D3}", logDir))));

        string all = await File.ReadAllTextAsync(Path.Combine(logDir, "erros.log"), Encoding.UTF8);
        for (int i = 0; i < writers; i++)
            Assert.Contains($"[Worker-{i:D3}]", all);
    }

    // ---------- ARC-05: ProcessProtectionPolicy isolada e ProcessTools thread-safe ----------

    [Fact]
    public void ProcessProtectionPolicy_isola_instancias_e_protege_processos_criticos()
    {
        var policyA = new ProcessProtectionPolicy(["renderizador.exe", "banco_local"]);
        var policyB = new ProcessProtectionPolicy();

        Assert.True(policyA.IsProtected("csrss"));
        Assert.True(policyA.IsProtected("lsass.exe"));
        Assert.True(policyA.IsProtected("NotebookDoctor"));
        Assert.True(policyA.IsProtected("renderizador"));
        Assert.True(policyA.IsProtected("banco_local.exe"));

        // Outra instância não é afetada pela lista extra de policyA
        Assert.False(policyB.IsProtected("renderizador"));
        Assert.True(policyB.IsProtected("svchost"));
    }

    [Fact]
    public async Task ProcessTools_SetUserProtected_e_IsProtected_sao_thread_safe()
    {
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() =>
            {
                for (int step = 0; step < 50; step++)
                {
                    ProcessTools.SetUserProtected([$"custom-{i}-{step}.exe", "app-fixo"]);
                    Assert.True(ProcessTools.IsProtected("explorer"));
                    _ = ProcessTools.IsProtected("app-fixo");
                }
            })));
        }
        finally
        {
            ProcessTools.SetUserProtected([]);
        }
        Assert.False(ProcessTools.IsProtected("app-fixo"));
    }

    // ---------- ARC-01: Serviços puros do sistema desacoplados em NotebookDoctor.Windows ----------

    [Fact]
    public void Servicos_puros_do_sistema_vivem_em_NotebookDoctor_Windows_sem_WPF()
    {
        var windowsAssembly = typeof(SafeDelete).Assembly;
        Assert.Equal("NotebookDoctor.Windows", windowsAssembly.GetName().Name);

        Type[] migratedServices =
        [
            typeof(Updater),
            typeof(ErrorJournal),
            typeof(SystemExe),
            typeof(SingleInstanceIpc),
            typeof(PowerPlans),
            typeof(ScheduledTasks),
            typeof(ServiceManager),
            typeof(SystemIntegrity),
            typeof(RestorePoints),
            typeof(EventLogs),
            typeof(BrowserExtensionScan),
            typeof(OneDriveScan),
            typeof(NetTest),
            typeof(FilePublisher),
            typeof(SettingsLockPolicy),
            typeof(StartMenu),
            typeof(StartupManager),
            typeof(UninstallRunner),
        ];

        Assert.All(migratedServices, t => Assert.Same(windowsAssembly, t.Assembly));

        // Confirma que NotebookDoctor.Windows não referencia PresentationFramework / WindowsBase (WPF)
        var referencedNames = windowsAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("PresentationFramework", referencedNames);
        Assert.DoesNotContain("PresentationCore", referencedNames);
        Assert.DoesNotContain("WindowsBase", referencedNames);
    }

    [WindowsFact]
    public void PowerPlans_ServiceManager_e_FilePublisher_funcionam_direto_do_assembly_Windows()
    {
        Assert.False(PowerPlans.SetActive("nao-e-um-guid; calc.exe"));
        Assert.False(PowerPlans.Delete("invalido"));
        var plans = PowerPlans.List();
        Assert.NotNull(plans);
        Assert.All(plans, p => Assert.True(Guid.TryParse(p.Guid, out _)));

        var services = ServiceManager.List();
        Assert.NotEmpty(services);
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceManager.SetStart("EventLog", 99));

        Assert.Equal(Loc.T("Arquivo não encontrado"), FilePublisher.Describe(Path.Combine(_dir, "inexistente.exe")));
        string script = Path.Combine(_dir, "script.cmd");
        File.WriteAllText(script, "@echo off");
        Assert.Equal(Loc.T("Atalho ou script (veja o local)"), FilePublisher.Describe(script));

        string signedCoreDll = typeof(object).Assembly.Location;
        Assert.StartsWith("Assinado:", FilePublisher.Describe(signedCoreDll));

        // Consultas somente-leitura adicionais desacopladas do WPF
        _ = SettingsLockPolicy.Read();
        Assert.NotNull(BrowserExtensionScan.List());
        Assert.NotNull(OneDriveScan.Folders().ToList());
    }

    // ---------- ARC-04: SingleInstanceIpc (Mutex + NamedPipe CurrentUserOnly) e modularização do App ----------

    [WindowsFact]
    public async Task SingleInstanceIpc_detecta_segunda_instancia_e_entrega_mensagem_pelo_pipe()
    {
        string suffix = "Test." + Guid.NewGuid().ToString("N")[..8];
        using var first = SingleInstanceIpc.Acquire(isDemo: false, suffix);
        using var second = SingleInstanceIpc.Acquire(isDemo: false, suffix);

        Assert.True(first.IsFirstInstance);
        Assert.False(second.IsFirstInstance);
        Assert.Equal("notebookdoctor:tab/programas", SingleInstanceIpc.BuildActivationMessage(["--tab=programas"], "notebookdoctor"));
        Assert.Equal("notebookdoctor:open", SingleInstanceIpc.BuildActivationMessage([], "notebookdoctor"));

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = SingleInstanceIpc.ListenAsync(
            first.PipeName,
            msg => { received.TrySetResult(msg); cts.Cancel(); },
            onError: null,
            cts.Token);

        // Aguarda brevemente o servidor abrir o pipe e envia uma mensagem maior que o limite para validar truncamento seguro
        await Task.Delay(100);
        string longPayload = "notebookdoctor:" + new string('a', 260);
        bool sent = await Task.Run(() => SingleInstanceIpc.TrySend(second.PipeName, longPayload, timeoutMs: 3000));
        Assert.True(sent);

        string delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SingleInstanceIpc.MaxMessageLength, delivered.Length);
        await serverTask;
    }

    [Fact]
    public void Governanca_de_build_DirectoryBuildProps_existe_com_TreatWarningsAsErrors()
    {
        string propsPath = Path.Combine(RepoPaths.Root, "Directory.Build.props");
        Assert.True(File.Exists(propsPath));
        string xml = File.ReadAllText(propsPath);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", xml);
        Assert.Contains("<AnalysisLevel>latest-recommended</AnalysisLevel>", xml);
        Assert.Contains("<Nullable>enable</Nullable>", xml);
    }
}

