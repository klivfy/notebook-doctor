namespace NotebookDoctor.Services;

/// <summary>Uma linha do registro de Ações (a aba Ações e a linha do tempo do Histórico).</summary>
public sealed class ActionEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>Como desfazer: "power" (volta ao plano anterior), "startup" (reativa/desativa item), "plan-created" (apaga o plano criado), "leftovers" (restaura o backup das sobras). Vazio = não desfazível.</summary>
    public string UndoKind { get; set; } = "";
    public string UndoArg1 { get; set; } = "";
    public string UndoArg2 { get; set; } = "";
    public bool Undone { get; set; }

    public string TimeText => Loc.Stamp(Time);
    public bool CanUndo => UndoKind.Length > 0 && !Undone;
    public string Status => Undone ? "Desfeito" : UndoKind.Length > 0 ? "Pode desfazer" : "Definitivo";

    // M13: a grade mostra estes. O registro guarda o texto no idioma em que foi gravado; entradas antigas (ou de outro
    // idioma) que sejam um texto fixo conhecido saem traduzidas do mesmo jeito.
    public string TitleText => Loc.T(Title);
    public string DetailText => Loc.T(Detail);
    public string StatusText => Loc.T(Status);
}
