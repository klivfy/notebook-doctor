using System.Collections.ObjectModel;
using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class SnoozeRow
{
    public SnoozeApp? App { get; }
    public SnoozedApp? Record { get; }
    public SnoozeRow(SnoozeApp app) { App = app; }
    public SnoozeRow(SnoozedApp record) { Record = record; }

    public string Key => App?.Key ?? Record!.Key;
    public string Name => App?.Name ?? Record!.Name;
    public bool Asleep => Record is not null;
    public bool Blocked => App is { Block: not null };

    public string Parts => App is not null ? SnoozeRules.Describe(App)
        : Loc.F("{0} peças desligadas", Record!.Items.Count(i => i.Applied));
    public string RamText => (App?.RamMb ?? Record!.RamMb) is var mb and > 0 ? Units.Bytes(mb * 1048576) : "";
    public string CpuText => (App?.CpuSeconds ?? Record!.CpuSeconds) is var s and > 0 ? Loc.F("{0} s", (long)s) : "";

    public string StateText
    {
        get
        {
            if (Record is { } r)
            {
                string since = r.Since.ToString("g", Loc.Culture);
                if (r.Awake) return Loc.F("Acordado porque você abriu; volta a dormir ao fechar (dormia desde {0})", since);
                bool pending = r.Items.Any(i => !i.Applied);
                return Loc.F("Dormindo desde {0}", since) + (pending ? " · " + Loc.T("algumas peças exigem administrador") : "")
                       + (r.WakeOnOpen ? "" : " · " + Loc.T("só acorda por você"));
            }
            if (App!.Block is { } b) return Loc.T(b);
            if (App.InUse) return Loc.T("Aberto: feche o programa antes");
            return App.Warn is not null ? Loc.T("Pode dormir (a sincronização para)") : Loc.T("Pode dormir");
        }
    }
}

