using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Histórico: gráficos do período, CSV, linha do tempo por programa e avisos.</summary>
public partial class MainWindow
{
    // ---------- Histórico ----------
    private async void LoadHistory()
    {
        var window = PeriodBox.SelectedIndex switch { 1 => TimeSpan.FromDays(7), 2 => TimeSpan.FromDays(30), _ => TimeSpan.FromHours(24) };
        // Tendências usam o mês inteiro, qualquer que seja o período escolhido
        var month = await Task.Run(() => Host.History.Load(TimeSpan.FromDays(30)));
        var data = window.TotalDays >= 30 ? month : month.Where(d => d.T >= DateTime.Now - window).ToList();
        ShowTrends(month);
        _histData = data;
        LoadTimeline(window, data);
        FillProcTimeline(data);
        if (data.Count < 2)
        {
            HistInfo.Text = Loc.T("Ainda sem dados suficientes: o app grava uma amostra por minuto.");
            HCpu.SetValues(Array.Empty<double>()); HRam.SetValues(Array.Empty<double>()); HFree.SetValues(Array.Empty<double>()); HScore.SetValues(Array.Empty<double>());
            HOffenders.Text = "–";
            return;
        }
        HistInfo.Text = Pt.N(data.Count, "amostra", "amostras");
        var times = HistoryStore.DownsampleTimes(data.Select(d => d.T).ToList(), 300);
        HCpu.SetValues(HistoryStore.Downsample(data.Select(d => d.Cpu).ToList(), 300), times);
        HRam.SetValues(HistoryStore.Downsample(data.Select(d => d.Ram).ToList(), 300), times);
        HFree.SetValues(HistoryStore.Downsample(data.Select(d => d.FreePct).ToList(), 300), times);
        HCpuLabel.Text = Loc.F("CPU · média {0:0}% · pico {1:0}%", data.Average(d => d.Cpu), data.Max(d => d.Cpu));
        HRamLabel.Text = Loc.F("RAM · média {0:0}% · pico {1:0}%", data.Average(d => d.Ram), data.Max(d => d.Ram));
        HDiskLabel.Text = Loc.F("Espaço livre no disco · agora {0:0}%", data[^1].FreePct);
        var scores = data.Where(d => d.Score >= 0).Select(d => (double)d.Score).ToList();
        HScore.SetValues(HistoryStore.Downsample(scores, 300));
        HScoreLabel.Text = scores.Count > 1 ? Loc.F("Nota de saúde · média {0:0} · mínima {1:0}", scores.Average(), scores.Min()) : Loc.T("Nota de saúde · coletando dados");

        var heavy = data.Where(d => d.Cpu >= 50 || d.Ram >= 80).ToList();
        var basis = heavy.Count >= 5 ? heavy : data;
        var ranking = basis.SelectMany(d => d.Top).GroupBy(n => n)
            .Select(g => (Name: g.Key, Pct: g.Count() * 100.0 / basis.Count)).OrderByDescending(x => x.Pct).Take(6);
        string head = (heavy.Count >= 5 ? Loc.F("Nos {0} momentos de carga alta (CPU ≥ 50% ou RAM ≥ 80%):", heavy.Count) : Loc.T("Nenhum período de carga alta registrado; ranking geral:")) + "\n";
        HOffenders.Text = head + string.Join("\n", ranking.Select(r => "• " + Loc.F("{0} — entre os 3 maiores em {1:0}% das amostras", r.Name, r.Pct)));
    }

