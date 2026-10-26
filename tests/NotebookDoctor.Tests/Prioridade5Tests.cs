using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Tests;

/// <summary>Prioridade 5: efeito de clique (B05), avaliação do app (B25) e estatísticas anônimas opcionais (M18).</summary>
public class ClickEffectTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)] // "Efeitos de animação" desligado no Windows
    [InlineData(true, true, false)]   // Alto Contraste
    [InlineData(false, true, false)]
    public void So_anima_com_as_animacoes_do_Windows_ligadas_e_fora_do_Alto_Contraste(bool animations, bool highContrast, bool expected) =>
        Assert.Equal(expected, ClickEffect.Allowed(animations, highContrast));

    [Fact]
    public void Usa_os_tempos_do_Windows_11()
    {
        Assert.Equal(167, ClickEffect.FastMs);
        Assert.Equal(250, ClickEffect.NormalMs);
        Assert.InRange(ClickEffect.PeakOpacity, 0.08, 0.25); // sutil: perto do estado "pressionado" (0,14)
    }

    [Theory]
    [InlineData(50, 16, 100, 32, 0.5, 0.5)]
    [InlineData(-10, 500, 100, 32, 0, 1)] // toque na borda, fora do botão: encosta na borda
    [InlineData(10, 10, 0, 0, 0.5, 0.5)]  // botão ainda sem tamanho: centro
    [InlineData(double.NaN, 5, 100, 10, 0.5, 0.5)]
    public void Origem_do_clique_fica_dentro_do_botao(double x, double y, double w, double h, double ex, double ey)
    {
        var (ox, oy) = ClickEffect.Origin(x, y, w, h);
        Assert.Equal(ex, ox, 3);
        Assert.Equal(ey, oy, 3);
    }

    [Fact]
    public void Raio_final_alcanca_o_canto_mais_distante()
    {
        Assert.Equal(Math.Sqrt(0.5), ClickEffect.EndRadius((0.5, 0.5)), 6);
        Assert.Equal(Math.Sqrt(2), ClickEffect.EndRadius((0, 0)), 6);
        Assert.Equal(1, ClickEffect.EndRadius((0, 1)) / Math.Sqrt(2), 6);
    }

    /// <summary>Todo estilo base de botão com o efeito tem o desenho dele ("Ripple") e o estilo liga o efeito.</summary>
    [Fact]
    public void Modelos_de_botao_tem_o_desenho_do_efeito_e_o_estilo_base_liga()
    {
        string xaml = File.ReadAllText(Path.Combine(RepoPaths.App, "Themes", "Controls.xaml"));
        Assert.Contains("local:ClickRipple.Enabled\" Value=\"True\"", xaml);
        foreach (Match template in Regex.Matches(xaml, "<ControlTemplate TargetType=\"Button\">.*?</ControlTemplate>", RegexOptions.Singleline))
            Assert.Contains("x:Name=\"Ripple\"", template.Value);
        Assert.Equal(3, Regex.Matches(xaml, "x:Name=\"Ripple\"").Count); // padrão, chip de estado e botão de ícone
        // O brilho usa só a cor do próprio botão (tema): nada de cor escrita no efeito
        Assert.DoesNotMatch(@"Ripple""[^>]*#[0-9A-Fa-f]{6}", xaml);
    }

    [Fact]
    public void Movimento_do_app_segue_a_mesma_regra()
    {
        string motion = File.ReadAllText(Path.Combine(RepoPaths.App, "Motion.cs"));
        Assert.Contains("ClickEffect.Allowed(SystemParameters.ClientAreaAnimation, SystemParameters.HighContrast)", motion);
        Assert.Contains("Motion.Enabled", File.ReadAllText(Path.Combine(RepoPaths.App, "ClickRipple.cs")));
    }
}