/// <summary>Leva 7.3 (C12): modo soneca. Dormir e acordar passam pelo <see cref="SnoozeEngine"/>, que guarda como voltar cada peça.</summary>
public partial class SnoozeWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private readonly ObservableCollection<SnoozeRow> _rows = [];
    private readonly ISnoozeSystem _system = new WindowsSnoozeSystem();
    private bool _busy;

    public SnoozeWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        Grid.ItemsSource = _rows;
        WakeOnOpen.IsChecked = Host.Settings.SnoozeWakeOnOpen;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) => await ReloadAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Msg.Text = Loc.T("Espere terminar o que está em andamento."); return; }
        Close();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void WakeOnOpen_Click(object sender, RoutedEventArgs e)
    {
        Host.Settings.SnoozeWakeOnOpen = WakeOnOpen.IsChecked == true;
        Host.Settings.Save();
    }

    private SnoozeRow? Current => Grid.SelectedItem as SnoozeRow;

    private async Task ReloadAsync()
    {
        if (_busy) return;
        _busy = true;
        Info.Text = Loc.T("Procurando o que roda sozinho…");
        try
        {
            var never = Host.Settings.SnoozeNever.ToList();
            List<SnoozeApp> candidates;
            if (AppPaths.Demo) candidates = DemoApps();
            else candidates = await Task.Run(() => SnoozeScanner.Scan(never));
            var asleep = AppPaths.Demo ? [] : Host.Snooze.Load();
            _rows.Clear();
            foreach (var r in asleep.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)) _rows.Add(new SnoozeRow(r));
            foreach (var a in candidates.Where(a => asleep.All(r => r.Key != a.Key))) _rows.Add(new SnoozeRow(a));
            long ram = candidates.Where(a => a.CanSleep).Sum(a => a.RamMb);
            Info.Text = Loc.F("{0} programas rodam sozinhos · {1} em soneca · os que podem dormir somam {2} de RAM em segundo plano.",
                candidates.Count, asleep.Count, Units.Bytes(ram * 1048576));
        }
        finally { _busy = false; }
    }

    private static List<SnoozeApp> DemoApps() =>
    [
        new()
        {
            Key = "steam", Name = "Steam", Startup = [new("Steam", "Run", "steam.exe -silent", @"C:\Program Files (x86)\Steam\steam.exe", true)],
            Services = [new("SteamService", "Steam Client Service", @"C:\Program Files (x86)\Common Files\Steam\steamservice.exe", 2)],
            Background = [new("steamwebhelper", "", 420, 180, false, 4)], MainExes = ["steam"],
        },
        new()
        {
            Key = "onedrive", Name = "OneDrive", Startup = [new("OneDrive", "Run", "OneDrive.exe /background", @"C:\Program Files\Microsoft OneDrive\OneDrive.exe", true)],
            Background = [new("OneDrive", "", 210, 95, false, 1)], MainExes = ["onedrive"], Warn = SnoozeRules.WarnReason("OneDrive", []),
        },
        new() { Key = "realtek", Name = "Realtek Audio", Startup = [new("RtkNGUI", "Run", "RtkNGUI64.exe", @"C:\Program Files\Realtek\RtkNGUI64.exe", true)], Block = "Segurança, driver, rede ou acesso remoto: fica como está" },
    ];

    // ---------- dormir ----------

    private async void Sleep_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Current is not { } row) { Msg.Text = Loc.T("Selecione um programa na lista."); return; }
        if (row.Asleep) { Msg.Text = Loc.T("Este programa já está em soneca."); return; }
        var app = row.App!;
        if (!app.CanSleep) { Msg.Text = row.StateText; return; }

        bool admin = AppInfo.IsAdmin;
        var lines = new List<string>();
        if (app.Startup.Count > 0) lines.Add("• " + Loc.F("Inicialização desligada: {0}", string.Join(", ", app.Startup.Select(s => s.Name))));
        if (app.Services.Count > 0)
            lines.Add("• " + (admin ? Loc.F("Serviços em manual: {0}", string.Join(", ", app.Services.Select(s => s.Name)))
                                     : Loc.F("Serviços ({0}) ficam como estão: exigem administrador", app.Services.Count)));
        if (app.Tasks.Count > 0) lines.Add("• " + Loc.F("Tarefas desativadas: {0}", string.Join(", ", app.Tasks.Select(t => t.Name))));
        if (app.Background.Count > 0)
            lines.Add("• " + Loc.F("Fechados agora: {0} (usavam {1} de RAM)", string.Join(", ", app.Background.Select(p => p.Name)), Units.Bytes(app.RamMb * 1048576)));
        string text = Loc.F("Pôr {0} para dormir?", app.Name) + "\n\n" + string.Join("\n", lines) + "\n\n"
            + (app.Warn is not null ? Loc.T(app.Warn) + "\n\n" : "")
            + Loc.T("O que roda em segundo plano é encerrado à força: trabalho não salvo nele se perde. Nada é apagado, e “Acordar” devolve tudo.");
        if (_main.Simulate(Loc.T("Pôr programa para dormir"), Loc.F("Poria {0} para dormir.", app.Name))) return;
        if (AppPaths.Demo) return;
        if (!Dialogs.Confirm(this, Loc.T("Modo soneca"), text, Loc.T("Pôr para dormir"))) return;

        _busy = true;
        Msg.Text = Loc.T("Pondo para dormir…");
        try
        {
            bool wakeOnOpen = WakeOnOpen.IsChecked == true;
            var (record, result) = await Task.Run(() => SnoozeEngine.Sleep(app, _system, killBackground: true, wakeOnOpen, DateTime.Now));
            if (record.Items.Any(i => i.Applied))
            {
                Host.Snooze.Save(record);
                string detail = Loc.F("Liberou cerca de {0} de RAM em segundo plano.", Units.Bytes(record.RamMb * 1048576))
                    + (result.NeedsAdmin ? " " + Loc.T("Alguns serviços ficaram como estavam: exigem administrador.") : "");
                ActionLog.Add(Loc.F("{0} em soneca", app.Name), detail, "snooze", app.Key);
                Msg.Text = detail;
            }
            else
                Msg.Text = result.NeedsAdmin ? Loc.T("Nada foi desligado: o que sobrou exige administrador.") : Loc.T("Não consegui desligar nada.");
        }
        finally { _busy = false; }
        await ReloadAsync();
    }

    // ---------- acordar ----------

    private async void Wake_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Current is not { Record: { } record }) { Msg.Text = Loc.T("Selecione um programa em soneca."); return; }
        if (_main.Simulate(Loc.T("Acordar programa"), Loc.F("Acordaria {0}.", record.Name))) return;
        _busy = true;
        try
        {
            var result = await Task.Run(() => SnoozeEngine.Wake(record, _system));
            if (SnoozeEngine.FullyAwake(record))
            {
                Host.Snooze.Remove(record.Key);
                ActionLog.Add(Loc.F("{0} acordou", record.Name), Loc.T("Inicialização, serviços e tarefas de volta."));
                Msg.Text = Loc.F("{0} acordou.", record.Name);
            }
            else
            {
                Host.Snooze.Save(record);
                Msg.Text = result.NeedsAdmin ? Loc.T("Algumas peças exigem administrador: abra o app como administrador e acorde de novo.") : Loc.T("Algumas peças não voltaram. Tente de novo.");
            }
        }
        finally { _busy = false; }
        await ReloadAsync();
    }

    // ---------- nunca adormecer ----------

    private async void Never_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) { Msg.Text = Loc.T("Selecione um programa na lista."); return; }
        var s = Host.Settings;
        string key = row.Key;
        if (row.Asleep) { Msg.Text = Loc.T("Acorde o programa antes."); return; }
        if (s.SnoozeNever.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            s.SnoozeNever.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            Msg.Text = Loc.F("{0} pode voltar a dormir.", row.Name);
        }
        else if (row.Blocked) { Msg.Text = Loc.T("Este programa já nunca dorme."); return; }
        else
        {
            s.SnoozeNever.Add(key);
            Msg.Text = Loc.F("{0} nunca vai dormir.", row.Name);
        }
        s.Save();
        await ReloadAsync();
    }
}