    // ---------- Tendências: hoje × semana passada (M48) e previsão do disco (M50) ----------
    private void ShowTrends(List<HistSample> month)
    {
        var now = DateTime.Now;
        if (HistoryInsights.CompareWithLastWeek(month, now) is { } c)
        {
            static string Diff(double today, double last, string unit) =>
                Math.Abs(today - last) < 2 ? Loc.F("igual ({0})", $"{today:0}{unit}") : Loc.F("{0} contra {1}", $"{today:0}{unit}", $"{last:0}{unit}") + (today > last ? " ▲" : " ▼");
            HCompare.Text = Loc.T("Últimas 24 h comparadas com o mesmo horário da semana passada:") + "\n"
                + "• " + Loc.F("CPU média: {0}", Diff(c.Today.Cpu, c.LastWeek.Cpu, "%")) + "\n"
                + "• " + Loc.F("RAM média: {0}", Diff(c.Today.Ram, c.LastWeek.Ram, "%"))
                + (c.Today.Score is { } a && c.LastWeek.Score is { } b ? "\n• " + Loc.F("Nota de saúde: {0}", Diff(a, b, "")) : "");
        }
        else HCompare.Text = Loc.T("Comparação com a semana passada: aparece quando houver uso registrado hoje e no mesmo dia da semana passada.");

        HDiskForecast.Text = HistoryInsights.DiskFull(month, now) is { } f
            ? Loc.F("Disco: o espaço livre cai cerca de {0:0.0}% do disco por dia. Nesse ritmo, ele enche por volta de {1:d}.", f.PctPerDay, f.When)
            : Loc.T("Disco: sem tendência de encher nos dados do último mês.");
        ShowHeatmap(month);
    }