public class AnonymizeTests
{
    [Theory]
    [InlineData("fale comigo em ana.silva@exemplo.com.br ok", "fale comigo em <email> ok")]
    [InlineData("MAC 00:1A:2B:3C:4D:5E e 00-1a-2b-3c-4d-5e e 001a.2b3c.4d5e", "MAC <mac> e <mac> e <mac>")]
    [InlineData("IP 192.168.0.12 e 10.0.0.1", "IP <ip> e <ip>")]
    [InlineData("v6 fe80::1c2b:3d4e:5f60:7182 fim", "v6 <ip> fim")]
    [InlineData("id {3F2504E0-4F89-11D3-9A0C-0305E82C3301} fim", "id <id> fim")]
    [InlineData(@"abriu C:\Users\Ana\Documentos\contrato final.docx", "abriu <caminho> final.docx")]
    [InlineData(@"servidor \\fileserver\pasta\x.txt", "servidor <caminho>")]
    public void Tira_o_que_identifica(string input, string expected) => Assert.Equal(expected, Redaction.Anonymize(input));

    [Fact]
    public void Tira_usuario_computador_pasta_e_serie()
    {
        string r = Redaction.Anonymize(@"Ana no NOTE-ANA série SN12345X em C:\Users\Ana", @"C:\Users\Ana", "Ana", "NOTE-ANA", "CORP", "SN12345X");
        Assert.DoesNotContain("Ana", r);
        Assert.DoesNotContain("NOTE-ANA", r);
        Assert.DoesNotContain("SN12345X", r);
    }

    [Fact]
    public void Texto_comum_passa_inteiro()
    {
        const string text = "O app é ótimo! Versão 1.1.0, abriu em 2,5 s às 14:30.";
        Assert.Equal(text, Redaction.Anonymize(text));
        Assert.Equal("", Redaction.Anonymize(""));
    }
}

public class FeedbackTests
{
    [Theory]
    [InlineData(0, false)] [InlineData(1, true)] [InlineData(5, true)] [InlineData(6, false)]
    public void Nota_vai_de_1_a_5(int stars, bool ok) => Assert.Equal(ok, Feedback.ValidStars(stars));

    [Fact]
    public void Comentario_vira_texto_puro_sem_dados_pessoais_e_com_limite()
    {
        Assert.Equal("", Feedback.CleanComment(null));
        Assert.Equal("", Feedback.CleanComment("   \n  "));
        Assert.Equal("linha um linha dois", Feedback.CleanComment("linha um\r\n\r\n  linha\tdois\u0007"));
        Assert.Equal("mande para <email>", Feedback.CleanComment("mande para ana@x.com"));
        string longText = new('a', 2000);
        Assert.Equal(Feedback.MaxComment, Feedback.CleanComment(longText).Length);
    }

