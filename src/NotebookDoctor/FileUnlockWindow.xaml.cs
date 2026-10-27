using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class LockRow(LockingProcess process, LockVerdict verdict)
{
    public LockingProcess Process { get; } = process;
    public LockVerdict Verdict { get; } = verdict;
    public string Name => Process.Name;
    public int Pid => Process.Pid;
    public string Title => Process.ServiceName.Length > 0 && Process.ServiceName != Process.Title ? $"{Process.Title} ({Process.ServiceName})" : Process.Title;
    public string VerdictText => FileLockRules.VerdictText(Verdict);
    public ImageSource? Icon => ProcessIcons.Get(Process.Name);
}

public sealed record DriveChoice(string Root, string Label);

/// <summary>Fase 10, leva 10.1: destravador de arquivos, pastas e pendrives ("Quem está usando este arquivo?").</summary>
public partial class FileUnlockWindow : Window
{
    private readonly MainWindow _main;
    private readonly ObservableCollection<LockRow> _rows = [];
    private string _path = "";
    private LockTargetKind? _kind;
    private bool _busy;

    public FileUnlockWindow(MainWindow main, string? path = null)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        Grid.ItemsSource = _rows;
        Drives.ItemsSource = DriveList();
        Drives.SelectedIndex = Drives.Items.Count > 0 ? 0 : -1;
        UpdateButtons();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        if (!string.IsNullOrWhiteSpace(path)) Loaded += async (_, _) => await CheckAsync(path);
    }

    /// <summary>Unidades prontas; as removíveis (pendrives, cartões) primeiro.</summary>
    private static List<DriveChoice> DriveList()
    {
        try
        {
            return DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Removable or DriveType.Fixed)
                .OrderBy(d => d.DriveType == DriveType.Removable ? 0 : 1).ThenBy(d => d.Name)
                .Select(d => new DriveChoice(d.Name, $"{d.Name.TrimEnd('\\')} {Label(d)}".Trim())).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }

        static string Label(DriveInfo d)
        {
            string kind = d.DriveType == DriveType.Removable ? Loc.T("removível") : Loc.T("disco");
            try { return d.VolumeLabel.Length > 0 ? $"{d.VolumeLabel} ({kind})" : $"({kind})"; }
            catch (IOException) { return $"({kind})"; }
        }
    }

    private void PickFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Escolha o arquivo em uso"), CheckFileExists = true };
        if (dlg.ShowDialog(this) == true) _ = CheckAsync(dlg.FileName);
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Escolha a pasta em uso") };
        if (dlg.ShowDialog(this) == true) _ = CheckAsync(dlg.FolderName);
    }

    private void CheckDrive_Click(object sender, RoutedEventArgs e)
    {
        if (Drives.SelectedItem is DriveChoice d) _ = CheckAsync(d.Root);
        else Info.Text = Loc.T("Nenhuma unidade pronta foi encontrada.");
    }

    private void Recheck_Click(object sender, RoutedEventArgs e)
    {
        if (_path.Length > 0) _ = CheckAsync(_path);
        else Info.Text = Loc.T("Escolha antes um arquivo, uma pasta ou uma unidade.");
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } dropped) _ = CheckAsync(dropped[0]);
    }

    /// <summary>Pergunta ao Windows quem usa o item. Abrir a janela já apontando para uma unidade (bandeja) também passa aqui.</summary>
    public async Task CheckAsync(string path)
    {
        if (_busy) return;
        var kind = FileLockRules.KindOf(path, File.Exists, Directory.Exists);
        if (kind is null) { Info.Text = Loc.F("Não encontrado: {0}", path); return; }
        _path = kind == LockTargetKind.Drive ? FileLockRules.DriveRoot(path) : path;
        _kind = kind;
        Target.Text = _path;
        _busy = true;
        UpdateButtons();
        Info.Text = kind == LockTargetKind.File ? Loc.T("Perguntando ao Windows…") : Loc.T("Listando os arquivos e perguntando ao Windows…");
        try
        {
            string target = _path;
            var (found, files) = await Task.Run(() =>
            {
                var list = FileUnlocker.FilesOf(target, kind.Value);
                return (FileUnlocker.WhoIsUsing(list), list.Count);
            });
            _rows.Clear();
            foreach (var p in FileLockRules.Order(found, Environment.ProcessId))
                _rows.Add(new LockRow(p, FileLockRules.Classify(p, Environment.ProcessId)));
            string limit = files >= FileLockRules.MaxFiles ? " " + Loc.F("Só os primeiros {0:N0} arquivos foram verificados.", FileLockRules.MaxFiles) : "";
            Info.Text = (_rows.Count == 0
                ? Loc.T("Nenhum programa está usando este item agora. Se o Windows ainda recusar, tente de novo em alguns segundos ou reinicie o Explorador.")
                : Loc.F("{0} usando este item.", Pt.N(_rows.Count, "programa", "programas"))) + limit;
            if (_rows.Count > 0) Grid.SelectedIndex = 0;
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private LockRow? Current => Grid.SelectedItem as LockRow;

    private void UpdateButtons()
    {
        EjectBtn.IsEnabled = !_busy && _kind == LockTargetKind.Drive && IsRemovable(_path);
        CloseProgramBtn.IsEnabled = !_busy;
        RestartExplorerBtn.IsEnabled = !_busy;
    }

    private static bool IsRemovable(string root)
    {
        try { return new DriveInfo(root).DriveType == DriveType.Removable; }
        catch (ArgumentException) { return false; }
    }

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { CloseProgram_Click(sender, e); e.Handled = true; }
    }

    private async void CloseProgram_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) { Info.Text = Loc.T("Selecione um programa na lista."); return; }
        switch (row.Verdict)
        {
            case LockVerdict.RestartExplorer: RestartExplorer_Click(sender, e); return;
            case LockVerdict.CanClose: break;
            default: Info.Text = row.VerdictText; return;
        }
        if (_main.Simulate(Loc.F("Fechar {0}", row.Name), Loc.F("Fecharia {0} (PID {1}) para liberar {2}.", row.Name, row.Pid, _path))) return;
        if (!Dialogs.Confirm(this, Loc.F("Fechar {0}?", row.Name),
                Loc.F("{0} está usando {1}. O app pede ao programa para fechar (ele pode perguntar se você quer salvar) e, se ele não fechar em 5 segundos, encerra.", row.Name, _path)
                + "\n\n" + Loc.T("Trabalho não salvo nesse programa será perdido."), "Fechar programa", danger: true)) return;
        var result = await Task.Run(() => FileUnlocker.Close(row.Process));
        Info.Text = result switch
        {
            CloseResult.Closed => Loc.F("{0} foi fechado.", row.Name),
            CloseResult.AlreadyGone => Loc.F("{0} já tinha fechado.", row.Name),
            CloseResult.NotAllowed => row.VerdictText,
            _ => Loc.F("Não foi possível fechar {0}: falta permissão (tente abrir o app como administrador).", row.Name),
        };
        if (result == CloseResult.Closed) ActionLog.Add(Loc.F("Programa fechado para destravar: {0}", row.Name), Loc.F("PID {0}, usava {1}", row.Pid, _path));
        if (result is CloseResult.Closed or CloseResult.AlreadyGone) await CheckAsync(_path);
    }

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_main.Simulate("Reiniciar o Explorador de Arquivos", "Fecharia e abriria de novo o Explorador (barra de tarefas e janelas de pasta).")) return;
        if (!Dialogs.Confirm(this, Loc.T("Reiniciar o Explorador de Arquivos?"),
                Loc.T("A barra de tarefas e as janelas de pasta somem por alguns segundos e voltam sozinhas. Cópias de arquivos em andamento no Explorador são interrompidas."),
                "Reiniciar")) return;
        bool ok = await Task.Run(FileUnlocker.RestartExplorer);
        Info.Text = ok ? Loc.T("Explorador reiniciado.") : Loc.T("O Explorador não voltou sozinho: aperte Ctrl+Shift+Esc e use Arquivo › Executar nova tarefa › explorer.");
        if (ok) ActionLog.Add("Explorador de Arquivos reiniciado", _path);
        if (ok && _path.Length > 0) await CheckAsync(_path);
    }

    private async void Eject_Click(object sender, RoutedEventArgs e)
    {
        if (_kind != LockTargetKind.Drive || _path.Length == 0) return;
        string drive = _path.TrimEnd('\\');
        if (_main.Simulate(Loc.F("Ejetar {0}", drive), Loc.F("Ejetaria {0} com segurança.", drive))) return;
        var result = await Task.Run(() => FileUnlocker.Eject(_path[0]));
        Info.Text = EjectText(result, drive);
        if (result == EjectResult.Ejected) ActionLog.Add(Loc.F("Unidade ejetada: {0}", drive));
        else if (result == EjectResult.InUse) await CheckAsync(_path);
    }

    internal static string EjectText(EjectResult result, string drive) => result switch
    {
        EjectResult.Ejected => Loc.F("{0} pode ser removido com segurança.", drive),
        EjectResult.InUse => Loc.F("{0} ainda está em uso: feche os programas da lista e tente de novo.", drive),
        EjectResult.NotRemovable => Loc.F("{0} não é uma unidade removível.", drive),
        _ => Loc.F("O Windows não ejetou {0}. Tente de novo ou use o ícone de remover hardware ao lado do relógio.", drive),
    };

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Info.Text = Loc.T("Espere terminar o que está em andamento."); return; }
        Close();
    }
}
