using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class RegRow : CheckRow
{
    public RegIssue Issue { get; }
    public bool CanFix { get; }

    public RegRow(RegIssue issue)
    {
        Issue = issue;
        CanFix = !issue.Machine || AppInfo.IsAdmin;
        Selected = issue.DefaultOn && CanFix;
    }

    public string Category => Loc.T(RegistryChecker.Categoria(Issue.Kind));
    public string Label => Issue.Label;
    public string Evidence => Loc.T(Issue.Evidence.Split(':')[0]) + (Issue.Evidence.Contains(':') ? ":" + Issue.Evidence[(Issue.Evidence.IndexOf(':') + 1)..] : "")
                              + (CanFix ? "" : " · " + Loc.T("exige administrador"));
    public string Scope => Issue.Machine ? Loc.T("Todos") : Loc.T("Usuário");
}

/// <summary>Leva 7.4 (C04): verificador de registro restrito. Só apaga o que o usuário marcar e o que ainda tiver prova na hora.</summary>
public partial class RegistryCheckWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private readonly ObservableCollection<RegRow> _rows = [];
    private bool _busy, _scanned;

    public bool RemovedAny { get; private set; }

    public RegistryCheckWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        Grid.ItemsSource = _rows;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) => UpdateInfo();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Msg.Text = Loc.T("Espere terminar o que está em andamento."); return; }
        Close();
    }

    private void All_Click(object sender, RoutedEventArgs e) { foreach (var r in _rows.Where(r => r.CanFix)) r.Selected = true; UpdateInfo(); }
    private void None_Click(object sender, RoutedEventArgs e) { foreach (var r in _rows) r.Selected = false; UpdateInfo(); }

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource is CheckBox || Grid.SelectedItem is not RegRow { CanFix: true } row) return;
        row.Selected = !row.Selected;
        e.Handled = true;
    }

    private void UpdateInfo()
    {
        if (!_scanned) { Info.Text = Loc.T("Clique em “Verificar agora”."); return; }
        Info.Text = _rows.Count == 0
            ? Loc.T("Nenhuma entrada quebrada com prova. Está tudo em ordem.")
            : Loc.F("{0} entradas com prova de estarem quebradas · {1} marcadas.", _rows.Count, _rows.Count(r => r.Selected));
    }

    private static string FontsDir => System.IO.Path.Combine(ServiceManager.SystemRoot, "Fonts");

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ScanBtn.IsEnabled = FixBtn.IsEnabled = false;
        Msg.Text = Loc.T("Verificando o registro…");
        try
        {
            List<RegIssue> issues;
            if (AppPaths.Demo)
            {
                issues =
                [
                    new(RegIssueKind.Uninstall, @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\ProgramaSumido", null, "Programa Sumido 2.1", @"O desinstalador não existe: C:\Program Files\Sumido\unins000.exe", true),
                    new(RegIssueKind.Run, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "Atualizador", "Atualizador", @"O programa não existe: C:\Users\a\AppData\Local\Velho\upd.exe", true),
                    new(RegIssueKind.SharedDll, @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\SharedDLLs", @"C:\Program Files\Velho\lib.dll", "lib.dll", @"O arquivo não existe: C:\Program Files\Velho\lib.dll", true),
                    new(RegIssueKind.ProgId, @"HKEY_CURRENT_USER\Software\Classes\Velho.Documento", null, "Velho.Documento", @"O programa que abre este tipo não existe: C:\Program Files\Velho\velho.exe", false),
                ];
            }
            else
            {
                var sys = new WindowsRegistryCheckSystem();
                string root = ServiceManager.SystemRoot, fonts = FontsDir;
                issues = await Task.Run(() => RegistryChecker.Scan(sys, root, fonts));
            }
            _rows.Clear();
            foreach (var i in issues)
            {
                var row = new RegRow(i);
                row.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(CheckRow.Selected)) UpdateInfo(); };
                _rows.Add(row);
            }
            _scanned = true;
            Msg.Text = "";
            UpdateInfo();
        }
        finally { _busy = false; ScanBtn.IsEnabled = FixBtn.IsEnabled = true; }
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_busy) return;
        var marked = _rows.Where(r => r.Selected && r.CanFix).ToList();
        if (marked.Count == 0) { Msg.Text = Loc.T("Nada selecionado."); return; }
        if (_main.Simulate(Loc.T("Limpar o registro"), Loc.F("Apagaria {0} do registro, depois de guardar um backup.", Pt.N(marked.Count, Loc.T("entrada"), Loc.T("entradas"))))) return;
        if (AppPaths.Demo) return;

        string text = Loc.F("Apagar {0} do registro?", Pt.N(marked.Count, Loc.T("entrada"), Loc.T("entradas"))) + "\n\n"
            + Loc.T("Cada uma é conferida de novo na hora, e só sai se o arquivo que ela cita continuar não existindo. Tudo vai para um backup antes: dá para restaurar em “Backups…” e em Ações.");
        if (marked.Any(r => r.Issue.Kind == RegIssueKind.ProgId))
            text += "\n\n" + Loc.T("Há tipos de arquivo sem programa marcados: confira se não são de um programa que você só desinstalou por engano.");
        if (!Dialogs.Confirm(this, Loc.T("Limpar o registro"), text, Loc.T("Apagar"), danger: true)) return;

        _busy = true;
        ScanBtn.IsEnabled = FixBtn.IsEnabled = false;
        Msg.Text = Loc.T("Guardando o backup e apagando…");
        try
        {
            var sys = new WindowsRegistryCheckSystem();
            var store = new BackupStore(AppPaths.Backups);
            string root = ServiceManager.SystemRoot, fonts = FontsDir;
            var issues = marked.Select(r => r.Issue).ToList();
            var result = await Task.Run(() => RegistryFixer.Fix(issues, sys, store, root, fonts));
            string summary = Loc.F("{0} apagadas.", Pt.N(result.Removed, Loc.T("entrada"), Loc.T("entradas")));
            if (result.Skipped > 0) summary += " " + Loc.F("{0} puladas (mudaram desde a verificação).", result.Skipped);
            if (result.Failed > 0) summary += " " + Loc.F("{0} falharam: algumas exigem administrador.", result.Failed);
            if (result.SessionId is { } id)
            {
                RemovedAny = true;
                ActionLog.Add(Loc.F("Registro limpo: {0}", Pt.N(result.Removed, Loc.T("entrada"), Loc.T("entradas"))), summary, "leftovers", id);
            }
            Msg.Text = summary;
            System.Media.SystemSounds.Asterisk.Play();
        }
        finally { _busy = false; ScanBtn.IsEnabled = FixBtn.IsEnabled = true; }
        Scan_Click(this, new RoutedEventArgs());
    }

    private void Backups_Click(object sender, RoutedEventArgs e) => Dialogs.ShowBackups(this, new BackupStore(AppPaths.Backups));
}
