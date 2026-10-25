using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public sealed class DriverRow
{
    public DriverOffer Offer { get; }
    public DriverRow(DriverOffer offer) { Offer = offer; }

    public string Title => Offer.Offered.Title;
    public string DeviceClass => Offer.Offered.DeviceClass;
    public string CurrentText => Offer.Current is null ? "" : VersionDate(Offer.Current.Version, Offer.Current.Date);
    public string OfferedText => VersionDate(Offer.Offered.Version, Offer.Offered.Date);
    public string SizeText => DriverUpdateRules.SizeText(Offer.Offered.SizeBytes);
    public string KindText => Offer.Offered.Optional ? Loc.T("Opcional") : Loc.T("Normal");
    public bool Recommended => Offer.Verdict == DriverVerdict.Newer;
    public string VerdictText => Offer.Verdict switch
    {
        DriverVerdict.Newer => Offer.CanBackup ? Loc.T("Recomendado: mais novo que o instalado")
            : Loc.T("Recomendado: mais novo (o instalado é do Windows, sem cópia)"),
        DriverVerdict.NotNewer => Loc.T("Não é mais novo que o instalado"),
        _ => Loc.T("Não achei o dispositivo: não dá para comparar"),
    };

    private static string VersionDate(string version, DateTime? date) =>
        (version.Length > 0 ? version : "?") + (date is { } d ? $" · {d.ToString("d", Loc.Culture)}" : "");
}

public sealed class DriverCopyRow
{
    public DriverBackup Backup { get; }
    public DriverCopyRow(DriverBackup b) { Backup = b; }
    public string DeviceName => Backup.DeviceName;
    public string VersionText => (Backup.Version.Length > 0 ? Backup.Version : "?") + (Backup.Date.Length > 0 ? " · " + Backup.Date : "");
    public string CreatedText => Backup.Created.ToString("g", Loc.Culture);
    public string Replaced => Backup.Replaced;
}

/// <summary>
/// Leva 7.2 (C13): drivers só pelo Windows Update. Instalar um driver exige ponto de restauração e cópia do atual antes;
/// sem a cópia de um driver de terceiros, nada é instalado. Voltar usa a cópia (e também fica em Ações, em "Desfazer").
/// </summary>
public partial class DriversWindow : Window
{
    private static App Host => App.Current;
    private readonly MainWindow _main;
    private readonly ObservableCollection<DriverRow> _offers = [];
    private readonly ObservableCollection<DriverCopyRow> _copies = [];
    private bool _busy, _scanned, _copiesMode;

