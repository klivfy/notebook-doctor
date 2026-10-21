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
        LoadAlerts(window);
        FillProcTimeline(data);
        if (data.Count < 2)
        {
            HistInfo.Text = "Ainda sem dados suficientes: o app grava uma amostra por minuto.";
            HCpu.SetValues(Array.Empty<double>()); HRam.SetValues(Array.Empty<double>()); HFree.SetValues(Array.Empty<double>()); HScore.SetValues(Array.Empty<double>());
            HOffenders.Text = "–";
            return;
        }
        HistInfo.Text = $"{data.Count} amostras";
        var times = HistoryStore.DownsampleTimes(data.Select(d => d.T).ToList(), 300);
        HCpu.SetValues(HistoryStore.Downsample(data.Select(d => d.Cpu).ToList(), 300), times);
        HRam.SetValues(HistoryStore.Downsample(data.Select(d => d.Ram).ToList(), 300), times);
        HFree.SetValues(HistoryStore.Downsample(data.Select(d => d.FreePct).ToList(), 300), times);
        HCpuLabel.Text = $"CPU · média {data.Average(d => d.Cpu):0}% · pico {data.Max(d => d.Cpu):0}%";
        HRamLabel.Text = $"RAM · média {data.Average(d => d.Ram):0}% · pico {data.Max(d => d.Ram):0}%";
        HDiskLabel.Text = $"Espaço livre no disco · agora {data[^1].FreePct:0}%";
        var scores = data.Where(d => d.Score >= 0).Select(d => (double)d.Score).ToList();
        HScore.SetValues(HistoryStore.Downsample(scores, 300));
        HScoreLabel.Text = scores.Count > 1 ? $"Nota de saúde · média {scores.Average():0} · mínima {scores.Min():0}" : "Nota de saúde · coletando dados";

        var heavy = data.Where(d => d.Cpu >= 50 || d.Ram >= 80).ToList();
        var basis = heavy.Count >= 5 ? heavy : data;
        var ranking = basis.SelectMany(d => d.Top).GroupBy(n => n)
            .Select(g => (Name: g.Key, Pct: g.Count() * 100.0 / basis.Count)).OrderByDescending(x => x.Pct).Take(6);
        string head = heavy.Count >= 5 ? $"Nos {heavy.Count} momentos de carga alta (CPU ≥ 50% ou RAM ≥ 80%):\n" : "Nenhum período de carga alta registrado; ranking geral:\n";
        HOffenders.Text = head + string.Join("\n", ranking.Select(r => $"• {r.Name} — entre os 3 maiores em {r.Pct:0}% das amostras"));
    }

    // ---------- Tendências: hoje × semana passada (M48) e previsão do disco (M50) ----------
    private void ShowTrends(List<HistSample> month)
    {
        var now = DateTime.Now;
        if (HistoryInsights.CompareWithLastWeek(month, now) is { } c)
        {
            static string Diff(double today, double last, string unit) =>
                Math.Abs(today - last) < 2 ? $"igual ({today:0}{unit})" : $"{today:0}{unit} contra {last:0}{unit} {(today > last ? "▲" : "▼")}";
            HCompare.Text = "Últimas 24 h comparadas com o mesmo horário da semana passada:\n"
                + $"• CPU média: {Diff(c.Today.Cpu, c.LastWeek.Cpu, "%")}\n"
                + $"• RAM média: {Diff(c.Today.Ram, c.LastWeek.Ram, "%")}"
                + (c.Today.Score is { } a && c.LastWeek.Score is { } b ? $"\n• Nota de saúde: {Diff(a, b, "")}" : "");
        }
        else HCompare.Text = "Comparação com a semana passada: aparece quando houver uso registrado hoje e no mesmo dia da semana passada.";

        HDiskForecast.Text = HistoryInsights.DiskFull(month, now) is { } f
            ? $"Disco: o espaço livre cai cerca de {f.PctPerDay:0.0}% do disco por dia. Nesse ritmo, ele enche por volta de {f.When:dd/MM/yyyy}."
            : "Disco: sem tendência de encher nos dados do último mês.";
    }

    // ---------- Histórico: CSV (M06), linha do tempo por programa (M05) e avisos (M46) ----------
    private List<HistSample> _histData = new();

    private string PeriodLabel => PeriodBox.SelectedIndex switch { 1 => "7-dias", 2 => "30-dias", _ => "24-horas" };

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_histData.Count == 0) { HistInfo.Text = "Ainda não há dados no período para exportar."; return; }
        try
        {
            string file = HistoryCsv.Export(_histData, PeriodLabel);
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\"");
            ShowSnack("Histórico exportado", $"{Pt.N(_histData.Count, "amostra", "amostras")} em {System.IO.Path.GetFileName(file)}. O Explorador abriu na pasta do arquivo.");
        }
        catch (Exception ex) { ShowSnack("Não foi possível exportar", ex.Message); }
    }

    private void LoadAlerts(TimeSpan window)
    {
        var alerts = AlertLog.Load(window);
        AlertsGrid.ItemsSource = alerts;
        AlertsGrid.Visibility = alerts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlertsEmpty.Visibility = alerts.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
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
            ProcTimelineInfo.Text = "Os dados por programa começam a ser gravados nesta versão (uma amostra por minuto).";
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
        PCpuLabel.Text = $"CPU · média {cpu.DefaultIfEmpty(0).Average():0.0}% · pico {cpu.DefaultIfEmpty(0).Max():0}%";
        PRamLabel.Text = $"Memória · pico {ram.DefaultIfEmpty(0).Max():N0} MB";
        ProcTimelineInfo.Text = rows.Count == 0 ? "" : $"Entre os mais pesados em {present * 100.0 / rows.Count:0}% das amostras";
    }

    private void Period_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && e.Source == PeriodBox) LoadHistory();
    }
}
