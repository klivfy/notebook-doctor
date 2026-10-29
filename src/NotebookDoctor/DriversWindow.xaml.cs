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

/// <summary>Uma linha da aba "Do fabricante".</summary>
public sealed class OemRow
{
    public DriverRecommendation Rec { get; }
    public OemRow(DriverRecommendation rec) { Rec = rec; }

    public string Name => Rec.ComponentName;
    public string SourceText
    {
        get
        {
            string who = OemLinks.Route(Rec.Source).DisplayName;
            return Rec.Authority switch
            {
                RecommendationAuthority.OemMandatory => Rec.Source == SupportVendor.Generic
                    ? Loc.T("Só do fabricante do notebook, pelo Windows Update")
                    : Loc.F("Só do fabricante do notebook ({0})", who),
                RecommendationAuthority.ComponentPreferred => Loc.F("Do fabricante da peça ({0})", who),
                _ => Loc.F("{0} ou o fabricante do notebook", who),
            };
        }
    }
    public string RiskText => Rec.Risk switch
    {
        RiskLevel.Critical => Loc.T("Crítico"),
        RiskLevel.High => Loc.T("Alto"),
        RiskLevel.Medium => Loc.T("Médio"),
        _ => Loc.T("Baixo"),
    };
    public bool Serious => Rec.Risk >= RiskLevel.High;
    public string Notes => Rec.RequiresRestorePoint ? Rec.Notes + " " + Loc.T("Crie um ponto de restauração antes de instalar.") : Rec.Notes;
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
    private readonly ObservableCollection<OemRow> _oem = [];
    private enum Mode { Offers, Copies, Oem }
    private Mode _mode = Mode.Offers;
    private bool _busy, _scanned;
    private HardwareInventory? _inv;
    private List<DetectedTool> _tools = [];
    private bool? _protection;

