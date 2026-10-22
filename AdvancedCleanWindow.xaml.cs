using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Uma linha com caixa de seleção que avisa a tela quando muda.</summary>
public abstract class CheckRow : INotifyPropertyChanged
{
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class RuleRow : CheckRow
{
    public CleanPlanEntry Entry { get; }
    public bool CanClean { get; }

    public RuleRow(CleanPlanEntry entry)
    {
        Entry = entry;
        CanClean = !entry.NeedsAdmin || AppInfo.IsAdmin;
        Selected = entry.Selected && CanClean;
    }

    public string Name => Loc.T(Entry.Name);

    public string CategoryText => Entry.Category switch
    {
        CleanCategory.Windows => Loc.T("Windows"),
        CleanCategory.Office => Loc.T("Office"),
        CleanCategory.Internet => Loc.T("Internet"),
        CleanCategory.Junk => Loc.T("Arquivos inúteis"),
        _ => Loc.T("Programas"),
    };

    public string ItemsText
    {
        get
        {
            var parts = new List<string>();
            if (Entry.FileCount > 0) parts.Add(Pt.N(Entry.FileCount, Loc.T("arquivo"), Loc.T("arquivos")));
            if (Entry.RegCount > 0) parts.Add(Pt.N(Entry.RegCount, Loc.T("chave"), Loc.T("chaves")));
            if (parts.Count == 0 && Entry.Rule.Action != CleanAction.None) parts.Add(Loc.T("ação"));
            return string.Join(" · ", parts);
        }
    }

    public string SizeText => Entry.Bytes > 0 ? Units.Bytes(Entry.Bytes) : "";

    public string Note
    {
        get
        {
            var parts = new List<string>();
            if (Entry.Rule.Warning.Length > 0) parts.Add(Loc.T(Entry.Rule.Warning));
            if (!CanClean) parts.Add(Loc.T("Exige administrador."));
            if (Entry.Blocked > 0) parts.Add(Loc.F("{0} barrados pelas proteções.", Entry.Blocked));
            return string.Join(" ", parts);
        }
    }
}

public sealed class JunkRow : CheckRow
{
    public JunkItem Item { get; }
    public JunkRow(JunkItem item) { Item = item; Selected = item.Selected; }
    public string Path => Item.Path;
    public string KindText => Item.KindText;
    public string SizeText => Item.SizeText;
    public string ModifiedText => Item.Modified.ToString("d", Loc.Culture);
}

public sealed class DiskRow : CheckRow
{
    public DiskModule Module { get; }
    public DiskRow(DiskModule module) => Module = module;
    public string Label => Loc.T(Module.Label);
    public string RiskText => Module.Risk == ModuleRisk.Safe ? Loc.T("Seguro") : Loc.T("Com cuidado");
    public string Warning => Module.Warning.Length > 0 ? Loc.T(Module.Warning) : "";
}

/// <summary>
/// Leva 6.1: limpeza avançada. Três seções: rastros e programas (motor de regras), arquivos inúteis e Limpeza de Disco do
/// Windows. Tudo é analisado antes, o registro vai para um backup que dá para desfazer em Ações, e nada começa marcado
/// fora do que é seguro.
/// </summary>
public partial class AdvancedCleanWindow : Window
{
    private readonly MainWindow _main;
    private static App Host => App.Current;
    private readonly ObservableCollection<RuleRow> _rules = [];
    private readonly ObservableCollection<JunkRow> _junk = [];
    private readonly ObservableCollection<DiskRow> _disk = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly KnownPlaces _places = WindowsPlaces.Current(AppPaths.Dir);
    private bool _busy;

    private static string CommunityFile => Path.Combine(AppPaths.Dir, "winapp2.ini");

