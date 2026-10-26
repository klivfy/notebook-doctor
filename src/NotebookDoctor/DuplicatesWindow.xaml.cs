using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class DupeRow : CheckRow
{
    private static readonly HashSet<string> Decodable = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico" };
    private ImageSource? _thumb;
    private bool _loading;

    public DuplicateGroup Group { get; }
    public DuplicateFile File { get; }
    public int GroupIndex { get; }

    public DupeRow(DuplicateGroup group, DuplicateFile file, int groupIndex) { Group = group; File = file; GroupIndex = groupIndex; }

    public string Path => File.Path;
    public string Name => System.IO.Path.GetFileName(File.Path);
    public string Folder => System.IO.Path.GetDirectoryName(File.Path) ?? "";
    public string SizeText => Units.Bytes(Group.Size);
    public string ModifiedText => File.Modified == DateTime.MinValue ? "" : File.Modified.ToString("d", Loc.Culture);
    public string GroupText => Loc.F("Grupo {0}", GroupIndex + 1);
    public bool Odd => GroupIndex % 2 == 1;

    /// <summary>Miniatura da imagem, ou o ícone do tipo de arquivo. Carrega em segundo plano na primeira vez que a linha aparece.</summary>
    public ImageSource? Thumb
    {
        get
        {
            if (_thumb is not null || _loading) return _thumb;
            _loading = true;
            string path = File.Path;
            if (Decodable.Contains(System.IO.Path.GetExtension(path)))
                Task.Run(() =>
                {
                    ImageSource? image = null;
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path);
                        bmp.DecodePixelWidth = 80;
                        bmp.CacheOption = BitmapCacheOption.OnLoad; // não deixa o arquivo aberto
                        bmp.EndInit();
                        bmp.Freeze();
                        image = bmp;
                    }
                    catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException or FileFormatException) { }
                    if (image is not null) { _thumb = image; Raise(nameof(Thumb)); }
                    else FromIcon(path);
                });
            else FromIcon(path);
            return _thumb;
        }
    }

    private void FromIcon(string path)
    {
        _thumb = ProcessIcons.ForFile(path, 0, () => { _thumb = ProcessIcons.ForFile(path, 0); Raise(nameof(Thumb)); });
        if (_thumb is not null) Raise(nameof(Thumb));
    }
}

/// <summary>
/// Leva 6.3 (C08): revisão dos grupos de arquivos idênticos. A pessoa marca as cópias a apagar; o grupo nunca fica sem
/// uma, e <see cref="DuplicateRemover"/> confere o conteúdo de novo no disco antes de cada exclusão.
/// </summary>
public partial class DuplicatesWindow : Window
{
    private readonly MainWindow _main;
    private readonly List<DuplicateGroup> _groups;
    private readonly ObservableCollection<DupeRow> _rows = [];
    private readonly KnownPlaces _places = WindowsPlaces.Current(AppPaths.Dir);
    private bool _busy, _adjusting;

    public bool RemovedAny { get; private set; }

