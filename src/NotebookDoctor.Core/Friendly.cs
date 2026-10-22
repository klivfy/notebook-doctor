namespace NotebookDoctor.Services;

/// <summary>
/// B27: variações de texto para as mensagens que a pessoa vê todo dia, para o app não soar repetitivo.
/// A escolha muda por dia, não a cada atualização: o texto não troca na frente da pessoa nem confunde o Narrador.
/// Títulos (o que vai para relatórios, logs e testes) continuam fixos; só a frase de apoio varia.
/// </summary>
public static class Friendly
{
    public static readonly string[] AllGood =
    [
        "Nenhum problema relevante agora.",
        "O computador está em boa forma. Pode seguir tranquilo.",
        "Nada pedindo atenção no momento.",
        "Tudo funcionando como deveria.",
    ];

    public static readonly string[] Freed =
    [
        "Arquivos em uso foram mantidos.",
        "Arquivos em uso ficaram como estavam.",
        "Só saiu o que não estava em uso.",
    ];

    public static readonly string[] NothingFreed =
    [
        "Os arquivos marcados estavam em uso ou já tinham sido apagados.",
        "Não sobrou nada para apagar: os arquivos estavam em uso ou já tinham saído.",
    ];

    /// <summary>Uma frase da lista, a mesma durante o dia inteiro.</summary>
    public static string Pick(IReadOnlyList<string> options, DateTime day) =>
        options.Count == 0 ? "" : options[day.Date.DayOfYear % options.Count];
}
