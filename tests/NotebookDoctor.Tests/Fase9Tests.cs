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
}
