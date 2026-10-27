namespace NotebookDoctor.Services;

/// <summary>
/// Leva 11.1, tarefa 05: Modo sobrevivência. Liga o economizador de bateria do Windows, põe no modo eficiência os
/// programas sem janela que estão usando CPU, baixa a tela para 60 Hz e passa para o tema escuro. Guarda como estava
/// cada coisa (<see cref="SurvivalState"/>) para devolver ao ligar na tomada ou pelo Desfazer.
/// </summary>
public static class Survival
{
    public static (SurvivalState State, List<string> Done) Apply(Snapshot? latest)
    {
        var done = new List<string>();
        string? scheme = null; uint? saverAc = null, saverDc = null; int? hz = null, themeApps = null, themeSystem = null;
        var eco = new List<string>();

        if (PowerProbe.ReadSaverThreshold() is { } saver && saver.Dc < 100 && PowerProbe.WriteSaverThreshold(saver.Scheme, saver.Ac, 100))
        {
            (scheme, saverAc, saverDc) = (saver.Scheme.ToString(), saver.Ac, saver.Dc);
            done.Add(Loc.T("economizador de bateria ligado"));
        }
        foreach (var g in latest?.Groups ?? [])
        {
            if (g.HasWindow || g.CpuPct < 0.5 || ProcessTools.IsProtected(g.Name)) continue;
            if (ProcessActions.SetEfficiency(g.Name, true) > 0) eco.Add(g.Name);
        }
        if (eco.Count > 0) done.Add(Loc.F("{0} sem janela no modo eficiência", Pt.N(eco.Count, "programa", "programas")));
        if (PowerProbe.RefreshRates() is { } rates && SurvivalState.SaverRate(rates.Current, rates.Supported) is { } low && PowerProbe.SetRefreshRate(low))
        {
            hz = rates.Current;
            done.Add(Loc.F("tela em {0} Hz", low));
        }
        var (apps, system) = PowerProbe.ReadLightTheme();
        if (apps == 1 || system == 1)
        {
            PowerProbe.WriteLightTheme(apps == 1 ? 0 : null, system == 1 ? 0 : null);
            (themeApps, themeSystem) = (apps, system);
            done.Add(Loc.T("tema escuro"));
        }
        return (new SurvivalState(scheme, saverAc, saverDc, hz, themeApps, themeSystem, eco), done);
    }

    /// <summary>Devolve o que o Modo sobrevivência mudou. Devolve quantas coisas voltaram.</summary>
    public static int Restore(SurvivalState s)
    {
        int n = 0;
        if (s.Scheme is { } scheme && Guid.TryParse(scheme, out var g) && s.SaverAc is { } ac && s.SaverDc is { } dc && PowerProbe.WriteSaverThreshold(g, ac, dc)) n++;
        foreach (var name in s.Eco) if (ProcessActions.SetEfficiency(name, false) > 0) n++;
        if (s.Hz is { } hz && PowerProbe.SetRefreshRate(hz)) n++;
        if (s.ThemeApps is not null || s.ThemeSystem is not null) { PowerProbe.WriteLightTheme(s.ThemeApps, s.ThemeSystem); n++; }
        return n;
    }
}