    [Fact]
    public void Envio_tem_so_os_campos_mostrados_e_nenhum_identificador()
    {
        string json = Feedback.Payload(4, "Gostei", "1.1.0", "pt");
        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(["app", "versao", "idioma", "nota", "comentario"], obj.Select(p => p.Key).ToArray());
        Assert.Equal(4, (int)obj["nota"]!);
        Assert.Equal("Gostei", (string)obj["comentario"]!);
        // Sem comentário, o campo nem existe
        Assert.False(JsonNode.Parse(Feedback.Payload(5, "", "1.1.0", "en"))!.AsObject().ContainsKey("comentario"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Feedback.Payload(0, "", "1.1.0", "pt"));
        Assert.Contains("ã", Feedback.Payload(3, "ótimo, não trava", "1.1.0", "pt")); // acentos legíveis na tela de conferência
    }

    [Fact]
    public void Enderecos_de_envio_sao_da_Klivfy()
    {
        Assert.True(KlivfyUrls.IsTrusted(KlivfyUrls.Rating));
        Assert.True(KlivfyUrls.IsTrusted(KlivfyUrls.Usage));
    }
}

public class TelemetryTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0);

    [Fact]
    public void So_conta_ferramentas_conhecidas()
    {
        var counts = new Dictionary<string, int>();
        Telemetry.Count(counts, "limpeza");
        Telemetry.Count(counts, "limpeza");
        Telemetry.Count(counts, "chrome.exe");                 // nome de programa: fora
        Telemetry.Count(counts, @"C:\Users\Ana\segredo.txt");  // arquivo: fora
        Telemetry.Count(counts, null);
        Assert.Equal(new Dictionary<string, int> { ["limpeza"] = 2 }, counts);
    }

    [Fact]
    public void Envio_nao_leva_identificador_nem_chave_estranha()
    {
        var uses = new Dictionary<string, int> { ["limpeza"] = 3, ["drivers"] = 1, ["NOTE-ANA"] = 9, ["servicos"] = 0 };
        var failures = new Dictionary<string, int> { ["app"] = 2, ["00:1A:2B:3C:4D:5E"] = 1 };
        string json = Telemetry.Payload(uses, failures, "1.1.0", "pt", Telemetry.WindowsFamily(22631), Now);
        var obj = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(["app", "versao", "idioma", "windows", "semana", "usos", "falhas"], obj.Select(p => p.Key).ToArray());
        Assert.Equal(["drivers", "limpeza"], obj["usos"]!.AsObject().Select(p => p.Key).ToArray());
        Assert.Equal(["app"], obj["falhas"]!.AsObject().Select(p => p.Key).ToArray());
        Assert.Equal("11", (string)obj["windows"]!);
        Assert.Equal("2026-W44", (string)obj["semana"]!);
        // Nada que identifique: sem MAC, IP, GUID, e-mail, caminho, nome do computador ou da pessoa
        Assert.Equal(json, Redaction.Anonymize(json));
        Assert.DoesNotContain("NOTE-ANA", json);
        Assert.DoesNotContain(Environment.UserName.Length >= 3 ? "\\" + Environment.UserName : "\\\\", json);
        Assert.DoesNotMatch(@"(?i)serial|serie|guid|\bid\b|mac|hostname|user", string.Join(" ", obj.Select(p => p.Key)));
    }

    [Fact]
    public void Desligada_nao_envia_e_ligada_envia_no_maximo_uma_vez_por_semana()
    {
        Assert.False(Telemetry.ShouldSend(false, DateTime.MinValue, Now, true));         // padrão: desligada
        Assert.False(Telemetry.ShouldSend(true, DateTime.MinValue, Now, false));         // nada a enviar
        Assert.True(Telemetry.ShouldSend(true, DateTime.MinValue, Now, true));
        Assert.False(Telemetry.ShouldSend(true, Now.AddDays(-6), Now, true));
        Assert.True(Telemetry.ShouldSend(true, Now.AddDays(-7), Now, true));
    }

    [Fact]
    public void Contadores_vazios_e_limite()
    {
        var empty = new Dictionary<string, int>();
        Assert.True(Telemetry.Empty(empty, empty));
        Assert.True(Telemetry.Empty(new Dictionary<string, int> { ["xyz"] = 5 }, empty));
        Assert.False(Telemetry.Empty(new Dictionary<string, int> { ["limpeza"] = 1 }, empty));
        var big = new Dictionary<string, int> { ["limpeza"] = 100_000 };
        Telemetry.Count(big, "limpeza");
        Assert.Equal(100_000, big["limpeza"]);
    }

    [Fact]
    public void Opcao_nasce_desligada_e_nao_viaja_na_importacao()
    {
        string settings = File.ReadAllText(Path.Combine(RepoPaths.App, "Services", "Settings.cs"));
        Assert.Matches(@"public bool TelemetryEnabled \{ get; set; \}(?! *=)", settings); // sem valor inicial: falso
        string exported = SettingsTransfer.Export("""{"TelemetryEnabled":true,"TelemetryUses":{"limpeza":3},"LastTelemetrySend":"2026-10-01T00:00:00","RatingSentAt":"2026-10-01T00:00:00","CheckUpdates":false}""", "1.1.0");
        var obj = JsonNode.Parse(exported)!.AsObject();
        Assert.False(obj.ContainsKey("TelemetryEnabled"));  // ligar é decisão de cada computador, nunca vem de um arquivo
        Assert.False(obj.ContainsKey("TelemetryUses"));
        Assert.False(obj.ContainsKey("LastTelemetrySend"));
        Assert.False(obj.ContainsKey("RatingSentAt"));
        Assert.True(obj.ContainsKey("CheckUpdates"));
    }
}