    public AdvancedCleanWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        RulesGrid.ItemsSource = _rules;
        JunkGrid.ItemsSource = _junk;
        DiskGrid.ItemsSource = _disk;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Closed += (_, _) => _cts.Cancel();
        Loaded += (_, _) =>
        {
            ShowSection(0);
            ShowExcluded();
            RulesInfo.Text = Loc.T("Clique em Analisar para ver o que pode ser limpo.");
            JunkInfo.Text = Loc.T("Clique em Procurar para achar arquivos inúteis.");
            _ = LoadDiskAsync();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Section_Click(object sender, RoutedEventArgs e) => ShowSection(int.Parse((string)((Button)sender).Tag));

    private void ShowSection(int i)
    {
        Sections.SelectedIndex = i;
        void Mark(Button b, bool on) => b.SetResourceReference(StyleProperty, on ? "TonalButton" : "TextButton");
        Mark(TabTraces, i == 0);
        Mark(TabJunk, i == 1);
        Mark(TabDisk, i == 2);
    }

    /// <summary>Uma operação por vez: os botões não disparam outra análise ou limpeza no meio de uma.</summary>
    private bool Begin(string message)
    {
        if (_busy) { Msg.Text = Loc.T("Aguarde o que já está em andamento."); return false; }
        _busy = true;
        Msg.Text = message;
        return true;
    }

    private void End(string message = "")
    {
        _busy = false;
        Msg.Text = message;
    }

    // ---------- Rastros e programas ----------
    private List<CleanRule> LoadRules(out int community)
    {
        var rules = new List<CleanRule>(BuiltInRules.All());
        community = 0;
        try
        {
            if (File.Exists(CommunityFile) && new FileInfo(CommunityFile).Length < 20 << 20)
            {
                var imported = Winapp2Parser.Parse(File.ReadAllText(CommunityFile));
                community = imported.Count;
                rules.AddRange(imported);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // arquivo de regras ilegível: só as nossas valem
        return rules;
    }

    private async void RulesScan_Click(object sender, RoutedEventArgs e) => await ScanRulesAsync();

    private async Task ScanRulesAsync()
    {
        if (!Begin(Loc.T("Analisando…"))) return;
        try
        {
            var rules = LoadRules(out int community);
            var places = _places;
            var plan = await Task.Run(() =>
            {
                var sys = new WindowsCleanSystem();
                var engine = new CleanEngine(sys, new CleanContext(places, EnvExpander.FromPlaces(places), Environment.OSVersion.Version));
                return engine.PlanAll(rules, _cts.Token);
            });
            _rules.Clear();
            foreach (var entry in plan)
            {
                var row = new RuleRow(entry);
                row.PropertyChanged += (_, _) => UpdateRulesInfo();
                _rules.Add(row);
            }
            RulesRemoveBtn.IsEnabled = community > 0;
            UpdateRulesInfo(community);
        }
        catch (OperationCanceledException) { }
        finally { End(); }
    }

    private int _community;

    private void UpdateRulesInfo(int? community = null)
    {
        if (community is { } c) _community = c;
        long marked = _rules.Where(r => r.Selected).Sum(r => r.Entry.Bytes);
        string text = Loc.F("{0} marcados para limpeza · {1} no total. {2} com algo para limpar.",
            Units.Bytes(marked), Units.Bytes(_rules.Sum(r => r.Entry.Bytes)), Pt.N(_rules.Count, Loc.T("regra"), Loc.T("regras")));
        if (_community > 0)
            text += " " + Loc.F("Regras importadas (formato winapp2.ini): {0}. Elas aparecem desmarcadas e passam pelas mesmas proteções.", _community);
        RulesInfo.Text = text;
    }

    private IEnumerable<RuleRow> RuleRows() => _rules;

    private void RulesAll_Click(object sender, RoutedEventArgs e)
    {
        // Só o que é Windows e Office (os nossos): as regras importadas pedem escolha uma a uma
        foreach (var r in _rules) r.Selected = r.CanClean && r.Entry.Rule.Origin == RuleOrigin.Builtin;
    }

    private void RulesNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _rules) r.Selected = false;
    }

    private void RulesGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource is CheckBox || RulesGrid.SelectedItem is not RuleRow { CanClean: true } row) return;
        row.Selected = !row.Selected;
        e.Handled = true;
    }

