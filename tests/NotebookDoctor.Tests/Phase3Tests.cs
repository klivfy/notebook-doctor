using System.Text.Json.Nodes;

namespace NotebookDoctor.Tests;

public class ColorMathTests
{
    [Fact]
    public void Contraste_de_preto_e_branco_e_21() =>
        Assert.Equal(21, ColorMath.Contrast(ColorMath.Black, ColorMath.White), 1);

    [Fact]
    public void Le_a_cor_do_registro_do_Windows() =>
        Assert.Equal(new Rgb(0x00, 0x78, 0xD4), Rgb.FromAbgr(0xFFD47800));

    [Theory]
    [InlineData(0x00, 0x78, 0xD4, 0x1F, 0x1F, 0x1F)] // azul do Windows sobre fundo escuro
    [InlineData(0xFF, 0xB9, 0x00, 0xFF, 0xFF, 0xFF)] // amarelo sobre fundo branco
    [InlineData(0x10, 0x10, 0x10, 0x12, 0x12, 0x12)] // quase preto sobre fundo escuro
    public void Cor_ajustada_sempre_e_legivel(byte r, byte g, byte b, byte br, byte bg, byte bb)
    {
        var bgColor = new Rgb(br, bg, bb);
        Assert.True(ColorMath.Contrast(ColorMath.Readable(new Rgb(r, g, b), bgColor), bgColor) >= 4.5);
    }

    [Fact]
    public void Cor_que_ja_contrasta_fica_igual()
    {
        var navy = new Rgb(0x00, 0x18, 0x8F);
        Assert.Equal(navy, ColorMath.Readable(navy, ColorMath.White));
    }

    [Theory]
    [InlineData(0x00, 0x18, 0x8F, 255)] // azul escuro: texto branco
    [InlineData(0xFF, 0xB9, 0x00, 0)]   // amarelo: texto preto
    public void Texto_sobre_a_cor(byte r, byte g, byte b, byte expected) =>
        Assert.Equal(expected, ColorMath.OnColor(new Rgb(r, g, b)).R);
}

public class SettingsTransferTests
{
    private const string Current = """{"ThemeMode":"dark","CpuAlertPct":80,"MiniLeft":120.5,"LastTab":3,"FirstRunDone":true,"AlertExceptions":["obs64"]}""";

    [Fact]
    public void Exporta_sem_dados_da_maquina()
    {
        var obj = JsonNode.Parse(SettingsTransfer.Export(Current, "1.2.0"))!.AsObject();
        Assert.Equal(SettingsTransfer.Marker, (string?)obj["_tipo"]);
        Assert.Equal("dark", (string?)obj["ThemeMode"]);
        Assert.Equal("obs64", (string?)obj["AlertExceptions"]![0]);
        Assert.False(obj.ContainsKey("MiniLeft"));
        Assert.False(obj.ContainsKey("LastTab"));
        Assert.False(obj.ContainsKey("FirstRunDone"));
    }

    [Fact]
    public void Importa_por_cima_mantendo_os_dados_da_maquina()
    {
        string imported = """{"_tipo":"NotebookDoctorSettings","_versao":"1.2.0","ThemeMode":"light","CpuAlertPct":70,"MiniLeft":999}""";
        var merged = JsonNode.Parse(SettingsTransfer.Merge(Current, imported))!.AsObject();
        Assert.Equal("light", (string?)merged["ThemeMode"]);
        Assert.Equal(70, (int?)merged["CpuAlertPct"]);
        Assert.Equal(120.5, (double?)merged["MiniLeft"]);  // posição do widget é desta máquina
        Assert.Equal("obs64", (string?)merged["AlertExceptions"]![0]); // o que não veio no arquivo fica
        Assert.False(merged.ContainsKey("_tipo"));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"ThemeMode":"light"}""")]
    [InlineData("""{"_tipo":"OutroApp"}""")]
    public void Recusa_arquivo_que_nao_e_do_app(string imported) =>
        Assert.Throws<FormatException>(() => SettingsTransfer.Merge(Current, imported));

    [Fact]
    public void Ida_e_volta_preserva_as_preferencias()
    {
        var back = JsonNode.Parse(SettingsTransfer.Merge("{}", SettingsTransfer.Export(Current, "1.2.0")))!.AsObject();
        Assert.Equal("dark", (string?)back["ThemeMode"]);
        Assert.Equal(80, (int?)back["CpuAlertPct"]);
    }

    [Fact]
    public void Redefinir_volta_ao_padrao_mas_mantem_dados_da_maquina()
    {
        var reset = JsonNode.Parse(SettingsTransfer.Reset(Current, """{"ThemeMode":"system","CpuAlertPct":80,"FirstRunDone":false,"AlertExceptions":[]}"""))!.AsObject();
        Assert.Equal("system", (string?)reset["ThemeMode"]);
        Assert.Empty(reset["AlertExceptions"]!.AsArray());
        Assert.True((bool?)reset["FirstRunDone"]); // não mostra as boas-vindas de novo
        Assert.Equal(120.5, (double?)reset["MiniLeft"]);
    }
}
