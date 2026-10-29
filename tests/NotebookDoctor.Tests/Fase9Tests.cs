using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NotebookDoctor.Services;

namespace NotebookDoctor.Tests;

/// <summary>Fase 9: fundação (carregamento de DLLs, gravação atômica, listas grandes).</summary>
public class Fase9Tests
{
    // Leva 9.1, tarefa 05: o executável portátil roda de pastas onde qualquer um deixa arquivos (Downloads, pendrive)
    [Fact]
    public void Biblioteca_do_Windows_so_carrega_DLLs_da_System32()
    {
        var attr = typeof(SafeDelete).Assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(DllImportSearchPath.System32, attr!.Paths);
    }

    [Fact]
    public void App_so_carrega_DLLs_da_System32()
    {
        string info = File.ReadAllText(Path.Combine(RepoPaths.App, "AssemblyInfo.cs"));
        Assert.Contains("DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)", info);
    }

    [Fact]
    public void Nenhuma_DLL_e_carregada_pelo_nome_com_a_busca_padrao()
    {
        // NativeLibrary.TryLoad(x, out h) e Load(x) procuram primeiro na pasta do executável. Pelo nome, só com DllImportSearchPath;
        // sem ele, só com um caminho completo montado por Path.Combine no mesmo arquivo
        var call = new Regex(@"NativeLibrary\.(?:Try)?Load\(\s*([^,()]+?)\s*(?:,\s*out\b|\))");
        var hits = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoPaths.Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            string code = File.ReadAllText(f);
            foreach (Match m in call.Matches(code))
            {
                string arg = m.Groups[1].Value;
                bool fullPath = Regex.IsMatch(arg, @"^\w+$") && Regex.IsMatch(code, $@"\b{arg}\s*=\s*Path\.Combine\(");
                if (!fullPath) hits.Add($"{Path.GetFileName(f)}: {m.Value}");
            }
        }
        Assert.Empty(hits);
    }

    // Leva 9.2, tarefa 09
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Gravacao_atomica_troca_o_arquivo_inteiro_e_nao_deixa_temporario()
    {
        string dir = Directory.CreateTempSubdirectory("nd-atomic-").FullName;
        try
        {
            string f = Path.Combine(dir, "settings.json");
            File.WriteAllText(f, "{\"antigo\": true, \"texto\": \"bem mais comprido que o novo\"}");
            AtomicFile.WriteAllText(f, "{\"novo\":1,\"nome\":\"Configurações\"}");
            Assert.Equal("{\"novo\":1,\"nome\":\"Configurações\"}", File.ReadAllText(f));
            Assert.Equal(new[] { f }, Directory.GetFiles(dir));
            // Sem BOM, como o File.WriteAllText que substitui
            Assert.Equal((byte)'{', File.ReadAllBytes(f)[0]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Linhas_gravadas_atomicamente_terminam_cada_uma_com_quebra()
    {
        string dir = Directory.CreateTempSubdirectory("nd-atomic-").FullName;
        try
        {
            string f = Path.Combine(dir, "alertas.jsonl");
            AtomicFile.WriteAllLines(f, ["{\"a\":1}", "{\"b\":2}"]);
            Assert.Equal("{\"a\":1}\n{\"b\":2}\n", File.ReadAllText(f));
            // E o AppendAllText de um alerta novo continua numa linha própria
            File.AppendAllText(f, "{\"c\":3}\n");
            Assert.Equal(3, File.ReadAllLines(f).Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Gravacao_que_falha_deixa_o_arquivo_antigo_intacto()
    {
        string dir = Directory.CreateTempSubdirectory("nd-atomic-").FullName;
        try
        {
            string f = Path.Combine(dir, "acoes.json");
            File.WriteAllText(f, "[1,2,3]");
            // Uma pasta com o nome do temporário faz a gravação falhar antes da troca
            Directory.CreateDirectory(f + ".tmp");
            var ex = Record.Exception(() => AtomicFile.WriteAllText(f, "[]"));
            Assert.True(ex is IOException or UnauthorizedAccessException, ex?.GetType().Name ?? "sem exceção");
            Assert.Equal("[1,2,3]", File.ReadAllText(f));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Copia_de_arquivo_ilegivel_fica_ao_lado_e_sem_arquivo_nao_ha_copia()
    {
        string dir = Directory.CreateTempSubdirectory("nd-atomic-").FullName;
        try
        {
            string f = Path.Combine(dir, "settings.json");
            Assert.Null(AtomicFile.KeepCopy(f, "ilegivel"));
            File.WriteAllText(f, "{quebrado");
            string? copy = AtomicFile.KeepCopy(f, "ilegivel");
            Assert.Equal(f + ".ilegivel", copy);
            Assert.Equal("{quebrado", File.ReadAllText(copy!));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Configuracoes_logs_e_soneca_gravam_de_forma_atomica()
    {
        foreach (var rel in new[] { "src/NotebookDoctor/Services/Settings.cs", "src/NotebookDoctor/Services/ActionLog.cs", "src/NotebookDoctor/Services/AlertLog.cs", "src/NotebookDoctor.Windows/SnoozeStore.cs" })
        {
            // O arquivo de dados (não a sonda de escrita da pasta) só é gravado pelo AtomicFile
            string code = File.ReadAllText(Path.Combine(RepoPaths.Root, rel));
            Assert.DoesNotMatch(@"(?<!Atomic)File\.WriteAll(Text|Lines)\(\s*(AppPaths\.Settings|FilePath|File)\b", code);
            Assert.Contains("AtomicFile.WriteAll", code);
        }
    }

    // Leva 9.2, tarefa 10: a grade só tem altura finita (e só virtualiza) porque o FitScroll fixa a altura da grade da janela
    [Fact]
    public void Tabelas_virtualizam_e_reaproveitam_linhas()
    {
        string controls = File.ReadAllText(Path.Combine(RepoPaths.App, "Themes", "Controls.xaml"));
        int style = controls.IndexOf("<Style TargetType=\"DataGrid\">", StringComparison.Ordinal);
        Assert.True(style >= 0);
        string body = controls[style..controls.IndexOf("</Style>", style, StringComparison.Ordinal)];
        Assert.Contains("\"VirtualizingPanel.VirtualizationMode\" Value=\"Recycling\"", body);
        Assert.Contains("\"EnableRowVirtualization\" Value=\"True\"", body);
        // Nenhuma tela desliga a virtualização por conta própria
        foreach (var xaml in Directory.EnumerateFiles(RepoPaths.App, "*.xaml"))
            Assert.DoesNotContain("IsVirtualizing=\"False\"", File.ReadAllText(xaml));
    }

    // Leva 9.3, tarefa 11
    [Theory]
    [InlineData(4, 17, 0, "SSD", "NVMe", "Healthy")]
    [InlineData(3, 11, 1, "HDD", "SATA", "Warning")]
    [InlineData(0, 7, 2, "Unspecified", "USB", "Unhealthy")]
    [InlineData(5, 99, 5, "SCM", "Unknown", "Unknown")]
    public void Codigos_do_disco_viram_os_nomes_do_PowerShell(long media, long bus, long health, string m, string b, string h)
    {
        Assert.Equal(m, StorageNames.Media(media));
        Assert.Equal(b, StorageNames.Bus(bus));
        Assert.Equal(h, StorageNames.Health(health));
    }

    [Theory]
    [InlineData("SystemInfoScript.cs")]
    [InlineData("OemProbe.cs")]
    public void Leitura_direta_escreve_cada_campo_que_o_script_escrevia(string script)
    {
        string ps = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "NotebookDoctor.Windows", script));
        string native = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "NotebookDoctor.Windows", "WmiReader.cs"));
        // Campos do JSON do PowerShell: "$o.os=", "[ordered]@{ caption=...; version=... }"
        var keys = Regex.Matches(ps, @"\$o\.(\w+)\s*=").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(ps, @"[{;]\s*(\w+)=").Select(m => m.Groups[1].Value))
            .Where(k => k is not ("ErrorActionPreference" or "OutputEncoding"))
            .Distinct().ToList();
        Assert.NotEmpty(keys);
        var missing = keys.Where(k => !native.Contains($"[\"{k}\"]", StringComparison.Ordinal)).ToList();
        Assert.Empty(missing);
    }

    // Leva 9.3, tarefa 12
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Changed = new(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Pasta_que_nao_mudou_usa_o_tamanho_guardado()
    {
        var cache = new FolderSizeCache();
        cache.Set(@"C:\Program Files\App\", Changed, 1_234_567, Now.AddDays(-2));
        Assert.True(cache.TryGet(@"c:\program files\app", Changed, Now, out long bytes));
        Assert.Equal(1_234_567, bytes);
    }

    [Fact]
    public void Pasta_que_mudou_ou_medicao_vencida_mede_de_novo()
    {
        var cache = new FolderSizeCache();
        cache.Set(@"C:\App", Changed, 100, Now.AddDays(-2));
        Assert.False(cache.TryGet(@"C:\App", Changed.AddSeconds(1), Now, out _));
        Assert.False(cache.TryGet(@"C:\App", Changed, Now.AddDays(29), out _));   // 31 dias depois da medição
        Assert.False(cache.TryGet(@"C:\App", Changed, Now.AddDays(-3), out _));  // relógio voltou para antes da medição
        Assert.False(cache.TryGet(@"C:\Outra", Changed, Now, out _));
    }

    [Fact]
    public void Pasta_vazia_nao_fica_guardada()
    {
        var cache = new FolderSizeCache();
        cache.Set(@"C:\App", Changed, 100, Now);
        cache.Set(@"C:\App", Changed, 0, Now);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Cache_gravado_e_lido_de_volta_sem_as_medicoes_vencidas()
    {
        var cache = new FolderSizeCache();
        cache.Set(@"C:\Nova", Changed, 500, Now.AddDays(-1));
        cache.Set(@"C:\Velha", Changed, 700, Now.AddDays(-40));
        var back = FolderSizeCache.Parse(cache.Serialize(Now));
        Assert.Equal(1, back.Count);
        Assert.True(back.TryGet(@"C:\Nova", Changed, Now, out long bytes));
        Assert.Equal(500, bytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{quebrado")]
    [InlineData("[{\"Path\":\"\",\"Bytes\":10}, {\"Path\":\"C:\\\\X\",\"Bytes\":-1}]")]
    public void Cache_ilegivel_ou_invalido_vira_cache_vazio(string? json) => Assert.Equal(0, FolderSizeCache.Parse(json).Count);
}

/// <summary>Leva 9.3 na CI do Windows: a leitura direta do WMI e o PowerShell precisam dar o mesmo retrato.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class Fase9IntegrationTests
{
    [WindowsFact]
    public void Aba_Sistema_pelo_WMI_direto_bate_com_o_PowerShell()
    {
        var native = SystemInfoScript.ReadNative();
        var ps = SystemInfoScript.Run();
        Assert.NotEqual("", native.OsName);
        Assert.Equal(ps.OsName, native.OsName);
        Assert.Equal(ps.OsVersion, native.OsVersion);
        Assert.Equal(ps.OsBuild, native.OsBuild);
        Assert.Equal(ps.OsArch, native.OsArch);
        Assert.Equal(ps.Installed, native.Installed);
        Assert.Equal(ps.Activation, native.Activation);
        Assert.Equal(ps.SecureBoot, native.SecureBoot);
        Assert.Equal(ps.Tpm, native.Tpm);
        Assert.Equal(ps.Cpus.Select(c => (c.Name, c.Cores, c.Threads, c.MaxMHz, c.L2Kb, c.L3Kb, c.Virtualization)),
                     native.Cpus.Select(c => (c.Name, c.Cores, c.Threads, c.MaxMHz, c.L2Kb, c.L3Kb, c.Virtualization)));
        Assert.Equal(ps.MemSlots, native.MemSlots);
        Assert.Equal(ps.MemMaxBytes, native.MemMaxBytes);
        Assert.Equal(ps.Modules, native.Modules);
        Assert.Equal((ps.BoardMaker, ps.BoardProduct, ps.BoardVersion, ps.BiosVendor, ps.BiosVersion, ps.BiosDate),
                     (native.BoardMaker, native.BoardProduct, native.BoardVersion, native.BiosVendor, native.BiosVersion, native.BiosDate));
        Assert.Equal(ps.Gpus, native.Gpus);
        Assert.Equal(ps.Disks.Select(d => (d.Name, d.Media, d.Bus, d.SizeBytes, d.Health)), native.Disks.Select(d => (d.Name, d.Media, d.Bus, d.SizeBytes, d.Health)));
        Assert.Equal(ps.PredictFailure, native.PredictFailure);
        Assert.Equal(ps.Audio, native.Audio);
        Assert.Equal(ps.Optical, native.Optical);
        Assert.Equal(ps.Usb, native.Usb);
        Assert.Equal(ps.Bluetooth, native.Bluetooth);
        Assert.Equal(ps.Temps.Select(t => (t.Source, t.Name)), native.Temps.Select(t => (t.Source, t.Name)));
    }

    [WindowsFact]
    public async Task Inventario_dos_drivers_pelo_WMI_direto_bate_com_o_PowerShell()
    {
        var native = await OemProbe.NativeInventoryAsync();
        var ps = await OemProbe.ScriptInventoryAsync();
        Assert.NotNull(native);
        Assert.NotNull(ps);
        Assert.NotEqual("", native!.Chassis.Manufacturer);
        Assert.Equal(ps!.Chassis, native.Chassis);
        Assert.Equal(ps.Bios, native.Bios);
        Assert.Equal(ps.Gpus.Select(g => (g.Name, g.Vendor, g.DriverVersion, g.IsDedicated)), native.Gpus.Select(g => (g.Name, g.Vendor, g.DriverVersion, g.IsDedicated)));
        Assert.Equal(ps.NetworkAdapters.Select(n => (n.Name, n.Vendor, n.DriverVersion)), native.NetworkAdapters.Select(n => (n.Name, n.Vendor, n.DriverVersion)));
        Assert.Equal(ps.Audio, native.Audio);
    }
}
