using System.Text.RegularExpressions;
using NotebookDoctor.Services;
using Xunit;

namespace NotebookDoctor.Tests;

/// <summary>Prioridade 8: o script do instalador (installer/NotebookDoctor.iss) precisa continuar combinando com o que o app assume.</summary>
public class InstaladorTests
{
    private static string Script() => File.ReadAllText(Path.Combine(RepoPaths.Root, "installer", "NotebookDoctor.iss"));

    [Fact]
    public void Atalho_do_Menu_Iniciar_tem_o_nome_que_o_app_espera()
    {
        // B21: o app só reconhece o atalho "Notebook Doctor.lnk" direto na pasta Programas
        string nome = Path.GetFileNameWithoutExtension(StartMenuLink.FileName);
        Assert.Contains($"Name: \"{{autoprograms}}\\{nome}\"", Script().Replace("{#Nome}", nome));
    }

    [Fact]
    public void Instala_sem_administrador_porque_o_app_roda_como_invoker()
    {
        Assert.Matches(new Regex(@"^PrivilegesRequired=lowest\s*$", RegexOptions.Multiline), Script());
        Assert.Contains("requestedExecutionLevel level=\"asInvoker\"",
            File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "NotebookDoctor", "app.manifest")));
    }

    [Fact]
    public void Leva_o_executavel_das_duas_arquiteturas_e_o_mesmo_nome_do_que_o_updater_espera()
    {
        string iss = Script();
        Assert.Contains("OrigemX64", iss);
        Assert.Contains("OrigemArm64", iss);
        Assert.Contains("Check: IsArm64", iss);
        Assert.Contains("OutputBaseFilename=NotebookDoctor-Setup-{#Versao}", iss);
        // o gerar-release.ps1 publica o manifesto com este mesmo nome
        Assert.Contains("NotebookDoctor-Setup-$Versao.exe", File.ReadAllText(Path.Combine(RepoPaths.Root, "scripts", "release", "gerar-release.ps1")));
    }

    [Fact]
    public void Nao_liga_inicio_com_o_Windows_nem_apaga_dados_da_pessoa()
    {
        string iss = Script();
        Assert.DoesNotContain("CurrentVersion\\Run", iss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[UninstallDelete]", iss);
    }
}
