using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Leva 8.3 (V01): recuperar arquivos apagados pelo Windows File Recovery. O app monta o comando, confirma com a pessoa e acompanha.</summary>
public partial class RecoveryWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private List<DriveFacts> _drives = [];
    private CancellationTokenSource? _cts;
    private string? _outputFolder;
    private bool _busy;

    public RecoveryWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        ModeBox.SelectedIndex = 1;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += async (_, _) => await LoadDrivesAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busy) e.Cancel = true;
        base.OnClosing(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Msg.Text = Loc.T("Espere terminar ou clique em Cancelar."); return; }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { _cts?.Cancel(); CancelBtn.IsEnabled = false; Msg.Text = Loc.T("Cancelando…"); }

    private static IEnumerable<string> AppFolders => [AppPaths.Dir, AppContext.BaseDirectory];

    private async Task LoadDrivesAsync()
    {
        _drives = await Task.Run(() => DriveProbe.List(AppFolders));
        SourceBox.Items.Clear();
        foreach (var d in _drives.Where(d => d.Ready && d.Kind is DriveKind.Fixed or DriveKind.Removable))
        {
            string name = d.Label.Length > 0 ? $" {d.Label}" : "";
            string kind = d.IsSystem ? Loc.T("Windows") : d.Kind == DriveKind.Removable ? Loc.T("removível") : Loc.T("fixa");
            SourceBox.Items.Add(new ComboBoxItem { Tag = d.Letter, Content = $"{d.Letter}:{name} · {kind} · {d.FileSystem} · {Units.Bytes(d.TotalBytes)}" });
        }
        // Começa pela primeira unidade que não é a do Windows: é quase sempre o pen drive que se quer salvar
        SourceBox.SelectedItem = SourceBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => _drives.Any(d => d.Letter == (string?)i.Tag && !d.IsSystem))
                                 ?? SourceBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private DriveFacts? Source => (SourceBox.SelectedItem as ComboBoxItem)?.Tag is string l ? _drives.FirstOrDefault(d => d.Letter == l) : null;
    private RecoveryMode Mode => Enum.TryParse((ModeBox.SelectedItem as ComboBoxItem)?.Tag as string, out RecoveryMode m) ? m : RecoveryMode.Extensive;

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Source is not { } d) return;
        ModeBox.SelectedIndex = (int)RecoveryRules.Suggest(d.FileSystem);
        // Destino padrão: uma pasta em outra unidade, se já não há uma escolhida em outra
        bool destOk = DestBox.Text.Length > 2 && char.ToUpperInvariant(DestBox.Text[0]) != char.ToUpperInvariant(d.Letter[0]);
        if (!destOk)
        {
            var other = _drives.FirstOrDefault(x => x.Ready && x.Kind == DriveKind.Fixed && x.Letter != d.Letter && x.FreeBytes > 1L << 30);
            DestBox.Text = other is null ? "" : $@"{other.Letter}:\Recuperados";
        }
        RefreshWarnings();
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e) => RefreshWarnings();

    private void RefreshWarnings()
    {
        if (Source is not { } d) { WarnInfo.Text = ""; return; }
        WarnInfo.Text = string.Join("\n", RecoveryRules.Warnings(d.IsSystem, d.SolidState, Mode).Select(w => "• " + Loc.T(w)));
    }

    private void PickDest_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Pasta onde guardar o que for recuperado") };
        if (dlg.ShowDialog(this) == true) DestBox.Text = dlg.FolderName;
    }

    private RecoveryRequest BuildRequest()
    {
        var types = new List<RecoveryType>();
        if (TypeDocs.IsChecked == true) types.Add(RecoveryType.Documents);
        if (TypePhotos.IsChecked == true) types.Add(RecoveryType.Photos);
        if (TypeVideos.IsChecked == true) types.Add(RecoveryType.Videos);
        if (TypeAudio.IsChecked == true) types.Add(RecoveryType.Audio);
        if (TypeArchives.IsChecked == true) types.Add(RecoveryType.Archives);
        var filters = FiltersBox.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return new RecoveryRequest(Source?.Letter ?? "", DestBox.Text.Trim(), Mode, types, filters);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RunBtn.IsEnabled = !busy;
        CancelBtn.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.IsEnabled = true;
        Bar.Visibility = LastLine.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Bar.IsIndeterminate = busy;
        if (busy) OpenBtn.Visibility = Visibility.Collapsed;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Source is not { } src) { Msg.Text = Loc.T("Escolha uma unidade."); return; }
        var request = BuildRequest();
        var errors = RecoveryRules.Validate(request, src.FileSystem);
        if (errors.Count > 0) { Msg.Text = Loc.T(errors[0]); return; }

        if (!await FileRecoveryRunner.AvailableAsync())
        {
            if (Dialogs.Confirm(this, Loc.T("Windows File Recovery não encontrado"),
                    Loc.T("A recuperação usa o Windows File Recovery (winfr), gratuito e da Microsoft. Ele não está instalado neste computador. Abrir a página dele na Microsoft Store?"), Loc.T("Abrir a Store")))
            {
                try { Process.Start(new ProcessStartInfo(FileRecoveryRunner.StoreUri) { UseShellExecute = true }); }
                catch (Win32Exception) { Msg.Text = Loc.T("Não consegui abrir a Microsoft Store."); }
            }
            return;
        }
        if (!AppInfo.IsAdmin)
        {
            if (Dialogs.Confirm(this, Loc.T("Precisa de administrador"), Loc.T("Recuperar arquivos exige administrador. Reabrir o app como administrador?"), Loc.T("Reabrir"))) Host.RestartAsAdmin();
            return;
        }
        if (_main.Simulate(Loc.T("Recuperar arquivos"), Loc.F("Procuraria arquivos apagados em {0}: e guardaria em {1}.", src.Letter, request.DestFolder))) return;

        string[] args;
        try { args = RecoveryRules.Args(request, src.FileSystem); }
        catch (InvalidOperationException ex) { Msg.Text = ex.Message; return; }

        string what = request.Types.Count > 0 ? string.Join(", ", request.Types.Select(TypeName)) : Loc.T("conforme os filtros");
        string text = Loc.F("Procurar arquivos apagados em {0}: e guardar em {1}?", src.Letter, request.DestFolder) + "\n\n"
            + Loc.F("Como: {0}. O que: {1}.", ModeName(request.Mode), what) + "\n\n"
            + string.Join("\n", RecoveryRules.Warnings(src.IsSystem, src.SolidState, request.Mode).Select(w => "• " + Loc.T(w)))
            + "\n\n" + Loc.T("O Windows File Recovery só lê a unidade de origem; ele grava apenas na pasta de destino.");
        if (!Dialogs.Confirm(this, Loc.T("Recuperar arquivos"), text, Loc.T("Começar"))) return;

        try { Directory.CreateDirectory(request.DestFolder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Msg.Text = Loc.F("Não consegui criar a pasta de destino: {0}", ex.Message); return; }

        DateTime started = DateTime.Now;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        Msg.Text = Loc.T("Procurando… isso pode levar bastante tempo. Não use a unidade de origem enquanto isso.");
        var percent = new Progress<int>(p => { Bar.IsIndeterminate = false; Bar.Value = p; });
        var lines = new Progress<string>(l => LastLine.Text = l);
        RecoveryRun run;
        try { run = await FileRecoveryRunner.RunAsync("winfr", args, percent, lines, _cts.Token); }
        catch (Win32Exception ex) { Msg.Text = ex.Message; SetBusy(false); return; }
        finally { _cts?.Dispose(); _cts = null; }
        SetBusy(false);

        // O que o winfr gravou nesta rodada
        string? folder = null;
        int files = 0;
        long bytes = 0;
        try
        {
            var names = Directory.GetDirectories(request.DestFolder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();
            if (RecoveryRules.FindOutput(names, started) is { } found)
            {
                folder = Path.Combine(request.DestFolder, found);
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) { files++; try { bytes += new FileInfo(f).Length; } catch (IOException) { } }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _outputFolder = folder;

        string summary = run.Cancelled ? Loc.T("Cancelado. O que já foi gravado fica na pasta de destino.")
            : run.ExitCode != 0 ? Loc.F("O winfr terminou com o código {0}.", run.ExitCode)
            : files > 0 ? Loc.F("Pronto: {0} ({1}) recuperados.", Pt.N(files, Loc.T("arquivo"), Loc.T("arquivos")), Units.Bytes(bytes))
            : Loc.T("Terminou, mas não achou arquivos. Tente o modo profundo ou o por assinatura.");
        Msg.Text = summary;
        ActionLog.Add(Loc.F("Recuperação de arquivos em {0}:", src.Letter), summary);
        if (run.ExitCode != 0 && !run.Cancelled && run.LastLines.Length > 0) Dialogs.ShowText(this, Loc.T("Resposta do winfr"), run.LastLines);
        OpenBtn.Visibility = folder is not null ? Visibility.Visible : Visibility.Collapsed;
        if (folder is not null) AppInfo.OpenShell(folder);
        System.Media.SystemSounds.Asterisk.Play();
    }

    private void Engine_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Msg.Text = Loc.T("Espere terminar ou clique em Cancelar."); return; }
        new NtfsRecoveryWindow(_main).ShowDialog();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_outputFolder is { } f && Directory.Exists(f)) AppInfo.OpenShell(f);
    }

    private static string TypeName(RecoveryType t) => t switch
    {
        RecoveryType.Documents => Loc.T("documentos"), RecoveryType.Photos => Loc.T("fotos"), RecoveryType.Videos => Loc.T("vídeos"),
        RecoveryType.Audio => Loc.T("áudios"), _ => Loc.T("compactados"),
    };

    private static string ModeName(RecoveryMode m) => m switch
    {
        RecoveryMode.Regular => Loc.T("rápido"), RecoveryMode.Extensive => Loc.T("profundo"), _ => Loc.T("por assinatura"),
    };
}