    private async void RulesDetails_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not RuleRow row) { Msg.Text = Loc.T("Selecione uma linha para ver o que sai."); return; }
        var entry = row.Entry;
        Msg.Text = Loc.T("Listando…");
        string text = await Task.Run(() => Describe(entry));
        Msg.Text = "";
        Dialogs.ShowText(this, Loc.F("O que seria apagado: {0}", row.Name), text);
    }

    private static string Describe(CleanPlanEntry entry)
    {
        var sys = new WindowsCleanSystem();
        var files = new List<PreviewFile>();
        foreach (var t in entry.Files)
            foreach (var f in t.Recurse ? sys.Files(t.Dir) : sys.DirectFiles(t.Dir))
                if (CleanEngine.Matches(t, f, () => sys.LastWrite(f)))
                    files.Add(new PreviewFile(f, sys.Size(f), sys.LastWrite(f)));
        var sb = new System.Text.StringBuilder();
        string name = Loc.T(entry.Name);
        sb.AppendLine(files.Count > 0 || entry.Rule.Action == CleanAction.None ? CleanPreview.Format(name, entry.Files.Select(t => t.Dir).ToList(), files) : name);
        if (entry.Registry.Count > 0)
        {
            sb.AppendLine().AppendLine(Loc.T("Registro (vai para um backup que dá para desfazer em Ações):"));
            foreach (var r in entry.Registry.Take(100))
                sb.AppendLine("• " + (r.ValueName is null ? r.Key : $"{r.Key} › {r.ValueName}"));
            if (entry.Registry.Count > 100) sb.AppendLine(Loc.F("… e mais {0}.", entry.Registry.Count - 100));
        }
        if (entry.Rule.Action == CleanAction.FlushDns) sb.AppendLine().AppendLine(Loc.T("Esvazia o cache de DNS (ipconfig /flushdns)."));
        if (entry.Rule.Action == CleanAction.ClearClipboard) sb.AppendLine().AppendLine(Loc.T("Esvazia a área de transferência."));
        if (entry.Rule.Warning.Length > 0) sb.AppendLine().AppendLine(Loc.T(entry.Rule.Warning));
        return sb.ToString().TrimEnd();
    }

    private async void RulesClean_Click(object sender, RoutedEventArgs e)
    {
        RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var chosen = _rules.Where(r => r.Selected && r.CanClean).ToList();
        if (chosen.Count == 0) { Msg.Text = Loc.T("Nada selecionado."); return; }
        string list = string.Join("\n", chosen.Select(r => $"• {r.Name}" + (r.SizeText.Length > 0 ? $" ({r.SizeText})" : "")));
        if (_main.Simulate(Loc.T("Limpar rastros e programas"), Loc.F("Apagaria: {0}.", string.Join("; ", chosen.Select(r => r.Name))))) return;
        bool registry = chosen.Any(r => r.Entry.RegCount > 0);
        string extra = registry ? "\n\n" + Loc.T("As chaves do registro vão para um backup, e dá para desfazer em Ações.") : "";
        if (!Dialogs.Confirm(this, Loc.T("Confirmar limpeza"), Loc.F("Apagar os itens abaixo?\n\n{0}{1}", list, extra), Loc.T("Apagar"))) return;
        if (!Begin(Loc.T("Limpando…"))) return;

        var entries = chosen.Select(r => { r.Entry.Selected = true; return r.Entry; }).ToList();
        var store = new BackupStore(AppPaths.Backups);
        string? session = null;
        RuleCleanResult result;
        try
        {
            var progress = new Progress<string>(name => Msg.Text = Loc.F("Limpando: {0}…", name));
            result = await Task.Run(() =>
            {
                using var backup = store.Begin(Loc.T("Limpeza avançada"));
                session = backup.Id;
                var r = CleanExecutor.Run(entries, _places, backup, AppInfo.IsAdmin, ClearClipboard, progress, _cts.Token);
                return r;
            });
        }
        catch (OperationCanceledException) { End(); return; }
        // Sem registro mexido, o backup ficaria vazio: não vale guardar
        bool kept = result.Keys > 0 && session is not null;
        if (!kept && session is not null) { try { store.Delete(session); } catch (IOException) { } }
        string summary = Loc.F("{0} liberados · {1}, {2}.", Units.Bytes(result.Bytes),
            Pt.N(result.Files, Loc.T("arquivo"), Loc.T("arquivos")), Pt.N(result.Keys, Loc.T("chave do registro"), Loc.T("chaves do registro")));
        if (result.Skipped > 0) summary += " " + Loc.F("{0} pulados (em uso ou sem permissão).", result.Skipped);
        ActionLog.Add(Loc.T("Limpeza avançada"), summary + " " + string.Join(", ", chosen.Select(r => r.Name)), kept ? "leftovers" : "", kept ? session! : "");
        End(summary);
        PlayDone();
        await ScanRulesAsync();
        Msg.Text = summary;
    }

    private bool ClearClipboard(CleanAction action)
    {
        if (action != CleanAction.ClearClipboard) return false;
        return Dispatcher.Invoke(() =>
        {
            try { Clipboard.Clear(); return true; }
            catch (System.Runtime.InteropServices.COMException) { return false; } // outro programa está usando a área de transferência
        });
    }

    private void PlayDone() => System.Media.SystemSounds.Asterisk.Play();

    private async void RulesImport_Click(object sender, RoutedEventArgs e)
    {
        var open = new Microsoft.Win32.OpenFileDialog { Filter = $"{Loc.T("Regras de limpeza")} (*.ini)|*.ini|{Loc.T("Todos os arquivos")}|*.*" };
        if (open.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(open.FileName).Length > 20 << 20) { Msg.Text = Loc.T("O arquivo é grande demais para ser um arquivo de regras."); return; }
            var rules = Winapp2Parser.Parse(File.ReadAllText(open.FileName));
            if (rules.Count == 0) { Msg.Text = Loc.T("O arquivo não tem regras no formato winapp2.ini."); return; }
            File.Copy(open.FileName, CommunityFile, overwrite: true);
            Msg.Text = Loc.F("{0} importadas.", Pt.N(rules.Count, Loc.T("regra"), Loc.T("regras")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Msg.Text = ex.Message; return; }
        await ScanRulesAsync();
    }

    private async void RulesRemoveImported_Click(object sender, RoutedEventArgs e)
    {
        try { if (File.Exists(CommunityFile)) File.Delete(CommunityFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Msg.Text = ex.Message; return; }
        await ScanRulesAsync();
    }

    // ---------- Arquivos inúteis ----------
    private void ShowExcluded()
    {
        var list = Host.Settings.JunkExcluded;
        JunkExcluded.Text = list.Count == 0 ? Loc.T("Nenhuma pasta ignorada.") : Loc.F("Pastas ignoradas: {0}", string.Join("; ", list));
        JunkClearExcludedBtn.IsEnabled = list.Count > 0;
    }

    private void JunkAddExcluded_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Pasta que a busca não deve olhar") };
        if (dlg.ShowDialog(this) != true) return;
        if (!Host.Settings.JunkExcluded.Contains(dlg.FolderName, StringComparer.OrdinalIgnoreCase)) Host.Settings.JunkExcluded.Add(dlg.FolderName);
        Host.Settings.Save();
        ShowExcluded();
    }

    private void JunkClearExcluded_Click(object sender, RoutedEventArgs e)
    {
        Host.Settings.JunkExcluded.Clear();
        Host.Settings.Save();
        ShowExcluded();
    }

    private async void JunkScan_Click(object sender, RoutedEventArgs e) => await ScanJunkAsync();

    private async Task ScanJunkAsync()
    {
        if (!Begin(Loc.T("Procurando…"))) return;
        try
        {
            bool drives = (JunkWhere.SelectedItem as ComboBoxItem)?.Tag as string == "drives";
            var roots = drives
                ? DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList()
                : [_places.UserProfile];
            var excluded = Host.Settings.JunkExcluded.Append(AppPaths.Dir).ToList();
            var places = _places;
            var items = await Task.Run(() => JunkRules.Scan(new WindowsCleanSystem(), places, roots, excluded, DateTime.Now, _cts.Token));
            _junk.Clear();
            foreach (var item in items)
            {
                var row = new JunkRow(item);
                row.PropertyChanged += (_, _) => UpdateJunkInfo();
                _junk.Add(row);
            }
            UpdateJunkInfo();
            if (items.Count >= JunkRules.MaxItems) Msg.Text = Loc.F("Mostrando os primeiros {0:N0} itens. Apague e procure de novo para ver o resto.", JunkRules.MaxItems);
            else Msg.Text = "";
        }
        catch (OperationCanceledException) { }
        finally { _busy = false; }
    }

    private void UpdateJunkInfo() =>
        JunkInfo.Text = _junk.Count == 0
            ? Loc.T("Nenhum arquivo inútil encontrado.")
            : Loc.F("{0} marcados · {1} no total ({2}).", Units.Bytes(_junk.Where(j => j.Selected).Sum(j => j.Item.Bytes)),
                Units.Bytes(_junk.Sum(j => j.Item.Bytes)), Pt.N(_junk.Count, Loc.T("item"), Loc.T("itens")));

    private void JunkAll_Click(object sender, RoutedEventArgs e) { foreach (var j in _junk) j.Selected = true; }
    private void JunkNone_Click(object sender, RoutedEventArgs e) { foreach (var j in _junk) j.Selected = false; }

    private void JunkGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource is CheckBox || JunkGrid.SelectedItem is not JunkRow row) return;
        row.Selected = !row.Selected;
        e.Handled = true;
    }

    private void JunkReveal_Click(object sender, RoutedEventArgs e)
    {
        if (JunkGrid.SelectedItem is not JunkRow row) { Msg.Text = Loc.T("Selecione um item na lista."); return; }
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{row.Path}\""); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Msg.Text = ex.Message; }
    }

    private async void JunkClean_Click(object sender, RoutedEventArgs e)
    {
        JunkGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var chosen = _junk.Where(j => j.Selected).ToList();
        if (chosen.Count == 0) { Msg.Text = Loc.T("Nada selecionado."); return; }
        if (_main.Simulate(Loc.T("Apagar arquivos inúteis"), Loc.F("Apagaria {0}.", Pt.N(chosen.Count, Loc.T("item"), Loc.T("itens"))))) return;
        int toRecycle = chosen.Count(j => j.Item.Kind is JunkKind.Backup or JunkKind.OldFile or JunkKind.LostFolder);
        string extra = toRecycle > 0 ? "\n\n" + Loc.F("{0} irão para a Lixeira (podem ser seus).", Pt.N(toRecycle, Loc.T("item"), Loc.T("itens"))) : "";
        string message = Loc.F("Apagar {0}, {1}?{2}", Pt.N(chosen.Count, Loc.T("item"), Loc.T("itens")), Units.Bytes(chosen.Sum(j => j.Item.Bytes)), extra);
        if (!Dialogs.Confirm(this, Loc.T("Confirmar limpeza"), message, Loc.T("Apagar"))) return;
        if (!Begin(Loc.T("Apagando…"))) return;
        RuleCleanResult result;
        try
        {
            var items = chosen.Select(j => j.Item).ToList();
            var places = _places;
            result = await Task.Run(() => CleanExecutor.DeleteJunk(items, places, _cts.Token));
        }
        catch (OperationCanceledException) { End(); return; }
        string summary = Loc.F("{0} liberados · {1}.", Units.Bytes(result.Bytes), Pt.N(result.Files, Loc.T("item"), Loc.T("itens")));
        if (result.Skipped > 0) summary += " " + Loc.F("{0} pulados (em uso ou sem permissão).", result.Skipped);
        ActionLog.Add(Loc.T("Arquivos inúteis"), summary);
        End();
        PlayDone();
        await ScanJunkAsync();
        Msg.Text = summary;
    }

    // ---------- Limpeza de Disco do Windows ----------
    private async Task LoadDiskAsync()
    {
        DiskRunBtn.IsEnabled = AppInfo.IsAdmin;
        DiskInfo.Text = AppInfo.IsAdmin
            ? ""
            : Loc.T("A Limpeza de Disco do Windows exige administrador: abra o app como administrador (Ajustes › Reabrir como administrador).");
        List<DiskModule> modules;
        try { modules = await Task.Run(DiskCleanupRunner.Available); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { modules = []; }
        _disk.Clear();
        foreach (var m in modules) _disk.Add(new DiskRow(m));
        if (modules.Count == 0) DiskInfo.Text = Loc.T("Este Windows não tem módulos da Limpeza de Disco.");
    }

    private async void DiskReload_Click(object sender, RoutedEventArgs e) => await LoadDiskAsync();

    private void DiskSafe_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _disk) r.Selected = r.Module.Risk == ModuleRisk.Safe;
    }

    private void DiskNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _disk) r.Selected = false;
    }

    private void DiskGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource is CheckBox || DiskGrid.SelectedItem is not DiskRow row) return;
        row.Selected = !row.Selected;
        e.Handled = true;
    }

    private async void DiskRun_Click(object sender, RoutedEventArgs e)
    {
        DiskGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var chosen = _disk.Where(r => r.Selected).ToList();
        if (chosen.Count == 0) { Msg.Text = Loc.T("Nada selecionado."); return; }
        if (_main.Simulate(Loc.T("Limpeza de Disco do Windows"), Loc.F("Rodaria: {0}.", string.Join("; ", chosen.Select(r => r.Label))))) return;
        var care = chosen.Where(r => r.Module.Risk == ModuleRisk.Care).ToList();
        string list = string.Join("\n", chosen.Select(r => "• " + r.Label));
        string warn = care.Count == 0 ? "" : "\n\n" + Loc.T("Com cuidado:") + "\n" + string.Join("\n", care.Select(r => $"• {r.Label}: {r.Warning}"));
        if (!Dialogs.Confirm(this, Loc.T("Confirmar limpeza"), Loc.F("Rodar a Limpeza de Disco do Windows para:\n\n{0}{1}\n\nO Windows faz a limpeza e mostra o andamento; pode demorar.", list, warn),
                Loc.T("Rodar"), danger: care.Count > 0)) return;
        if (!Begin(Loc.T("A Limpeza de Disco do Windows está rodando…"))) return;
        bool ok;
        try
        {
            var keys = chosen.Select(r => r.Module.Key).ToList();
            ok = await Task.Run(() => DiskCleanupRunner.Run(keys, _cts.Token));
        }
        catch (OperationCanceledException) { End(); return; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
        {
            End(ex.Message);
            return;
        }
        ActionLog.Add(Loc.T("Limpeza de Disco do Windows"), string.Join(", ", chosen.Select(r => r.Label)));
        End(ok ? Loc.T("Limpeza de Disco do Windows concluída.") : Loc.T("A Limpeza de Disco do Windows terminou com erro: confira no próprio Windows."));
        PlayDone();
    }
}
