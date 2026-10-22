using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class MenuRow : CheckRow
{
    private string? _publisher;
    private bool _loading;

    /// <summary>Item que está no registro agora (verbo ou manipulador). Null quando a linha é um manipulador desativado.</summary>
    public ContextMenuItem? Item { get; }
    /// <summary>Manipulador desativado por este app: a chave está no backup.</summary>
    public DisabledHandler? Off { get; }
    public bool Protected { get; }
    public string Reason { get; }

    public MenuRow(ContextMenuItem item, bool isProtected, string reason) { Item = item; Protected = isProtected; Reason = reason; }
    public MenuRow(DisabledHandler off) { Off = off; Reason = ""; }

    public string Display => Item?.Display ?? Off!.Display;
    public string Where => Loc.T(ContextMenuRules.LocationLabel(Item?.Location ?? Off!.Location));
    public string KindText => (Item?.Kind ?? MenuItemKind.Handler) == MenuItemKind.Handler ? Loc.T("Manipulador") : Loc.T("Verbo");
    public string FileText => Item?.File ?? Off!.File;
    public string Origin => (Item?.Hive ?? Off!.Hive) == "HKEY_LOCAL_MACHINE" ? Loc.T("Todos") : Loc.T("Usuário");
    public bool IsOff => Off is not null || Item is { Hidden: true };

    public string StateText =>
        Off is not null ? Loc.T("Desativado")
        : Item!.Hidden ? Loc.T("Escondido")
        : Protected ? Loc.T(Reason)
        : Item.File.Length > 0 && !Item.FileExists ? Loc.T("Arquivo sumiu")
        : Loc.T("Ativo");

    /// <summary>Quem assinou o arquivo; lido em segundo plano na primeira vez que a linha aparece.</summary>
    public string Publisher
    {
        get
        {
            if (_publisher is not null) return _publisher;
            if (_loading) return "";
            _loading = true;
            string file = FileText;
            if (file.Length == 0) { _publisher = ""; return ""; }
            Task.Run(() => { _publisher = Loc.T(FilePublisher.Describe(file)); Raise(nameof(Publisher)); });
            return "";
        }
    }
}

