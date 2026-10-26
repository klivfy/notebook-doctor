namespace NotebookDoctor.Services;

/// <summary>Textos da leva 7.2 usados pela tela de drivers e pelo "Desfazer" de Ações.</summary>
public static class DriverTexts
{
    public static string Restore(RestoreOutcome outcome) => outcome switch
    {
        RestoreOutcome.Restored => Loc.T("Driver anterior de volta. Reinicie o computador se o Windows pedir."),
        RestoreOutcome.CopyMissing => Loc.T("A cópia do driver anterior não está mais na pasta."),
        RestoreOutcome.AddFailed => Loc.T("O Windows recusou instalar a cópia do driver anterior."),
        _ => Loc.T("A cópia foi instalada, mas o driver novo continua em uso. Use o Gerenciador de Dispositivos: Propriedades › Driver › Reverter driver."),
    };
}
