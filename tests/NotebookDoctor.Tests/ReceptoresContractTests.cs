using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NotebookDoctor.Tests;

/// <summary>
/// Contrato com os receptores da Klivfy (RECEPTORES.md): o que o app envia precisa validar contra os esquemas publicados em
/// specs/receptores, e a lista de ferramentas do esquema precisa ser a do app. Um validador mínimo (só as palavras do esquema usadas aqui).
/// </summary>
public class ReceptoresContractTests
{
    private static JsonNode Schema(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root, "specs", "receptores", name + ".schema.json")))!;

    /// <summary>Devolve os erros ("" = válido) de <paramref name="value"/> contra <paramref name="schema"/>.</summary>
    private static List<string> Validate(JsonNode? value, JsonNode schema, JsonNode root, string path = "$")
    {
        var errors = new List<string>();
        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            JsonNode target = root;
            foreach (var part in reference.TrimStart('#', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)) target = target[part]!;
            return Validate(value, target, root, path);
        }
        if (schema["const"] is { } constant && value?.ToJsonString() != constant.ToJsonString()) errors.Add($"{path}: deveria ser {constant.ToJsonString()}");
        if (schema["enum"] is JsonArray options && !options.Any(o => o!.ToJsonString() == value?.ToJsonString())) errors.Add($"{path}: valor fora da lista ({value?.ToJsonString()})");
        switch (schema["type"]?.GetValue<string>())
        {
            case "string":
                if (value is not JsonValue sv || !sv.TryGetValue<string>(out var s)) { errors.Add($"{path}: deveria ser texto"); break; }
                if (schema["minLength"] is { } min && s.Length < min.GetValue<int>()) errors.Add($"{path}: curto demais");
                if (schema["maxLength"] is { } max && s.Length > max.GetValue<int>()) errors.Add($"{path}: longo demais");
                if (schema["pattern"] is { } pat && !Regex.IsMatch(s, pat.GetValue<string>())) errors.Add($"{path}: fora do padrão ({s})");
                break;
            case "integer":
                if (value is not JsonValue iv || !iv.TryGetValue<int>(out var n)) { errors.Add($"{path}: deveria ser inteiro"); break; }
                if (schema["minimum"] is { } lo && n < lo.GetValue<int>()) errors.Add($"{path}: menor que o mínimo");
                if (schema["maximum"] is { } hi && n > hi.GetValue<int>()) errors.Add($"{path}: maior que o máximo");
                break;
            case "object":
                if (value is not JsonObject obj) { errors.Add($"{path}: deveria ser objeto"); break; }
                var props = schema["properties"] as JsonObject;
                foreach (var req in schema["required"]?.AsArray() ?? [])
                    if (!obj.ContainsKey(req!.GetValue<string>())) errors.Add($"{path}: falta {req}");
                foreach (var (key, child) in obj)
                {
                    if (schema["propertyNames"] is { } names) errors.AddRange(Validate(JsonValue.Create(key), names, root, $"{path}.{{chave}}"));
                    if (props is not null && props[key] is { } sub) errors.AddRange(Validate(child, sub, root, $"{path}.{key}"));
                    else if (schema["additionalProperties"] is JsonObject extra) errors.AddRange(Validate(child, extra, root, $"{path}.{key}"));
                    else if (schema["additionalProperties"] is JsonValue { } flag && flag.TryGetValue<bool>(out var allowed) && !allowed) errors.Add($"{path}: campo não previsto ({key})");
                }
                break;
        }
        return errors;
    }

    private static void AssertValid(string json, string schemaName)
    {
        var schema = Schema(schemaName);
        var errors = Validate(JsonNode.Parse(json), schema, schema);
        Assert.True(errors.Count == 0, string.Join("\n", errors) + "\n" + json);
    }

    private static void AssertInvalid(string json, string schemaName)
    {
        var schema = Schema(schemaName);
        Assert.NotEmpty(Validate(JsonNode.Parse(json), schema, schema));
    }

    [Theory]
    [InlineData(1, "", "pt")]
    [InlineData(5, "Gostei, mas queria mais opções de limpeza", "pt")]
    [InlineData(3, "ótimo — não trava, 100% útil “mesmo”", "es")]
    public void Avaliacao_do_app_valida_contra_o_esquema(int stars, string comment, string language) =>
        AssertValid(Feedback.Payload(stars, Feedback.CleanComment(comment), "1.1.0", language), "avaliacao");

    [Fact]
    public void Avaliacao_com_o_comentario_no_limite_e_com_marcadores_valida()
    {
        string text = Feedback.CleanComment(new string('a', 2000) + " ana@x.com");
        Assert.Equal(Feedback.MaxComment, text.Length);
        AssertValid(Feedback.Payload(5, text, "1.1.0", "en"), "avaliacao");
        AssertValid(Feedback.Payload(4, Feedback.CleanComment("veja C:\\Users\\Ana\\a.txt e 10.0.0.1"), "1.1.0", "en"), "avaliacao");
    }

    [Fact]
    public void Esquema_da_avaliacao_recusa_o_que_o_app_nunca_manda()
    {
        AssertInvalid("""{"app":"NotebookDoctor","versao":"1.1.0","idioma":"pt","nota":6}""", "avaliacao");
        AssertInvalid("""{"app":"NotebookDoctor","versao":"1.1.0","idioma":"fr","nota":3}""", "avaliacao");
        AssertInvalid("""{"app":"NotebookDoctor","versao":"1.1.0","idioma":"pt","nota":3,"id":"x"}""", "avaliacao");
        AssertInvalid("""{"app":"NotebookDoctor","versao":"1.1.0","idioma":"pt","nota":3,"comentario":""}""", "avaliacao");
        AssertInvalid("""{"app":"Outro","versao":"1.1.0","idioma":"pt","nota":3}""", "avaliacao");
        AssertInvalid("""{"versao":"1.1.0","idioma":"pt","nota":3}""", "avaliacao");
    }

    [Fact]
    public void Estatisticas_do_app_validam_contra_o_esquema()
    {
        var now = new DateTime(2026, 10, 26);
        var empty = new Dictionary<string, int>();
        AssertValid(Telemetry.Payload(empty, empty, "1.1.0", "pt", "11", now), "estatisticas");
        var uses = Telemetry.Tools.ToDictionary(t => t, _ => 3);
        AssertValid(Telemetry.Payload(uses, new Dictionary<string, int> { ["app"] = 2 }, "1.1.0", "es", Telemetry.WindowsFamily(19045), now), "estatisticas");
        AssertValid(Telemetry.Payload(new Dictionary<string, int> { ["limpeza"] = 999_999 }, empty, "1.1.0", "en", "10", new DateTime(2026, 1, 1)), "estatisticas"); // limite de 100000
    }

    [Fact]
    public void Semanas_do_ano_do_app_sempre_seguem_o_padrao_do_esquema()
    {
        var schema = Schema("estatisticas")["properties"]!["semana"]!;
        var pattern = schema["pattern"]!.GetValue<string>();
        for (var day = new DateTime(2026, 1, 1); day < new DateTime(2030, 1, 1); day = day.AddDays(1))
            Assert.Matches(pattern, Telemetry.Week(day));
    }

    [Fact]
    public void Lista_de_ferramentas_do_esquema_e_a_do_app()
    {
        var names = Schema("estatisticas")["$defs"]!["contagens"]!["propertyNames"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet();
        Assert.True(names.SetEquals(Telemetry.Tools), "esquema: " + string.Join(",", names.Except(Telemetry.Tools)) + " | app: " + string.Join(",", Telemetry.Tools.Except(names)));
    }

    [Fact]
    public void Esquema_das_estatisticas_recusa_identificadores_e_chaves_fora_da_lista()
    {
        const string head = "{\"app\":\"NotebookDoctor\",\"versao\":\"1.1.0\",\"idioma\":\"pt\",\"windows\":\"11\",\"semana\":\"2026-W44\",";
        AssertInvalid(head + "\"usos\":{\"chrome.exe\":3},\"falhas\":{}}", "estatisticas");
        AssertInvalid(head + "\"usos\":{},\"falhas\":{},\"id\":\"x\"}", "estatisticas");
        AssertInvalid(head + "\"usos\":{\"limpeza\":0},\"falhas\":{}}", "estatisticas");
        AssertInvalid(head + "\"usos\":{\"limpeza\":1}}", "estatisticas");
        AssertInvalid("{\"app\":\"NotebookDoctor\",\"versao\":\"1.1.0\",\"idioma\":\"pt\",\"windows\":\"11\",\"semana\":\"2026-44\",\"usos\":{},\"falhas\":{}}", "estatisticas");
        AssertInvalid("{\"app\":\"NotebookDoctor\",\"versao\":\"1.1.0\",\"idioma\":\"pt\",\"windows\":\"11.0.22631\",\"semana\":\"2026-W44\",\"usos\":{},\"falhas\":{}}", "estatisticas");
    }

    [Fact]
    public void Documento_cita_os_mesmos_enderecos_do_app()
    {
        string doc = File.ReadAllText(Path.Combine(RepoPaths.Root, "RECEPTORES.md"));
        Assert.Contains(KlivfyUrls.Rating, doc);
        Assert.Contains(KlivfyUrls.Usage, doc);
        foreach (var tool in Telemetry.Tools) Assert.Contains($"`{tool}`", doc);
    }
}
