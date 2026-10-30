using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotebookDoctor.Services;

namespace NotebookDoctor;

public static partial class Smoke
{
    public static string? StoreFolderFromArgs(IEnumerable<string> args) =>
        args.FirstOrDefault(a => a.StartsWith("--store-screenshots=", StringComparison.Ordinal)) is { } a ? a[20..].Trim('"') : null;

    /// <summary>
    /// Gera as capturas de tela oficiais de divulgação da Microsoft Store (<c>1920×1080</c> PNG, 16:9) no idioma ativo.
    /// Todas as listas que leriam pastas ou programas reais da máquina são preenchidas com dados sintéticos neutros
    /// e os avisos de modo demonstração/simulação são ocultados para apresentar a interface limpa de produção.
    /// </summary>
    public static async Task RunStoreScreenshotsAsync(MainWindow main, string folder)
    {
        Directory.CreateDirectory(folder);
        try
        {
            main.SizeToContent = SizeToContent.Manual;
            main.Width = 1536;
            main.Height = 864;
            await Idle(800);

            SeedStoreDemoData(main);
            HideDemoChrome(main);

            // 01: Aba Início (Tema Claro, 2 colunas)
            ThemeManager.Apply("light");
            main.SelectTab(AppTabs.Panel);
            await Idle(600);
            main.ApplyPainelLayout(1536);
            HideDemoChrome(main);
            SaveStore1080p(main, folder, "01-inicio-claro");

            // 02: Aba Início (Tema Escuro, 2 colunas)
            ThemeManager.Apply("dark");
            await Idle(600);
            main.ApplyPainelLayout(1536);
            HideDemoChrome(main);
            SaveStore1080p(main, folder, "02-inicio-escuro");

            // 03: Aba Processos (Tema Claro)
            ThemeManager.Apply("light");
            main.SelectTab(AppTabs.Processes);
            await Idle(600);
            HideDemoChrome(main);
            SaveStore1080p(main, folder, "03-processos-claro");

            // 04: Aba Limpeza (Tema Claro, dados sintéticos)
            main.SelectTab(AppTabs.Clean);
            await Idle(600);
            SeedCleanTab(main);
            HideDemoChrome(main);
            SaveStore1080p(main, folder, "04-limpeza-claro");

            // 05: Aba Espaço (Tema Claro, pastas sintéticas sem nome de usuário real)
            main.SelectTab(AppTabs.Space);
            await Idle(600);
            SeedSpaceTab(main);
            HideDemoChrome(main);
            SaveStore1080p(main, folder, "05-espaco-claro");

            // 06: Aba Inicialização (Tema Claro, entradas sintéticas)
            main.SelectTab(AppTabs.Startup);
            await Idle(600);
            SeedStartupTab(main);
            HideDemoChrome(main);
            SaveStore1080p(main, folder, "06-inicializacao-claro");

            // 07: Aba Histórico (Tema Escuro, gráficos de 7 dias)
            ThemeManager.Apply("dark");
            main.SelectTab(AppTabs.History);
            if (main.FindName("PeriodBox") is ComboBox periodBox) periodBox.SelectedIndex = 1;
            await Idle(800);
            HideDemoChrome(main);
            SaveStore1080p(main, folder, "07-historico-escuro");

            // 08: Aba Ajustes & Energia (Tema Claro)
            ThemeManager.Apply("light");
            main.SelectTab(AppTabs.Tweaks);
            await Idle(800);
            SeedTweaksTab(main);
            HideDemoChrome(main);
            SaveStore1080p(main, folder, "08-ajustes-claro");

            // 09: Aba Sistema (Tema Claro, resumo de hardware DemoSystem)
            main.SelectTab(AppTabs.System);
            await Idle(800);
            HideDemoChrome(main);
            SaveStore1080p(main, folder, "09-sistema-claro");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "erro-store-screenshots.txt"), ex.ToString());
            Environment.Exit(1);
            return;
        }
        Environment.Exit(0);
    }

    private static void HideDemoChrome(MainWindow main)
    {
        if (main.FindName("Snack") is UIElement snack) snack.Visibility = Visibility.Collapsed;
        if (main.FindName("SimChip") is UIElement simChip) simChip.Visibility = Visibility.Collapsed;
    }

    private static void SeedStoreDemoData(MainWindow main)
    {
        SeedCleanTab(main);
        SeedSpaceTab(main);
        SeedStartupTab(main);
    }

    private static void SeedCleanTab(MainWindow main)
    {
        var items = new ObservableCollection<CleanItem>
        {
            new() { Label = Loc.T("Temporários do usuário"), Paths = [@"C:\Users\Demo\AppData\Local\Temp"], Selected = true, Bytes = 1_420_000_000L, Files = 1842 },
            new() { Label = Loc.T("Cache de internet do Windows"), Paths = [@"C:\Users\Demo\AppData\Local\Microsoft\Windows\INetCache"], Selected = true, Bytes = 640_000_000L, Files = 910 },
            new() { Label = Loc.T("Relatórios de erro (WER)"), Paths = [@"C:\Users\Demo\AppData\Local\Microsoft\Windows\WER"], Selected = true, Bytes = 215_000_000L, Files = 64 },
            new() { Label = Loc.T("Dumps de falha de aplicativos"), Paths = [@"C:\Users\Demo\AppData\Local\CrashDumps"], Selected = true, Bytes = 185_000_000L, Files = 12 },
            new() { Label = Loc.F("Cache do {0} (feche o navegador antes)", "Google Chrome"), Paths = [@"C:\Users\Demo\AppData\Local\Google\Chrome"], Selected = false, IgnoreAge = true, Bytes = 980_000_000L, Files = 3120 },
            new() { Label = Loc.F("Cache do {0} (feche o navegador antes)", "Microsoft Edge"), Paths = [@"C:\Users\Demo\AppData\Local\Microsoft\Edge"], Selected = false, IgnoreAge = true, Bytes = 410_000_000L, Files = 1250 },
            new() { Label = Loc.T("Miniaturas e ícones do Explorador (recriados quando preciso)"), Paths = [@"C:\Users\Demo\AppData\Local\Microsoft\Windows\Explorer"], Selected = false, IgnoreAge = true, Bytes = 95_000_000L, Files = 18 },
        };
        if (main.FindName("CleanGrid") is DataGrid grid) grid.ItemsSource = items;
        if (main.FindName("CleanHeader") is TextBlock header)
        {
            long sel = items.Where(i => i.Selected).Sum(i => i.Bytes);
            long tot = items.Sum(i => i.Bytes);
            header.Text = Loc.F("{0} marcados para limpeza · {1} no total (temporários com mais de {2} dias; caches inteiros).", Cleaner.FormatBytes(sel), Cleaner.FormatBytes(tot), 7);
        }
        if (main.FindName("CleanMsg") is TextBlock msg) msg.Text = "";
    }

    private static void SeedSpaceTab(MainWindow main)
    {
        var items = new List<FolderUsage>
        {
            new() { Name = "Downloads", Path = @"C:\Users\Demo\Downloads", Bytes = 28_400_000_000L, Percent = 100 },
            new() { Name = "AppData", Path = @"C:\Users\Demo\AppData", Bytes = 19_200_000_000L, Percent = 67.6 },
            new() { Name = "Videos", Path = @"C:\Users\Demo\Videos", Bytes = 14_800_000_000L, Percent = 52.1 },
            new() { Name = "Documents", Path = @"C:\Users\Demo\Documents", Bytes = 9_600_000_000L, Percent = 33.8 },
            new() { Name = "Pictures", Path = @"C:\Users\Demo\Pictures", Bytes = 6_300_000_000L, Percent = 22.2 },
            new() { Name = "Desktop", Path = @"C:\Users\Demo\Desktop", Bytes = 2_100_000_000L, Percent = 7.4 },
            new() { Name = "Music", Path = @"C:\Users\Demo\Music", Bytes = 1_450_000_000L, Percent = 5.1 },
        };
        if (main.FindName("SpaceGrid") is DataGrid grid) grid.ItemsSource = items;
        if (main.FindName("SpaceMsg") is TextBlock msg)
            msg.Text = Loc.F("Pronto: {0} itens em {1} · total {2}. Só leitura: nada foi apagado.", items.Count, @"C:\Users\Demo", Cleaner.FormatBytes(items.Sum(x => x.Bytes)));
    }

    private static void SeedStartupTab(MainWindow main)
    {
        var items = new List<StartupEntry>
        {
            new() { Name = "Microsoft Teams", Command = @"""C:\Program Files\WindowsApps\MSTeams\ms-teams.exe"" --system-initiated", Origin = "Usuário (registro)", Enabled = true, Impact = "Alto (42 s de CPU, 640 MB)", ImpactText = Loc.F("{0} ({1:0} s de CPU, {2} MB)", Loc.T("Alto"), 42, 640), BootText = "3,4 s", BatteryText = Loc.F("{0:0}% da CPU", 14), Publisher = "Microsoft Corporation" },
            new() { Name = "Spotify", Command = @"""C:\Users\Demo\AppData\Roaming\Spotify\Spotify.exe"" --minimized", Origin = "Usuário (registro)", Enabled = true, Impact = "Médio (12 s de CPU, 280 MB)", ImpactText = Loc.F("{0} ({1:0} s de CPU, {2} MB)", Loc.T("Médio"), 12, 280), BootText = "1,8 s", BatteryText = Loc.F("{0:0}% da CPU", 6), Publisher = "Spotify AB" },
            new() { Name = "Microsoft OneDrive", Command = @"""C:\Program Files\Microsoft OneDrive\OneDrive.exe"" /background", Origin = "Usuário (registro)", Enabled = true, Impact = "Baixo (3 s de CPU, 110 MB)", ImpactText = Loc.F("{0} ({1:0} s de CPU, {2} MB)", Loc.T("Baixo"), 3, 110), BootText = "0,9 s", BatteryText = Loc.F("{0:0}% da CPU", 2), Publisher = "Microsoft Corporation" },
            new() { Name = "Discord", Command = @"""C:\Users\Demo\AppData\Local\Discord\Update.exe"" --processStart Discord.exe", Origin = "Usuário (registro)", Enabled = false, Delayed = true, Impact = "Não está em execução", ImpactText = Loc.T("Não está em execução"), BootText = "2,6 s", Publisher = "Discord Inc." },
            new() { Name = "Steam Client Bootstrapper", Command = @"""C:\Program Files (x86)\Steam\steam.exe"" -silent", Origin = "Usuário (registro)", Enabled = false, Impact = "Não está em execução", ImpactText = Loc.T("Não está em execução"), BootText = "4,1 s", Publisher = "Valve Corp." },
            new() { Name = "Windows Security notification icon", Command = @"%windir%\system32\SecurityHealthSystray.exe", Origin = "Sistema (registro)", Enabled = true, Impact = "Baixo (1 s de CPU, 28 MB)", ImpactText = Loc.F("{0} ({1:0} s de CPU, {2} MB)", Loc.T("Baixo"), 1, 28), Publisher = "Microsoft Windows" },
        };
        if (main.FindName("StartupGrid") is DataGrid grid) grid.ItemsSource = items;
        if (main.FindName("StartupMsg") is TextBlock msg) msg.Text = Loc.F("{0} ativos de {1}", items.Count(x => x.Enabled), items.Count);
    }

    private static void SeedTweaksTab(MainWindow main)
    {
        var tweaks = new List<Tweak>
        {
            new("hibernate", "Hibernação", Loc.F("Ligada · hiberfil.sys ocupa {0}", Cleaner.FormatBytes(6_400_000_000L)),
                Loc.T("O Windows reserva no disco um arquivo do tamanho de boa parte da RAM para poder hibernar. Quem só usa Suspender não precisa dele; desligar devolve esse espaço. Desligar também desativa a Inicialização Rápida."),
                "Desligar hibernação", "", NeedsAdmin: true, Recommended: true, CanApply: true),
            new("faststartup", "Inicialização Rápida", Loc.F("Ligada · o Windows está há {0:0.#} dias sem reiniciar de verdade", 4.2),
                Loc.T("Com ela, \"Desligar\" na verdade hiberna o núcleo do Windows: o boot fica alguns segundos mais rápido, mas problemas e memória acumulada não são zerados, e drivers e atualizações às vezes não se aplicam. Desligar faz cada desligamento ser completo."),
                "Desligar Inicialização Rápida", "", NeedsAdmin: true, Recommended: true, CanApply: true),
            new("pagefile", "Arquivo de paginação (memória virtual)", Loc.F("Gerenciado pelo Windows · {0} agora", Cleaner.FormatBytes(2_560_000_000L)),
                Loc.F("Quando a RAM ({0}) enche, o Windows usa o disco como extensão. Sem ele, ou com tamanho fixo pequeno, programas podem fechar sozinhos por falta de memória. O recomendado é deixar o Windows gerenciar o tamanho.", Units.Gb(16, "0")),
                "Já recomendado", "", NeedsAdmin: true, Recommended: false, CanApply: false),
            new("visualfx", "Efeitos visuais", Loc.F("{0} de {1} efeitos ligados (animações, sombras, transparência)", 9, 9),
                Loc.T("Animações, sombras e transparência usam a placa de vídeo integrada e a CPU. Em notebooks mais simples, desligar deixa a interface mais ágil. O texto continua suavizado."),
                "Usar melhor desempenho", "", NeedsAdmin: false, Recommended: false, CanApply: true),
        };
        if (main.FindName("TweaksList") is ItemsControl list) list.ItemsSource = tweaks;
    }

    /// <summary>
    /// Desenha o conteúdo da janela em resolução lógica <c>1536×864</c> (16:9) escalada vetorialmente para exatos <c>1920×1080</c> pixels (1,25x),
    /// independentemente do tamanho ou DPI do monitor da máquina onde o gerador roda.
    /// </summary>
    private static void SaveStore1080p(MainWindow w, string folder, string name)
    {
        const double logicalW = 1536, logicalH = 864;
        const int targetW = 1920, targetH = 1080;

        if (w.Content is not FrameworkElement content) throw new InvalidOperationException($"{name}: janela sem conteúdo");
        var root = VisualTreeHelper.GetChildrenCount(w) > 0 && VisualTreeHelper.GetChild(w, 0) is FrameworkElement client ? client : content;

        root.Measure(new Size(logicalW, logicalH));
        root.Arrange(new Rect(0, 0, logicalW, logicalH));
        root.UpdateLayout();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = Application.Current.TryFindResource("WindowBackdrop") as Brush ?? Brushes.White;
            dc.DrawRectangle(bg, null, new Rect(0, 0, targetW, targetH));
            var brush = new VisualBrush(root)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, logicalW, logicalH),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, targetW, targetH));
        }

        var bmp = new RenderTargetBitmap(targetW, targetH, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(folder, name + ".png"));
        png.Save(fs);
    }
}