    public DriversWindow(MainWindow main, bool oem = false)
    {
        InitializeComponent();
        _main = main;
        Owner = main;
        OffersGrid.ItemsSource = _offers;
        CopiesGrid.ItemsSource = _copies;
        OemGrid.ItemsSource = _oem;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) => { LoadCopies(); if (oem) ShowMode(Mode.Oem); else UpdateInfo(); };
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
        OemScanBtn.IsEnabled = OemOpenBtn.IsEnabled = OemModelBtn.IsEnabled = OemToolBtn.IsEnabled = OemRestoreBtn.IsEnabled = !busy;
    }

    // ---------- abas ----------

    private void ShowMode(Mode mode)
    {
        if (_busy) { Msg.Text = Loc.T("Espere terminar o que está em andamento."); return; }
        _mode = mode;
        static Visibility V(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        OffersGrid.Visibility = ScanBtn.Visibility = InstallBtn.Visibility = V(mode == Mode.Offers);
        CopiesGrid.Visibility = RestoreBtn.Visibility = DeleteBtn.Visibility = V(mode == Mode.Copies);
        OemPanel.Visibility = OemScanBtn.Visibility = OemOpenBtn.Visibility = V(mode == Mode.Oem);
        OffersTab.SetResourceReference(StyleProperty, mode == Mode.Offers ? "TonalButton" : "TextButton");
        CopiesTab.SetResourceReference(StyleProperty, mode == Mode.Copies ? "TonalButton" : "TextButton");
        OemTab.SetResourceReference(StyleProperty, mode == Mode.Oem ? "TonalButton" : "TextButton");
        Intro.Text = mode == Mode.Oem
            ? Loc.T("Firmware, chipset, energia e touchpad só do fabricante do notebook; vídeo dedicado, do fabricante da placa. O app lê este computador, mostra de onde cada driver deve vir e abre o site oficial. Nada é baixado nem instalado por aqui, e nada sai do computador sem você clicar.")
            : Loc.T("Só drivers que o próprio Windows Update oferece: nada de sites de terceiros. Só aparece como recomendado o que é mais novo que o instalado. Antes de instalar, o app cria um ponto de restauração e guarda uma cópia do driver atual, para voltar quando precisar. Exige administrador para instalar.");
        Msg.Text = "";
        if (mode == Mode.Oem) { UpdateOemButtons(); if (_inv is null) OemScan_Click(this, new RoutedEventArgs()); }
        UpdateInfo();
    }

    private void OffersTab_Click(object sender, RoutedEventArgs e) => ShowMode(Mode.Offers);
    private void CopiesTab_Click(object sender, RoutedEventArgs e) { LoadCopies(); ShowMode(Mode.Copies); }
    private void OemTab_Click(object sender, RoutedEventArgs e) => ShowMode(Mode.Oem);

    private void LoadCopies()
    {
        _copies.Clear();
        foreach (var b in DriverUpdater.ListBackups(AppPaths.Dir)) _copies.Add(new DriverCopyRow(b));
    }

    private void UpdateInfo()
    {
        if (_mode == Mode.Oem)
            Info.Text = _inv is null ? "" : Loc.F("{0} itens · {1} só do fabricante do notebook.", _oem.Count, _oem.Count(r => r.Rec.Authority == RecommendationAuthority.OemMandatory));
        else if (_mode == Mode.Copies)
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

    // ---------- do fabricante ----------

    private async void OemScan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        Msg.Text = Loc.T("Lendo este computador…");
        try
        {
            if (AppPaths.Demo)
            {
                _inv = DemoData.OemInventory();
                _tools = [];
                _protection = true;
            }
            else
            {
                _inv = await OemProbe.InventoryAsync();
                _tools = await Task.Run(OemProbe.DetectTools);
                _protection = OemProbe.SystemRestoreEnabled();
            }
            if (_inv is null) { Msg.Text = Loc.T("Não consegui ler o computador pelo Windows (WMI). Tente de novo."); return; }
            FillOem();
            Msg.Text = "";
        }
        finally { SetBusy(false); UpdateOemButtons(); UpdateInfo(); }
    }

    private void FillOem()
    {
        if (_inv is null) return;
        _oem.Clear();
        foreach (var r in OemAdvisor.Recommend(_inv)) _oem.Add(new OemRow(r));

        var c = _inv.Chassis;
        string bios = _inv.Bios.Version.Length == 0 ? ""
            : " · " + Loc.F("BIOS {0}", _inv.Bios.Version + (_inv.Bios.ReleaseDate is { } d ? " (" + d.ToString("d", Loc.Culture) + ")" : ""));
        string name = (c.Manufacturer + " " + c.Model).Trim();
        OemComputer.Text = (name.Length > 0 ? name : Loc.T("Fabricante não informado")) + bios;

        var route = OemLinks.Route(_inv.Oem, c.Model);
        var lines = new List<string>();
        if (_inv.Oem is not (SupportVendor.Dell or SupportVendor.Lenovo or SupportVendor.Hp))
            lines.Add(Loc.T("O fabricante deste computador não tem página de drivers conhecida pelo app: para firmware e chipset, o caminho oficial é o Windows Update (Atualizações opcionais)."));
        else if (route.Tool is { } tool)
            lines.Add(_tools.Any(t => t.Tool.Key == tool.Key)
                ? Loc.F("O {0} está instalado: é o aplicativo oficial do fabricante para manter estes drivers em dia.", tool.Name)
                : Loc.F("O aplicativo oficial do fabricante para manter estes drivers em dia é o {0}. Ele não está instalado.", tool.Name));
        var others = _tools.Where(t => t.Tool.Key != route.Tool?.Key).Select(t => t.Tool.Name).ToList();
        if (others.Count > 0) lines.Add(Loc.F("Também instalados: {0}.", string.Join(", ", others)));
        OemTools.Text = string.Join(" ", lines);
        UpdateRestoreBox();
    }

    private RestoreReadiness Readiness => OemRules.Readiness(_protection, Host.Settings.LastRestorePoint, DateTime.Now);

    /// <summary>Regra 3: com algo de risco alto ou crítico na lista, avisa enquanto não houver ponto de restauração recente.</summary>
    private void UpdateRestoreBox()
    {
        bool risky = _oem.Any(r => r.Rec.RequiresRestorePoint);
        var state = Readiness;
        OemRestoreBox.Visibility = risky && state != RestoreReadiness.Ready ? Visibility.Visible : Visibility.Collapsed;
        OemRestore.Text = state switch
        {
            RestoreReadiness.ProtectionOff => Loc.T("A Proteção do Sistema está desligada: o Windows não consegue criar pontos de restauração. Ligue-a antes de instalar BIOS, chipset ou touchpad."),
            RestoreReadiness.Unknown => Loc.T("Não consegui ler a Proteção do Sistema. Antes de instalar BIOS, chipset ou touchpad, crie um ponto de restauração."),
            _ => Loc.T("Antes de instalar BIOS, chipset ou touchpad, crie um ponto de restauração: se algo der errado, o Windows volta ao estado de antes."),
        };
        OemRestoreBtn.Content = state == RestoreReadiness.ProtectionOff ? Loc.T("Abrir a Proteção do Sistema") : Loc.T("Criar ponto de restauração");
    }

    private void UpdateOemButtons()
    {
        bool oem = _mode == Mode.Oem;
        OemModelBtn.Visibility = oem && _inv is not null && OemLinks.ModelPageFor(_inv.Chassis, false) is not null ? Visibility.Visible : Visibility.Collapsed;
        OemRestoreBtn.Visibility = oem && OemRestoreBox.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        var tool = _inv is null ? null : OemLinks.Route(_inv.Oem, _inv.Chassis.Model).Tool;
        var found = tool is null ? null : _tools.FirstOrDefault(t => t.Tool.Key == tool.Key);
        string? text = tool is null ? null
            : found is not null ? (found.Path.Length > 0 ? Loc.F("Abrir o {0}", tool.Name) : null)
            : tool.WingetId.Length > 0 ? Loc.F("Instalar o {0} pelo WinGet…", tool.Name)
            : tool.PageUrl == OemLinks.LenovoVantageStore ? Loc.F("Ver o {0} na Microsoft Store", tool.Name)
            : null;
        OemToolBtn.Content = text ?? "";
        OemToolBtn.Visibility = oem && text is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OemGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => Msg.Text = "";

    /// <summary>A lista mostra todas as linhas e quem rola é o painel: a roda do mouse sobre a lista vai para ele.</summary>
    private void OemGrid_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        e.Handled = true;
        OemPanel.ScrollToVerticalOffset(OemPanel.VerticalOffset - e.Delta);
    }

    private void OpenOfficial(string url)
    {
        // Só abre endereço que o próprio app montou: nada lido do computador vira link sem passar por aqui
        if (!OemLinks.IsOfficial(url)) { Msg.Text = Loc.T("Endereço fora dos sites oficiais: não abri."); return; }
        AppInfo.OpenShell(url);
    }

    private void OemOpen_Click(object sender, RoutedEventArgs e)
    {
        if (OemGrid.SelectedItem is not OemRow row) { Msg.Text = Loc.T("Selecione um item na lista."); return; }
        if (row.Rec.RequiresRestorePoint && Readiness != RestoreReadiness.Ready)
        {
            string why = Readiness == RestoreReadiness.ProtectionOff
                ? Loc.T("A Proteção do Sistema está desligada, então o Windows não tem como voltar atrás. Ligue-a e crie um ponto de restauração antes de instalar o que baixar.")
                : Loc.T("Ainda não há um ponto de restauração recente. Crie um (botão “Criar ponto de restauração”) antes de instalar o que baixar.");
            if (!Dialogs.Confirm(this, Loc.T("Ponto de restauração antes"), Loc.F("“{0}” é de risco {1}.", row.Name, row.RiskText.ToLower(Loc.Culture)) + " " + why,
                    Loc.T("Abrir a página"))) return;
        }
        OpenOfficial(row.Rec.DirectUrl);
        Msg.Text = Loc.T("Página aberta no navegador. Baixe só o arquivo do seu modelo e do seu Windows.");
    }

    /// <summary>
    /// Regra 4: o número de série só sai do computador aqui, num endereço que a pessoa vê e aprova. Sem número gravado,
    /// abre a página geral (ou a do tipo de máquina, na Lenovo, que não identifica o aparelho).
    /// </summary>
    private void OemModel_Click(object sender, RoutedEventArgs e)
    {
        if (_inv is null) return;
        var c = _inv.Chassis;
        string who = OemLinks.Route(_inv.Oem).DisplayName;
        string serial = OemRules.SafeSerial(c.SerialNumber);
        if (_inv.Oem == SupportVendor.Hp && serial.Length > 0)
        {
            if (!Dialogs.Confirm(this, Loc.T("Número de série"),
                    Loc.F("O site da HP pede o número de série para mostrar os drivers deste modelo. O app copia o número ({0}) para você colar na página; ele só vai para a HP se você colar.", serial),
                    Loc.T("Copiar e abrir"))) return;
            try { Clipboard.SetText(serial); } catch (System.Runtime.InteropServices.ExternalException) { }
            OpenOfficial(OemLinks.Route(SupportVendor.Hp).PortalUrl);
            Msg.Text = Loc.T("Número de série copiado. Cole com Ctrl+V no campo da página.");
            return;
        }
        var page = OemLinks.ModelPageFor(c, withSerial: true);
        if (page is null) return;
        if (page.SendsSerial && !Dialogs.Confirm(this, Loc.T("Enviar o número de série?"),
                Loc.F("A página do seu modelo abre com o número de série deste computador ({0}) no endereço. Assim o site da {1} mostra os drivers exatos deste aparelho, mas fica sabendo qual computador é. Para não enviar o número, use “Abrir a página oficial”.", serial, who),
                Loc.T("Abrir com o número"))) return;
        OpenOfficial(page.Url);
        Msg.Text = page.SendsSerial ? Loc.T("Página aberta no navegador.") : Loc.T("O fabricante não gravou o número de série neste computador: abri a página geral de drivers.");
    }

    private async void OemTool_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _inv is null || OemLinks.Route(_inv.Oem, _inv.Chassis.Model).Tool is not { } tool) return;
        var found = _tools.FirstOrDefault(t => t.Tool.Key == tool.Key);
        if (found is not null)
        {
            // O Dell Command | Update tem a linha de comando e, ao lado, a janela; a pessoa quer a janela
            string exe = found.Path;
            if (tool.Key == "dcu" && Path.GetDirectoryName(exe) is { } dir && File.Exists(Path.Combine(dir, "DellCommandUpdate.exe"))) exe = Path.Combine(dir, "DellCommandUpdate.exe");
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
            catch (Win32Exception) { Msg.Text = Loc.F("Não consegui abrir o {0}.", tool.Name); }
            return;
        }
        if (tool.WingetId.Length == 0) { OpenOfficial(tool.PageUrl); return; }

        if (_main.Simulate(Loc.T("Instalar aplicativo do fabricante"), Loc.F("Instalaria o {0} pelo WinGet ({1}).", tool.Name, tool.WingetId))) return;
        if (AppPaths.Demo) return;
        if (!await WingetRunner.AvailableAsync())
        {
            Msg.Text = Loc.T("O WinGet não está instalado. Baixe o aplicativo pelo site do fabricante (“Abrir a página oficial”).");
            return;
        }
        var s = Host.Settings;
        if (!s.WingetSourceTerms)
        {
            if (!Dialogs.Confirm(this, Loc.T("Termos do catálogo do WinGet"),
                    Loc.T("Para consultar as versões, o WinGet usa o catálogo público dele e exige que você aceite os termos de uso da fonte (https://aka.ms/winget-agreements). O Notebook Doctor não aceita nada em seu nome. Se não aceitar, a busca não é feita."),
                    Loc.T("Li e aceito"))) return;
            s.WingetSourceTerms = true;
            s.Save();
        }
        if (!Dialogs.Confirm(this, Loc.T("Aceitar os termos e instalar"),
                Loc.F("Instalar o {0} pelo WinGet? Ele vem do catálogo público do WinGet, publicado pelo próprio fabricante. Ao continuar, você aceita os termos de licença do {0}. O instalador pode pedir permissão de administrador. Para remover depois, use a aba Programas.", tool.Name),
                Loc.T("Aceito os termos e instalo"))) return;

        SetBusy(true);
        Msg.Text = Loc.F("Instalando o {0}… pode levar alguns minutos.", tool.Name);
        try
        {
            WingetRun run;
            try { run = await WingetRunner.InstallAsync(tool.WingetId, s.WingetSourceTerms, packageTermsAccepted: true); }
            catch (Win32Exception) { Msg.Text = Loc.T("O WinGet não respondeu."); return; }
            bool ok = !run.TimedOut && WingetCommands.Succeeded(run.ExitCode);
            string result = run.TimedOut
                ? Loc.T("O WinGet passou de 45 minutos. O app parou de esperar, mas o instalador pode ainda estar rodando.")
                : WingetCommands.Classify(run.ExitCode) switch
                {
                    ExitKind.Done => Loc.T("Instalado."),
                    ExitKind.NotApplicable => Loc.T("Já está instalado ou não se aplica a este computador."),
                    ExitKind.Cancelled => Loc.T("Cancelado na janela de permissão."),
                    _ => Loc.F("O WinGet terminou com o código 0x{0:X8}.", run.ExitCode),
                };
            ActionLog.Add(ok ? Loc.F("Programa instalado: {0}", tool.Name) : Loc.F("Falha ao instalar: {0}", tool.Name), Loc.F("WinGet, {0}. {1}", tool.WingetId, result));
            Msg.Text = result;
            _tools = await Task.Run(OemProbe.DetectTools);
            FillOem();
        }
        finally { SetBusy(false); UpdateOemButtons(); }
    }

    private async void OemRestore_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Readiness == RestoreReadiness.ProtectionOff)
        {
            try { Process.Start(new ProcessStartInfo(SystemExe.System32("SystemPropertiesProtection.exe")) { UseShellExecute = true, WorkingDirectory = Environment.SystemDirectory }); }
            catch (Win32Exception) { Msg.Text = Loc.T("Não consegui abrir a Proteção do Sistema."); }
            return;
        }
        if (_main.Simulate(Loc.T("Criar ponto de restauração"), Loc.T("Criaria um ponto de restauração do Windows."))) return;
        if (AppPaths.Demo) return;
        if (!AppInfo.IsAdmin)
        {
            if (Dialogs.Confirm(this, Loc.T("Precisa de administrador"), Loc.T("Criar ponto de restauração exige administrador. Reabrir o app como administrador?"), Loc.T("Reabrir")))
                Host.RestartAsAdmin();
            return;
        }
        SetBusy(true);
        Msg.Text = Loc.T("Criando o ponto de restauração…");
        try
        {
            string restore = await RestorePoint.EnsureAsync(Host.Settings, Loc.T("antes de instalar driver"), force: true);
            Msg.Text = restore == "criado" ? Loc.T("Ponto de restauração criado.") : Loc.F("Ponto de restauração: {0}.", Loc.T(restore));
            _protection = OemProbe.SystemRestoreEnabled();
            UpdateRestoreBox();
        }
        finally { SetBusy(false); UpdateOemButtons(); }
    }
}
