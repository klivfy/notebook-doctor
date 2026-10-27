using System.IO;
using System.Windows;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Leva 8.1 (S01–S03): aba Sistema. O retrato do computador, só leitura, com exportação em TXT, HTML e PDF.</summary>
public partial class MainWindow
{
    private List<InfoSection>? _sysSections;
    private bool _sysLoading;

    private async Task LoadSystemAsync()
    {
        if (_sysLoading) return;
        _sysLoading = true;
        SysRefreshBtn.IsEnabled = false;
        SysMsg.Text = Loc.T("Lendo o computador… pode levar alguns segundos.");
        try
        {
            _sysSections = AppPaths.Demo ? DemoSystem() : await SystemInfoCollector.CollectAsync(Environment.MachineName);
            SysList.ItemsSource = _sysSections;
            SysMsg.Text = _sysSections.Count == 0 ? Loc.T("Não consegui ler as informações do computador.") : "";
        }
        finally { _sysLoading = false; SysRefreshBtn.IsEnabled = true; }
    }

    private void SysTools_Click(object sender, RoutedEventArgs e) => new WindowsToolsWindow(this).ShowDialog();

    // Fase 12, leva 12.1
    private void SysMeeting_Click(object sender, RoutedEventArgs e) => new MeetingWindow(this).ShowDialog();

    // Verificar malware: Defender e MRT do Windows
    private void SysSecurity_Click(object sender, RoutedEventArgs e) => new SecurityWindow(this).ShowDialog();

    private async void SysRefresh_Click(object sender, RoutedEventArgs e) => await LoadSystemAsync();

    private void SysCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_sysSections is not { Count: > 0 }) { SysMsg.Text = Loc.T("Ainda não há o que copiar."); return; }
        try { Clipboard.SetText(SystemInfoExport.Summary(_sysSections, Environment.MachineName)); ShowSnack(Loc.T("Resumo copiado"), Loc.T("Cole com Ctrl+V numa conversa, e-mail ou chamado.")); }
        catch (System.Runtime.InteropServices.ExternalException) { ShowSnack(Loc.T("Não foi possível copiar"), Loc.T("Outro programa está usando a área de transferência. Tente de novo.")); }
    }

    private void SysExport_Click(object sender, RoutedEventArgs e)
    {
        if (_sysSections is not { Count: > 0 }) { SysMsg.Text = Loc.T("Ainda não há o que exportar."); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Loc.F("Informações do sistema {0}", DateTime.Now.ToString("yyyy-MM-dd")),
            Filter = $"{Loc.T("Texto (TXT)")}|*.txt|{Loc.T("Página (HTML)")}|*.html|PDF|*.pdf",
            InitialDirectory = AppPaths.ExportDir(),
        };
        if (dlg.ShowDialog(this) != true) return;
        string path = dlg.FileName, machine = Environment.MachineName;
        string footer = Loc.F("Gerado pelo Notebook Doctor {0} · {1}", AppInfo.Version, AppInfo.Copyright);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".pdf")
                File.WriteAllBytes(path, SimplePdf.Build(SystemInfoExport.PdfLines(_sysSections, machine, DateTime.Now, footer), Loc.T("Informações do sistema")));
            else if (ext.StartsWith(".htm"))
                File.WriteAllText(path, SystemInfoExport.Html(_sysSections, machine, DateTime.Now, footer), new System.Text.UTF8Encoding(true));
            else
                File.WriteAllText(path, SystemInfoExport.Text(_sysSections, machine, DateTime.Now, footer), new System.Text.UTF8Encoding(true));
            ShowSnack(Loc.F("Salvo: {0}", Path.GetFileName(path)), Loc.T("O arquivo fica só no seu computador."), Loc.T("Abrir"), () => AppInfo.OpenShell(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SysMsg.Text = Loc.F("Não foi possível salvar: {0}", ex.Message); }
    }

    /// <summary>Retrato de mentira para o modo demonstração.</summary>
    private static List<InfoSection> DemoSystem()
    {
        var raw = new RawSystem
        {
            OsName = "Microsoft Windows 11 Pro", OsVersion = "23H2", OsBuild = "22631.3737", OsArch = "64 bits", Installed = "2024-03-12", Booted = DateTime.Now.AddHours(-5).ToString("yyyy-MM-dd HH:mm"),
            Activation = 1, SecureBoot = true, Tpm = new RawTpm(true, "2.0", true),
            Cpus = [new RawCpu("Intel(R) Core(TM) i5-1235U", 10, 12, 4400, 1300, 6656, 12288, true)],
            MemSlots = 2, MemMaxBytes = 64L << 30,
            Modules = [new RawModule("DIMM A", "BANK 0", 8L << 30, 3200, 3200, "Samsung", "M471A1K43DB1", "SODIMM", "DDR4"), new RawModule("DIMM B", "BANK 1", 8L << 30, 3200, 3200, "Samsung", "M471A1K43DB1", "SODIMM", "DDR4")],
            BoardMaker = "Acme", BoardProduct = "Notebook 15", BoardVersion = "Rev 1.0", BiosVendor = "Acme", BiosVersion = "F.20", BiosDate = "2024-01-15",
            Gpus = [new RawGpu("Intel(R) Iris(R) Xe Graphics", "31.0.101.4502", "2023-06-01", 128L << 20, "1920x1080 @ 60 Hz")],
            Disks = [new RawDisk("NVMe Acme 512GB", "SSD", "NVMe", 512_110_190_592, "Healthy", 41, 4)], PredictFailure = false,
            Audio = ["Realtek High Definition Audio"], Usb = [Loc.T("Mouse sem fio"), Loc.T("Webcam HD")], Bluetooth = [Loc.T("Fone Bluetooth")],
            Temps = [new RawTemp("ACPI", "TZ00_0", 48)],
        };
        var net = new List<NetAdapter> { new("Wi-Fi", "Intel(R) Wi-Fi 6 AX201", "Wireless80211", "", 866, true, ["192.168.0.12"], "192.168.0.1", ["1.1.1.1"]) };
        var wifi = new WifiInfo("MinhaCasa", 82, 44, SystemInfoBuilder.BandOfChannel(44), "802.11ax (Wi-Fi 6)", 866, 866);
        return SystemInfoBuilder.Build(raw, null, "UEFI", net, wifi, [], "DEMO-PC");
    }
}