    public DuplicatesWindow(MainWindow main, IEnumerable<DuplicateGroup> groups)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        _groups = groups.ToList();
        Grid.ItemsSource = _rows;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Rebuild();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Rebuild()
    {
        _rows.Clear();
        for (int g = 0; g < _groups.Count; g++)
            foreach (var f in _groups[g].Files)
            {
                var row = new DupeRow(_groups[g], f, g);
                row.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(CheckRow.Selected)) OnSelectionChanged(row); };
                _rows.Add(row);
            }
        UpdateInfo();
    }

    /// <summary>A última cópia do grupo não pode ser marcada: o clique que marcaria todas é desfeito.</summary>
    private void OnSelectionChanged(DupeRow row)
    {
        if (_adjusting) return;
        if (row.Selected && _rows.Where(r => ReferenceEquals(r.Group, row.Group)).All(r => r.Selected))
        {
            _adjusting = true;
            row.Selected = false;
            _adjusting = false;
            Msg.Text = Loc.T("Uma cópia de cada grupo sempre fica.");
        }
        UpdateInfo();
    }

    private void UpdateInfo()
    {
        var marked = _rows.Where(r => r.Selected).ToList();
        Info.Text = _rows.Count == 0
            ? Loc.T("Nenhum grupo de duplicados.")
            : Loc.F("{0} grupos · {1} marcados · {2} serão liberados.", _groups.Count, marked.Count, Units.Bytes(marked.Sum(r => r.Group.Size)));
    }

    private void Mark(KeepRule rule)
    {
        _adjusting = true;
        foreach (var g in _groups)
        {
            var keep = DuplicateRules.Keeper(g, rule);
            foreach (var r in _rows.Where(r => ReferenceEquals(r.Group, g))) r.Selected = !ReferenceEquals(r.File, keep);
        }
        _adjusting = false;
        UpdateInfo();
        Msg.Text = "";
    }

    private void KeepOldest_Click(object sender, RoutedEventArgs e) => Mark(KeepRule.Oldest);
    private void KeepNewest_Click(object sender, RoutedEventArgs e) => Mark(KeepRule.Newest);
    private void None_Click(object sender, RoutedEventArgs e) { _adjusting = true; foreach (var r in _rows) r.Selected = false; _adjusting = false; UpdateInfo(); }

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource is CheckBox || Grid.SelectedItem is not DupeRow row) return;
        row.Selected = !row.Selected;
        e.Handled = true;
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not DupeRow row) { Msg.Text = Loc.T("Selecione uma cópia na lista."); return; }
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{row.Path}\""); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Msg.Text = ex.Message; }
    }

    private void Recycle_Click(object sender, RoutedEventArgs e) => Delete(permanent: false);
    private void Permanent_Click(object sender, RoutedEventArgs e) => Delete(permanent: true);

    private async void Delete(bool permanent)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        var marked = _rows.Where(r => r.Selected).ToList();
        if (marked.Count == 0) { Msg.Text = Loc.T("Nada selecionado."); return; }
        if (_busy) return;
        long bytes = marked.Sum(r => r.Group.Size);
        string what = Loc.F("{0}, {1}", Pt.N(marked.Count, Loc.T("arquivo"), Loc.T("arquivos")), Units.Bytes(bytes));
        if (_main.Simulate(Loc.T("Apagar duplicados"), Loc.F("Apagaria {0}.", what))) return;
        bool cloud = marked.Any(r => r.Path.Contains(@"\OneDrive", StringComparison.OrdinalIgnoreCase));
        string note = (cloud ? "\n\n" + Loc.T("Há arquivos do OneDrive: apagar aqui também apaga na nuvem.") : "")
                      + "\n\n" + Loc.T("Uma cópia de cada grupo continua no lugar.");
        string text = permanent
            ? Loc.F("Apagar de vez {0}? Isso não dá para desfazer.{1}", what, note)
            : Loc.F("Enviar {0} para a Lixeira?{1}", what, note);
        if (!Dialogs.Confirm(this, Loc.T("Confirmar"), text, permanent ? Loc.T("Apagar de vez") : Loc.T("Enviar para a Lixeira"), danger: permanent)) return;

        _busy = true;
        Msg.Text = Loc.T("Conferindo e apagando…");
        var work = marked.GroupBy(r => r.Group).Select(g => (g.Key, (IReadOnlyList<string>)g.Select(r => r.Path).ToList())).ToList();
        DuplicateRemoval result;
        try { result = await Task.Run(() => DuplicateRemover.Remove(work, _places, KeepRule.Oldest, permanent)); }
        finally { _busy = false; }

        RemovedAny |= result.Files > 0;
        // Fora da lista: o que já não existe no disco. Grupos com menos de duas cópias somem.
        for (int i = 0; i < _groups.Count; i++)
        {
            var left = _groups[i].Files.Where(f => File.Exists(f.Path)).ToList();
            _groups[i] = _groups[i] with { Files = left };
        }
        _groups.RemoveAll(g => g.Files.Count < 2);
        Rebuild();
        string summary = Loc.F("{0} liberados · {1}.", Units.Bytes(result.Bytes), Pt.N(result.Files, Loc.T("arquivo"), Loc.T("arquivos")));
        if (result.Skipped.Count > 0) summary += " " + Loc.F("{0} pulados (mudaram, estão em uso ou a Lixeira recusou).", result.Skipped.Count);
        ActionLog.Add(permanent ? Loc.T("Duplicados apagados") : Loc.T("Duplicados enviados para a Lixeira"), summary);
        Msg.Text = summary;
        System.Media.SystemSounds.Asterisk.Play();
    }
}