    public DriversWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        OffersGrid.ItemsSource = _offers;
        CopiesGrid.ItemsSource = _copies;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) => { LoadCopies(); UpdateInfo(); };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_busy) e.Cancel = true; // instalação de driver não pode ficar sem quem acompanhe
        base.OnClosing(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Msg.Text = Loc.T("Espere terminar o que está em andamento."); return; }
        Close();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ScanBtn.IsEnabled = InstallBtn.IsEnabled = RestoreBtn.IsEnabled = DeleteBtn.IsEnabled = !busy;
    }

    // ---------- abas ----------

    private void ShowMode(bool copies)
    {
        _copiesMode = copies;
        OffersGrid.Visibility = copies ? Visibility.Collapsed : Visibility.Visible;
        CopiesGrid.Visibility = copies ? Visibility.Visible : Visibility.Collapsed;
        ScanBtn.Visibility = InstallBtn.Visibility = copies ? Visibility.Collapsed : Visibility.Visible;
        RestoreBtn.Visibility = DeleteBtn.Visibility = copies ? Visibility.Visible : Visibility.Collapsed;
        OffersTab.SetResourceReference(StyleProperty, copies ? "TextButton" : "TonalButton");
        CopiesTab.SetResourceReference(StyleProperty, copies ? "TonalButton" : "TextButton");
        Msg.Text = "";
        UpdateInfo();
    }

    private void OffersTab_Click(object sender, RoutedEventArgs e) => ShowMode(false);
    private void CopiesTab_Click(object sender, RoutedEventArgs e) { LoadCopies(); ShowMode(true); }

    private void LoadCopies()
    {
        _copies.Clear();
        foreach (var b in DriverUpdater.ListBackups(AppPaths.Dir)) _copies.Add(new DriverCopyRow(b));
    }

    private void UpdateInfo()
    {
        if (_copiesMode)
            Info.Text = _copies.Count == 0 ? Loc.T("Nenhuma cópia guardada.") : Loc.F("{0} cópias guardadas.", _copies.Count);
        else if (!_scanned) Info.Text = Loc.T("Clique em “Procurar drivers”.");
        else
        {
            int rec = _offers.Count(r => r.Recommended);
            Info.Text = _offers.Count == 0 ? Loc.T("O Windows Update não tem drivers novos para este computador.")
                : Loc.F("{0} drivers oferecidos · {1} recomendados.", _offers.Count, rec);
        }
    }

    private void Optional_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate-optionalupdates") { UseShellExecute = true }); }
        catch (Win32Exception) { Msg.Text = Loc.T("Não consegui abrir o Windows Update."); }
    }

    // ---------- buscar ----------

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        Msg.Text = Loc.T("Consultando o Windows Update… pode levar alguns minutos.");
        try
        {
            if (AppPaths.Demo)
            {
                var installed = new List<InstalledDriver>
                {
                    new("Intel(R) UHD Graphics", @"PCI\VEN_8086&DEV_46A6\3&11583659&0&10", [@"PCI\VEN_8086&DEV_46A6"], "31.0.101.4502", new DateTime(2023, 6, 1), "oem12.inf", "Intel"),
                };
                var offered = new List<OfferedDriver>
                {
                    new("11111111-1111-1111-1111-111111111111", "Intel Corporation - Display - 31.0.101.5333", "Intel Corporation", "Intel(R) UHD Graphics", "Display",
                        @"PCI\VEN_8086&DEV_46A6", new DateTime(2024, 5, 1), "31.0.101.5333", 640L * 1024 * 1024, false, true, ""),
                };
                Fill(DriverUpdateRules.Order(offered, installed));
                return;
            }
            DriverScan scan;
            try { scan = await DriverUpdater.ScanAsync(); }
            catch (Win32Exception) { Msg.Text = Loc.T("Não consegui abrir o PowerShell."); return; }
            catch (System.Text.Json.JsonException) { Msg.Text = Loc.T("O Windows Update respondeu algo que não entendi."); return; }
            if (scan.Error.Length > 0) { Msg.Text = Loc.F("O Windows Update não respondeu: {0}", scan.Error); return; }
            Fill(DriverUpdateRules.Order(scan.Offered, scan.Installed.ToList()));
            Msg.Text = "";
        }
        finally { SetBusy(false); }
    }

    private void Fill(List<DriverOffer> ordered)
    {
        _offers.Clear();
        foreach (var o in ordered) _offers.Add(new DriverRow(o));
        _scanned = true;
        UpdateInfo();
    }

    // ---------- instalar ----------

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (OffersGrid.SelectedItem is not DriverRow row) { Msg.Text = Loc.T("Selecione um driver na lista."); return; }
        var offer = row.Offer;
        if (!offer.CanInstall || offer.Current is null)
        {
            Msg.Text = offer.Verdict == DriverVerdict.NoDevice
                ? Loc.T("Não achei o dispositivo deste driver: sem como guardar o atual, o app não instala.")
                : Loc.T("Este driver não é mais novo que o instalado: não há o que ganhar.");
            return;
        }
        if (_main.Simulate(Loc.T("Instalar driver"), Loc.F("Instalaria “{0}” pelo Windows Update, depois do ponto de restauração e da cópia do driver atual.", offer.Offered.Title))) return;
        if (AppPaths.Demo) return;
        if (!AppInfo.IsAdmin)
        {
            if (Dialogs.Confirm(this, Loc.T("Precisa de administrador"), Loc.T("Instalar driver exige administrador. Reabrir o app como administrador?"), Loc.T("Reabrir")))
                Host.RestartAsAdmin();
            return;
        }

        string copyNote = offer.CanBackup
            ? Loc.T("O app guarda uma cópia do driver atual antes de instalar.")
            : Loc.T("O driver atual é do próprio Windows e não dá para copiar; o Windows mantém a versão dele (Gerenciador de Dispositivos › Reverter driver).");
        string text = Loc.F("Instalar “{0}”?", offer.Offered.Title) + "\n\n"
            + Loc.F("Instalado: {0}", row.CurrentText) + "\n" + Loc.F("Oferecido: {0}", row.OfferedText) + "\n\n"
            + Loc.T("Antes, o app cria um ponto de restauração do Windows.") + " " + copyNote + " "
            + Loc.T("A tela pode piscar e o dispositivo ficar indisponível por instantes; salve seu trabalho. Alguns drivers pedem reiniciar.");
        if (!Dialogs.Confirm(this, Loc.T("Instalar driver"), text, Loc.T("Instalar"))) return;

        // Contrato de licença: se o driver tem um, a pessoa lê e aceita. O app não aceita por ela.
        bool eula = offer.Offered.EulaAccepted;
        if (!eula)
        {
            Dialogs.ShowText(this, Loc.T("Contrato de licença"), offer.Offered.EulaText.Length > 0 ? offer.Offered.EulaText : Loc.T("O Windows Update não mostrou o texto do contrato."));
            if (!Dialogs.Confirm(this, Loc.T("Contrato de licença"), Loc.F("Você leu e aceita o contrato de licença de “{0}”?", offer.Offered.Title), Loc.T("Aceito")))
                return;
            eula = true;
        }

        SetBusy(true);
        try
        {
            Msg.Text = Loc.T("Criando o ponto de restauração…");
            string restore = await RestorePoint.EnsureAsync(Host.Settings, Loc.T("antes de instalar driver"), force: true);
            bool restoreOk = restore == "criado" || restore.StartsWith("já existe", StringComparison.Ordinal);
            if (!restoreOk && !Dialogs.Confirm(this, Loc.T("Sem ponto de restauração"),
                    Loc.F("Não consegui criar o ponto de restauração: {0}. Se o driver novo der problema, não haverá esse caminho de volta. Instalar mesmo assim?", Loc.T(restore)),
                    Loc.T("Instalar sem o ponto"), danger: true))
            {
                Msg.Text = Loc.T("Nada foi instalado.");
                return;
            }

            DriverBackup? backup = null;
            if (offer.CanBackup)
            {
                Msg.Text = Loc.T("Guardando uma cópia do driver atual…");
                try { backup = await DriverUpdater.BackupAsync(offer.Current, offer.Offered.Title, AppPaths.Dir, DateTime.Now); }
                catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException) { backup = null; }
                if (backup is null)
                {
                    // Sem a cópia o app não instala: a volta é a razão de existir deste passo
                    Msg.Text = Loc.T("Não consegui guardar a cópia do driver atual. Nada foi instalado.");
                    return;
                }
            }

            Msg.Text = Loc.T("Baixando e instalando… não feche o app nem desligue o computador.");
            DriverInstallResult result = await DriverUpdater.InstallAsync(offer.Offered, eula);
            string summary = InstallText(result);
            ActionLog.Add(result.Ok ? Loc.F("Driver instalado: {0}", offer.Offered.Title) : Loc.F("Falha ao instalar driver: {0}", offer.Offered.Title),
                Loc.F("{0} → {1}. {2}", row.CurrentText, row.OfferedText, summary),
                result.Ok && backup is not null ? "driver" : "", result.Ok && backup is not null ? backup.Id : "");
            if (!result.Ok && backup is not null) DriverUpdater.DeleteBackup(AppPaths.Dir, backup); // nada mudou: a cópia não serve de nada
            LoadCopies();
            Msg.Text = summary;
            System.Media.SystemSounds.Asterisk.Play();
        }
        finally { SetBusy(false); }
        if (_scanned) Scan_Click(this, new RoutedEventArgs());
    }

    private static string InstallText(DriverInstallResult r)
    {
        if (r.Ok) return r.RebootRequired ? Loc.T("Instalado. Reinicie o computador para o driver novo entrar em uso.") : Loc.T("Instalado.");
        return r.Stage switch
        {
            "find" => Loc.T("O Windows Update não oferece mais este driver. Procure de novo."),
            "eula" => Loc.T("O contrato de licença não foi aceito."),
            "download" => Loc.F("O download falhou (código {0}).", r.Code),
            "timeout" => Loc.T("Passou de 2 horas. O app parou de esperar, mas a instalação pode ainda estar rodando."),
            _ => Loc.F("O Windows Update não concluiu a instalação (código {0}, 0x{1:X8}).", r.Code, r.HResult),
        };
    }

    // ---------- cópias ----------

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (CopiesGrid.SelectedItem is not DriverCopyRow row) { Msg.Text = Loc.T("Selecione uma cópia na lista."); return; }
        if (_main.Simulate(Loc.T("Voltar ao driver anterior"), Loc.F("Reinstalaria o driver {0} de “{1}”.", row.VersionText, row.DeviceName))) return;
        if (AppPaths.Demo) return;
        if (!AppInfo.IsAdmin)
        {
            if (Dialogs.Confirm(this, Loc.T("Precisa de administrador"), Loc.T("Voltar ao driver anterior exige administrador. Reabrir o app como administrador?"), Loc.T("Reabrir")))
                Host.RestartAsAdmin();
            return;
        }
        if (!Dialogs.Confirm(this, Loc.T("Voltar ao driver anterior?"),
                Loc.F("O driver {0} de “{1}” volta a ser o instalado, no lugar do atual. O dispositivo pode ficar indisponível por instantes.", row.VersionText, row.DeviceName),
                Loc.T("Voltar"))) return;
        SetBusy(true);
        Msg.Text = Loc.T("Voltando ao driver anterior…");
        try
        {
            RestoreOutcome outcome;
            try { outcome = await DriverUpdater.RestoreAsync(row.Backup, AppPaths.Dir); }
            catch (Exception ex) when (ex is Win32Exception or IOException or ArgumentException) { Msg.Text = ex.Message; return; }
            string text = DriverTexts.Restore(outcome);
            ActionLog.Add(outcome == RestoreOutcome.Restored ? Loc.F("Driver anterior restaurado: {0}", row.DeviceName) : Loc.F("Volta do driver não concluída: {0}", row.DeviceName),
                Loc.F("{0}. {1}", row.VersionText, text));
            Msg.Text = text;
        }
        finally { SetBusy(false); }
    }

    private void DeleteCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (CopiesGrid.SelectedItem is not DriverCopyRow row) { Msg.Text = Loc.T("Selecione uma cópia na lista."); return; }
        if (!Dialogs.Confirm(this, Loc.T("Apagar a cópia?"), Loc.T("Depois disso, não dá mais para voltar a este driver por aqui."), Loc.T("Apagar"), danger: true)) return;
        DriverUpdater.DeleteBackup(AppPaths.Dir, row.Backup);
        LoadCopies();
        UpdateInfo();
        Msg.Text = Loc.T("Cópia apagada.");
    }
}