/// <summary>Leva 7.3 (C06): gerenciador do menu de contexto do Explorador.</summary>
public partial class ContextMenuWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private readonly ObservableCollection<MenuRow> _rows = [];

    public ContextMenuWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        Grid.ItemsSource = _rows;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) => Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();
    private void ShowWindows_Click(object sender, RoutedEventArgs e) => Reload();

    private MenuRow? Current => Grid.SelectedItem as MenuRow;

    private void Reload()
    {
        _rows.Clear();
        string root = ContextMenuEditor.SystemRoot;
        List<ContextMenuItem> items;
        if (AppPaths.Demo) items = DemoItems();
        else
        {
            try { items = ContextMenuEditor.Scan(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { items = []; }
        }
        int windows = 0;
        foreach (var item in ContextMenuRules.Order(items, root))
        {
            var (prot, reason) = ContextMenuRules.Protection(item, root);
            if (prot && !item.Hidden && ShowWindows.IsChecked != true) { windows++; continue; }
            _rows.Add(new MenuRow(item, prot, reason));
        }
        if (!AppPaths.Demo)
            foreach (var off in ContextMenuEditor.Disabled(AppPaths.Dir)) _rows.Add(new MenuRow(off));
        Info.Text = Loc.F("{0} itens · {1} desativados.", _rows.Count, _rows.Count(r => r.IsOff))
                    + (windows > 0 ? " " + Loc.F("{0} do Windows escondidos da lista.", windows) : "");
    }

    private static List<ContextMenuItem> DemoItems() =>
    [
        new(MenuItemKind.Handler, "HKEY_LOCAL_MACHINE", "*", "7-Zip", "7-Zip", "{23170F69-40C1-278A-1000-000100020000}", @"C:\Program Files\7-Zip\7-zip.dll", true, "", false),
        new(MenuItemKind.Handler, "HKEY_LOCAL_MACHINE", "Directory", "WinRAR", "WinRAR", "{B41DB860-8EE4-11D2-9906-E49FADC173CA}", @"C:\Program Files\WinRAR\RarExt.dll", false, "", false),
        new(MenuItemKind.Verb, "HKEY_CURRENT_USER", @"Directory\Background", "git_shell", "Git Bash Here (git_shell)", "", @"C:\Program Files\Git\git-bash.exe", true, "", false),
        new(MenuItemKind.Handler, "HKEY_LOCAL_MACHINE", "AllFilesystemObjects", "Sharing", "Sharing", "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}", @"C:\Windows\System32\ntshrui.dll", true, "", false),
    ];

    // ---------- desativar e reativar ----------

    private bool EnsureAdminFor(bool machine)
    {
        if (!machine || AppInfo.IsAdmin) return true;
        if (Dialogs.Confirm(this, Loc.T("Precisa de administrador"), Loc.T("Este item vale para todos os usuários do PC e exige administrador. Reabrir o app como administrador?"), Loc.T("Reabrir")))
            Host.RestartAsAdmin();
        return false;
    }

    private void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) { Msg.Text = Loc.T("Selecione um item na lista."); return; }
        if (row.Off is not null || row.IsOff) { Msg.Text = Loc.T("Este item já está desativado."); return; }
        if (row.Protected) { Msg.Text = Loc.F("{0}: não pode ser desativado por aqui.", Loc.T(row.Reason)); return; }
        var item = row.Item!;
        bool handler = item.Kind == MenuItemKind.Handler;
        string what = handler
            ? Loc.F("Tirar “{0}” do menu de contexto? A chave do registro é guardada num backup e apagada; “Reativar” a devolve.", item.Display)
            : Loc.F("Esconder “{0}” do menu de contexto? Nada é apagado; “Reativar” devolve o item.", item.Display);
        if (_main.Simulate(Loc.T("Desativar item do menu de contexto"), Loc.F("Desativaria “{0}”.", item.Display))) return;
        if (AppPaths.Demo) return;
        if (!EnsureAdminFor(item.Machine)) return;
        if (!Dialogs.Confirm(this, Loc.T("Desativar item?"), what, Loc.T("Desativar"))) return;
        try
        {
            if (handler)
            {
                var rec = ContextMenuEditor.DisableHandler(item, AppPaths.Dir);
                ActionLog.Add(Loc.F("Item do menu de contexto desativado: {0}", item.Display), $"{row.Where}. {item.File}", "ctxmenu", rec.SessionId);
            }
            else
            {
                ContextMenuEditor.SetVerbHidden(item, true);
                ActionLog.Add(Loc.F("Item do menu de contexto escondido: {0}", item.Display), row.Where, "ctxverb", $"{item.Hive}|{item.Location}|{item.Name}");
            }
            Msg.Text = Loc.T("Pronto. O Explorador já usa o menu novo; se ainda aparecer, abra a janela de novo.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException or ArgumentException)
        {
            Msg.Text = ex is UnauthorizedAccessException ? Loc.T("Sem permissão: abra o app como administrador.") : ex.Message;
        }
        Reload();
    }

    private void Enable_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) { Msg.Text = Loc.T("Selecione um item na lista."); return; }
        if (!row.IsOff) { Msg.Text = Loc.T("Este item já está ativo."); return; }
        string name = row.Display;
        if (_main.Simulate(Loc.T("Reativar item do menu de contexto"), Loc.F("Reativaria “{0}”.", name))) return;
        if (AppPaths.Demo) return;
        if (!EnsureAdminFor((row.Item?.Hive ?? row.Off!.Hive) == "HKEY_LOCAL_MACHINE")) return;
        try
        {
            if (row.Off is { } off)
            {
                string msg = ContextMenuEditor.EnableHandler(off, AppPaths.Dir, out bool restored);
                if (!restored) { Msg.Text = Loc.T("Não consegui devolver a chave. Tente como administrador.") + " " + msg; return; }
                ActionLog.Add(Loc.F("Item do menu de contexto reativado: {0}", name), row.Where);
            }
            else
            {
                ContextMenuEditor.SetVerbHidden(row.Item!, false);
                ActionLog.Add(Loc.F("Item do menu de contexto reativado: {0}", name), row.Where);
            }
            Msg.Text = Loc.T("Pronto.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException or ArgumentException)
        {
            Msg.Text = ex is UnauthorizedAccessException ? Loc.T("Sem permissão: abra o app como administrador.") : ex.Message;
        }
        Reload();
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || row.FileText.Length == 0) { Msg.Text = Loc.T("Este item não tem arquivo para mostrar."); return; }
        if (!File.Exists(row.FileText)) { Msg.Text = Loc.T("O arquivo não existe mais."); return; }
        try { Process.Start("explorer.exe", $"/select,\"{row.FileText}\""); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { Msg.Text = ex.Message; }
    }
}
