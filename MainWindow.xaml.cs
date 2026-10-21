using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed record ProcRow(string Name, int Count, double CpuPct, long MemMB, double IoKBs, int Conns, string About, string SafetyText,
                             System.Windows.Media.ImageSource? Icon = null)
{
    public bool HasIcon => Icon is not null;
}

public partial class MainWindow : Window
{
    private static App Host => App.Current;

    private const int TabProcs = 1, TabClean = 2, TabSpace = 3, TabStartup = 4, TabHistory = 5, TabActions = 6, TabTweaks = 7, TabSettings = 8;

    private List<StartupEntry> _startup = new();

    public MainWindow()
    {
        InitializeComponent();
        ApplyCellTextStyle();
        FooterText.Text = AppInfo.Copyright;
        AboutText.Text = $"Notebook Doctor {AppInfo.Version}\nSuporte: klivfy.com/suporte";
        RefreshAdminCard();
        LoadSettingsIntoForm();
        RefreshProfileCard();
        Host.SnapshotReady += OnSnapshot;
        Host.HealthUpdated += () => { RefreshHealthStrip(); if (Host.Latest is not null) OnSnapshot(Host.Latest); };
        Host.HeavyAlertChanged += RefreshHeavyBanner;
        Host.MiniChanged += on => CfgMini.IsChecked = on;
        RefreshStartup();
        Tabs.SelectedIndex = Math.Clamp(Host.Settings.LastTab, 0, Tabs.Items.Count - 1);
        SeedScorePulse();
        SimChip.Visibility = Host.Settings.SimulationMode ? Visibility.Visible : Visibility.Collapsed;
        // O "Tamanho do texto" do Windows pode mudar com o app escondido na bandeja
        IsVisibleChanged += (_, _) => { if (IsVisible) TextScale.Apply(); };
        _snackTimer.Tick += (_, _) => HideSnack();

        // Atalhos: Ctrl+1…8 trocam de aba; F5 atualiza a aba atual
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D9) { Tabs.SelectedIndex = e.Key - Key.D1; e.Handled = true; }
            else if (e.Key == Key.F5) { RefreshCurrentTab(); e.Handled = true; }
            else if (e.Key == Key.Escape && Snack.IsVisible) { HideSnack(); e.Handled = true; }
        };
    }

    private void RefreshCurrentTab()
    {
        switch (Tabs.SelectedIndex)
        {
            case 0: _ = Host.RefreshHealthAsync(); break;
            case TabClean: Scan_Click(this, new RoutedEventArgs()); break;
            case TabStartup: RefreshStartup(); break;
            case TabHistory: LoadHistory(); break;
            case TabActions: LoadActions(); break;
            case TabTweaks: LoadTweaks(); break;
            case TabSettings: LoadPlans(); break;
        }
    }
}