    // ---------- M49: mapa de calor de lentidão (dia da semana × hora) ----------
    private static readonly string[] DayNames = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];
    private static readonly string[] DayNamesLong = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"];

    private void ShowHeatmap(List<HistSample> month)
    {
        var map = HistoryInsights.SlownessHeatmap(month);
        HeatGrid.Children.Clear();
        HeatGrid.RowDefinitions.Clear();
        HeatGrid.ColumnDefinitions.Clear();
        HeatGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int h = 0; h < 24; h++) HeatGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        for (int r = 0; r < 8; r++) HeatGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });

        void Place(UIElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); HeatGrid.Children.Add(e); }
        // U11: rótulos com o menor token de texto (12 px), que cresce com o "Tamanho do texto" do Windows
        TextBlock Label(string text)
        {
            var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            label.SetResourceReference(TextBlock.FontSizeProperty, "FsCaption");
            return label;
        }

        for (int h = 0; h < 24; h += 3) Place(Label($"{h}h"), 0, h + 1);
        for (int d = 0; d < 7; d++)
        {
            Place(Label(Loc.T(DayNames[d])), d + 1, 0);
            for (int h = 0; h < 24; h++)
            {
                double v = map[d, h];
                var cell = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(3) };
                if (double.IsNaN(v))
                {
                    cell.SetResourceReference(Border.BackgroundProperty, "SurfaceVariant");
                    cell.ToolTip = Loc.F("{0}, {1}h: sem dados", Loc.T(DayNamesLong[d]), h);
                }
                else
                {
                    // Cor de carga do tema; a intensidade mostra a fração do tempo sob carga
                    cell.SetResourceReference(Border.BackgroundProperty, "LevelCrit");
                    cell.Opacity = 0.12 + 0.88 * v;
                    cell.ToolTip = Loc.F("{0}, {1}h: sob carga em {2:P0} do tempo", Loc.T(DayNamesLong[d]), h, v);
                }
                System.Windows.Automation.AutomationProperties.SetName(cell, cell.ToolTip as string);
                Place(cell, d + 1, h + 1);
            }
        }

        HHeatInfo.Text = HistoryInsights.Worst(map) is { } w
            ? Loc.F("Pior horário: {0} por volta das {1}h, com CPU ou RAM no limite em {2:P0} do tempo. Quanto mais forte a cor, mais tempo sob carga.", Loc.T(DayNamesLong[(int)w.Day]), w.Hour, w.Share)
            : Loc.T("Nenhum horário com carga frequente no último mês. Quanto mais forte a cor, mais tempo sob carga; cinza é horário sem dados.");
    }

    // ---------- Histórico: CSV (M06), linha do tempo por programa (M05) e avisos (M46) ----------
    private List<HistSample> _histData = new();

    private string PeriodLabel => PeriodBox.SelectedIndex switch { 1 => "7-dias", 2 => "30-dias", _ => "24-horas" };

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_histData.Count == 0) { HistInfo.Text = Loc.T("Ainda não há dados no período para exportar."); return; }
        try
        {
            string file = HistoryCsv.Export(_histData, PeriodLabel);
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\"");
            ShowSnack("Histórico exportado", Loc.F("{0} em {1}. O Explorador abriu na pasta do arquivo.", Pt.N(_histData.Count, "amostra", "amostras"), System.IO.Path.GetFileName(file)));
        }
        catch (Exception ex) { ShowSnack("Não foi possível exportar", ex.Message); }
    }

    // ---------- M47: linha do tempo do período ----------
    private async void LoadTimeline(TimeSpan window, List<HistSample> data)
    {
        var from = DateTime.Now - window;
        var events = await Task.Run(() => Timeline.Merge(
            AlertLog.Load(window).Select(a => new TimelineEvent(a.Time, a.Silenced ? "Aviso (silenciado)" : "Aviso", a.Title, a.Text)),
            ActionLog.Load().Where(a => a.Time >= from).Select(a => new TimelineEvent(a.Time, "Ação do app", a.Title, a.Detail)),
            DiskUsage.InstalledApps().Where(a => a.InstallDate >= from.Date)
                .Select(a => new TimelineEvent(a.InstallDate!.Value, "Instalação", a.Name, "Data informada pelo instalador (sem hora)")),
            Timeline.Peaks(data)));
        AlertsGrid.ItemsSource = events;
        AlertsGrid.Visibility = events.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlertsEmpty.Visibility = events.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FillProcTimeline(List<HistSample> data)
    {
        string? keep = ProcTimelineBox.SelectedItem as string;
        // Programas do período, dos que mais pesaram para os menos (CPU somada + memória, 1 GB ≈ 10 pontos de CPU)
        var names = data.Where(d => d.P is not null).SelectMany(d => d.P!)
            .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Weight: g.Sum(kv => kv.Value[0] + kv.Value[1] / 100)))
            .Where(x => x.Weight > 0 && !ProcessTools.IsSystem(x.Name) || x.Weight > 50)
            .OrderByDescending(x => x.Weight).Select(x => x.Name).Take(60).ToList();
        ProcTimelineBox.ItemsSource = names;
        if (names.Count == 0)
        {
            ProcTimelineInfo.Text = Loc.T("Os dados por programa começam a ser gravados nesta versão (uma amostra por minuto).");
            PCpu.SetValues(Array.Empty<double>()); PRam.SetValues(Array.Empty<double>());
            return;
        }
        ProcTimelineBox.SelectedItem = keep is not null && names.Contains(keep) ? keep : names[0];
    }

    private void ProcTimeline_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProcTimelineBox.SelectedItem is not string name) return;
        var rows = _histData.Where(d => d.P is not null).ToList();
        // Ausente na amostra = não estava entre os mais pesados (fica como zero)
        double[] Val(HistSample d, int i) => d.P!.TryGetValue(name, out var v) ? v : [0, 0];
        var cpu = rows.Select(d => Val(d, 0)[0]).ToList();
        var ram = rows.Select(d => Val(d, 0)[1]).ToList();
        var times = HistoryStore.DownsampleTimes(rows.Select(d => d.T).ToList(), 300);
        PCpu.SetValues(HistoryStore.Downsample(cpu, 300), times);
        PRam.Max = Math.Max(100, ram.DefaultIfEmpty(0).Max() * 1.1);
        PRam.SetValues(HistoryStore.Downsample(ram, 300), times);
        int present = rows.Count(d => d.P!.ContainsKey(name));
        PCpuLabel.Text = Loc.F("CPU · média {0:0.0}% · pico {1:0}%", cpu.DefaultIfEmpty(0).Average(), cpu.DefaultIfEmpty(0).Max());
        PRamLabel.Text = Loc.F("Memória · pico {0:N0} MB", ram.DefaultIfEmpty(0).Max());
        ProcTimelineInfo.Text = rows.Count == 0 ? "" : Loc.F("Entre os mais pesados em {0:0}% das amostras", present * 100.0 / rows.Count);
    }

    private void Period_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && e.Source == PeriodBox) LoadHistory();
    }
}
