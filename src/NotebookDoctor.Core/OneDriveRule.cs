namespace NotebookDoctor.Services;

/// <param name="LocalBytes">Arquivos baixados neste computador (ocupam disco)</param>
/// <param name="OnlineBytes">Arquivos "só online" (Arquivos sob Demanda): aparecem na pasta, mas não ocupam disco</param>
/// <param name="Partial">A contagem parou no limite de tempo ou de arquivos: os números são o mínimo</param>
public sealed record OneDriveInfo(string Folder, long LocalBytes, long OnlineBytes, bool Partial = false);

/// <summary>
/// M33: OneDrive com tudo baixado ocupa no disco o mesmo que na nuvem. Com "Arquivos sob Demanda", os arquivos
/// ficam só online até serem abertos, e "Liberar espaço" devolve o que já foi baixado.
/// </summary>
public static class OneDriveRule
{
    // Atributos do Windows para arquivos de nuvem
    public const int Offline = 0x1000, RecallOnOpen = 0x40000, RecallOnDataAccess = 0x400000, Pinned = 0x80000;

    /// <summary>Arquivo só online: o conteúdo é baixado quando alguém o abre.</summary>
    public static bool IsOnlineOnly(int attributes) => (attributes & (RecallOnDataAccess | RecallOnOpen | Offline)) != 0;

    /// <summary>Vale avisar: muito arquivo baixado com o disco apertado, ou uma quantidade grande em qualquer caso.</summary>
    public static bool ShouldWarn(OneDriveInfo o, double diskFreePct) =>
        o.LocalBytes >= 20L << 30 || (o.LocalBytes >= 3L << 30 && diskFreePct < 15);

    public static Finding? Evaluate(OneDriveInfo? o, double diskFreePct)
    {
        if (o is null || !ShouldWarn(o, diskFreePct)) return null;
        string online = o.OnlineBytes > 0 ? $"; {Pt.Bytes(o.OnlineBytes)} já estão só online" : "";
        return new Finding("ATENCAO", $"OneDrive ocupa {Pt.Bytes(o.LocalBytes)}{(o.Partial ? " ou mais" : "")} neste computador",
            $"{o.Folder}{online}",
            "Ative \"Arquivos sob Demanda\" nas configurações do OneDrive e, na pasta, use clique direito › \"Liberar espaço\" no que não precisa offline. Os arquivos continuam na nuvem e aparecem na pasta.",
            "open-folder", "Abrir pasta do OneDrive", o.Folder);
    }
}
