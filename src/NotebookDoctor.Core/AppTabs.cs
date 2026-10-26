using System.Globalization;
using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// Ordem das abas da janela principal. Jump list, notificações e <c>--tab=</c> usam o nome, que não muda quando uma
/// aba nova entra no meio (a Fase 5 pôs Programas entre Espaço e Inicialização).
/// </summary>
public static class AppTabs
{
    public const int Panel = 0, Processes = 1, Clean = 2, Space = 3, Programs = 4, Startup = 5, History = 6, Actions = 7, Tweaks = 8, System = 9, Settings = 10;

    public static readonly string[] Names = ["painel", "processos", "limpeza", "espaco", "programas", "inicializacao", "historico", "acoes", "ajustes", "sistema", "configuracoes"];

    public static string Name(int index) => Names[index];

    /// <summary>"programas", "Espaço" ou "4" → índice da aba; null quando não reconhece.</summary>
    public static int? Parse(string? text)
    {
        string t = Plain(text ?? "");
        if (t.Length == 0) return null;
        if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return n < Names.Length ? n : null;
        int i = Array.IndexOf(Names, t);
        return i >= 0 ? i : null;
    }

    /// <summary>Minúsculas e sem acento: "Configurações" → "configuracoes".</summary>
    private static string Plain(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }
}
