using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// Verificação visual automática (<c>--demo --smoke=pasta</c>), usada pela CI no Windows. Abre cada aba e cada janela nos temas claro e
/// escuro, salva uma imagem PNG de cada uma e anota qualquer erro (recurso de estilo que falta, modelo quebrado). Termina o app com código
/// 0 se tudo abriu e 1 se algo falhou; o relatório fica em <c>smoke.txt</c> na mesma pasta.
/// </summary>
public static class Smoke
{
    public static string? FolderFromArgs(IEnumerable<string> args) =>
        args.FirstOrDefault(a => a.StartsWith("--smoke=", StringComparison.Ordinal)) is { } a ? a[8..].Trim('"') : null;

    private static readonly List<string> Errors = [];
    private static readonly StringBuilder Log = new();

    public static async Task RunAsync(MainWindow main, string folder)
    {
        Directory.CreateDirectory(folder);
        // Erros de interface que o app normalmente engole também contam como falha aqui
        Application.Current.DispatcherUnhandledException += (_, e) => { Errors.Add("Exceção na tela: " + e.Exception); e.Handled = true; };
        // Avisos de ligação (binding) quebrada não derrubam a tela, mas vão para o relatório
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingWarnings());
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Error;
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                ThemeManager.Apply(theme);
                await Idle(600);
                for (int tab = 0; tab <= AppTabs.Settings; tab++)
                {
                    await Step($"{theme}-aba-{tab:00}", async () =>
                    {
                        main.SelectTab(tab);
                        await Idle(500);
                        Save(main, folder, $"{theme}-aba-{tab:00}");
                    });
                }
                foreach (var (name, create) in Tools(main)) await Tool(theme, folder, name, create);
                // Controles em estados que as telas de demonstração não mostram sozinhas
                await Tool(theme, folder, "controles", () => Controls(main));
                await Modal(theme, folder, "dialogo-info", () => Dialogs.Info(main, Loc.T("Informação"), Loc.T("Texto de exemplo de um aviso do app, com duas linhas para conferir a quebra.")));
                await Modal(theme, folder, "dialogo-perigo", () => Dialogs.Confirm(main, Loc.T("Apagar"), Loc.T("Confirmação de uma ação que não pode ser desfeita."), Loc.T("Apagar"), danger: true));
                await Modal(theme, folder, "boas-vindas", () => Dialogs.ShowWelcome(main, App.Current.Settings));
            }

            // Monitor largo (1360 × 800): o Início passa a duas colunas, com o Diagnóstico ao lado. Todas as abas abrem nesse tamanho
            // nos dois temas, e a troca de coluna é conferida (não depende do tamanho da tela da CI)
            main.SizeToContent = SizeToContent.Manual;
            main.Width = 1360; main.Height = 800;
            foreach (var theme in new[] { "light", "dark" })
            {
                ThemeManager.Apply(theme);
                await Idle(600);
                for (int tab = 0; tab <= AppTabs.Settings; tab++)
                {
                    await Step($"{theme}-aba-{tab:00}-largo", async () =>
                    {
                        main.SelectTab(tab);
                        await Idle(500);
                        if (tab == 0 && main.ActualWidth >= 1200 && !main.PainelTwoColumns)
                            Errors.Add($"Início: janela com {main.ActualWidth:0} px e o Diagnóstico não foi para a coluna ao lado");
                        Save(main, folder, $"{theme}-aba-{tab:00}-largo");
                    });
                }
            }

            // B46: notebook 1366 × 768 com escala de 150% (a mais comum nessa tela) deixa cerca de 911 × 480 para as janelas.
            // Cada aba e cada ferramenta abre nesse tamanho, e botão que fica fora da janela vira aviso.
            ThemeManager.Apply("light");
            await Idle(600);
            Shrink(main);
            for (int tab = 0; tab <= AppTabs.Settings; tab++)
            {
                await Step($"light-aba-{tab:00}-150", async () =>
                {
                    main.SelectTab(tab);
                    await Idle(500);
                    Save(main, folder, $"light-aba-{tab:00}-150");
                });
            }
            foreach (var (name, create) in Tools(main)) await Tool("light", folder, name + "-150", create, small: true);

            // B46: "Tamanho do texto" do Windows em 150% e 200%, no mesmo notebook pequeno (o pior caso). Texto cortado ou botão
            // empurrado para fora vira aviso, como acima
            foreach (int pct in new[] { 150, 200 })
            {
                TextScale.Force(pct / 100.0);
                await Idle(400);
                for (int tab = 0; tab <= AppTabs.Settings; tab++)
                {
                    await Step($"light-aba-{tab:00}-texto{pct}", async () =>
                    {
                        main.SelectTab(tab);
                        await Idle(500);
                        Save(main, folder, $"light-aba-{tab:00}-texto{pct}");
                    });
                }
                foreach (var (name, create) in Tools(main)) await Tool("light", folder, $"{name}-texto{pct}", create, small: true);
            }
            TextScale.Force(1);
        }
        catch (Exception ex) { Errors.Add("Falha geral: " + ex); }

        Log.AppendLine(Errors.Count == 0 ? "OK: todas as telas abriram." : $"FALHAS: {Errors.Count}");
        foreach (var e in Errors) Log.AppendLine(e).AppendLine();
        File.WriteAllText(Path.Combine(folder, "smoke.txt"), Log.ToString(), new UTF8Encoding(false));
        Console.Out.Write(Log.ToString());
        Environment.Exit(Errors.Count == 0 ? 0 : 1);
    }

    /// <summary>
    /// Com o app em outro idioma, texto visível que ainda é uma chave em português é tradução que faltou aplicar; e, em
    /// inglês, texto com cara de português (acentos ou palavras como "de", "com", "não") é texto montado no código que
    /// não passou pela tradução (U1).
    /// </summary>
    private static void ReportUntranslated(Window w, string name)
    {
        if (Loc.IsSource) return;
        var seen = new HashSet<string>();
        void Walk(DependencyObject d)
        {
            if (d is System.Windows.Controls.TextBlock { IsVisible: true } tb && tb.Text is { Length: > 1 } t && seen.Add(t))
            {
                if (Loc.TryT(t, out var en) && en != t) Log.AppendLine($"aviso [{name}] sem tradução: {t}");
                else if (Loc.Language == "en" && LooksPortuguese(t)) Log.AppendLine($"aviso [{name}] parece português: {t}");
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(w);
    }

    /// <summary>
    /// B46: botão que ficou fora da área da janela (empurrado por linhas de tamanho fixo quando a janela encolhe) não dá para clicar. O que
    /// está dentro de uma área com rolagem fica de fora: chega-se a ele rolando.
    /// </summary>
    private static void ReportClipped(FrameworkElement root, string name)
    {
        var area = new Rect(-1, -1, root.ActualWidth + 2, root.ActualHeight + 2);
        foreach (var b in Descendants(root).OfType<System.Windows.Controls.Primitives.ButtonBase>())
        {
            if (!b.IsVisible || b.ActualWidth < 1 || b.ActualHeight < 1 || InsideScroll(b, root)) continue;
            var r = b.TransformToAncestor(root).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight));
            if (!area.Contains(r))
                Log.AppendLine($"aviso [{name}] botão fora da janela: {b.Content as string ?? System.Windows.Automation.AutomationProperties.GetName(b) ?? b.Name}");
        }
        // B46: item de lista (como as seções do trilho) cortado pela janela, sem rolagem que o alcance
        foreach (var item in Descendants(root).OfType<System.Windows.Controls.ListBoxItem>())
        {
            if (!item.IsVisible || item.ActualHeight < 1 || InsideScroll(item, root)) continue;
            var r = item.TransformToAncestor(root).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
            if (!area.Contains(r)) Log.AppendLine($"aviso [{name}] item fora da janela: {item.Content as string ?? item.Name}");
        }
        // Lista espremida pelo resto da janela: aparece, mas não mostra nem uma linha
        foreach (var list in Descendants(root).OfType<System.Windows.Controls.Primitives.Selector>()
                     .Where(l => l is System.Windows.Controls.DataGrid or System.Windows.Controls.ListBox))
        {
            // Lista vazia só com o cabeçalho (≈ 40 px) é estado normal; lista com itens e sem espaço para um deles, não
            if (list.IsVisible && list.ActualHeight < MinListHeight && (list.Items.Count > 0 || list.ActualHeight < 24))
                Log.AppendLine($"aviso [{name}] lista com {list.ActualHeight:0} px: {System.Windows.Automation.AutomationProperties.GetName(list) ?? list.Name}");
        }
    }

    /// <summary>Cabeçalho mais uma linha de tabela.</summary>
    private const double MinListHeight = 48;

    private static bool InsideScroll(DependencyObject d, DependencyObject root)
    {
        for (var p = VisualTreeHelper.GetParent(d); p is not null && p != root; p = VisualTreeHelper.GetParent(p))
            if (p is System.Windows.Controls.ScrollViewer) return true;
        return false;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject d)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex Portuguese = new(
        @"[ãõçáéíóúâêôàÃÕÇÁÉÍÓÚÂÊÔ]|\b(de|da|dos|das|sem|não|uma|pode|ligado|arquivos?|pasta|programas?|limpeza)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Caminhos ficam de fora: são dados da pessoa, não textos do app. ("do" e "com" ficam de fora por existirem em inglês e em endereços.)</summary>
    private static bool LooksPortuguese(string t) => !t.Contains(":\\") && Portuguese.IsMatch(t);

    private sealed class BindingWarnings : System.Diagnostics.TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (message is not null) Log.AppendLine("aviso " + message); }
    }

    private static async Task Step(string name, Func<Task> action)
    {
        try { await action(); Log.AppendLine("ok    " + name); }
        catch (Exception ex) { Errors.Add($"{name}: {ex}"); Log.AppendLine("ERRO  " + name); }
    }

    /// <summary>Uma folha com os controles em cada estado: botões, caixas, campos e barras (inclusive a de andamento sem fim).</summary>
    private static Window Controls(Window owner)
    {
        var w = new Window { Owner = owner, Title = "Controles", Width = 720, Height = 520, ShowInTaskbar = false };
        w.SetResourceReference(Window.BackgroundProperty, "WindowBackdrop");
        w.SetResourceReference(Window.FontFamilyProperty, "FontBody");
        w.SetResourceReference(Window.FontSizeProperty, "FsBody");
        w.SetResourceReference(Window.ForegroundProperty, "TextPrimary");
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(24) };
        System.Windows.Controls.WrapPanel Row(params UIElement[] items)
        {
            var r = new System.Windows.Controls.WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var i in items) r.Children.Add(i);
            return r;
        }
        System.Windows.Controls.Button Btn(string text, string? style = null, bool enabled = true)
        {
            var b = new System.Windows.Controls.Button { Content = text, IsEnabled = enabled };
            if (style is not null) b.SetResourceReference(FrameworkElement.StyleProperty, style);
            return b;
        }
        panel.Children.Add(Row(Btn("Destaque"), Btn("Padrão", "TonalButton"), Btn("Sutil", "TextButton"), Btn("Perigo", "DangerButton"), Btn("Desligado", null, false)));
        panel.Children.Add(Row(new System.Windows.Controls.CheckBox { Content = "Marcada", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) },
            new System.Windows.Controls.CheckBox { Content = "Desmarcada" }));
        panel.Children.Add(Row(new System.Windows.Controls.TextBox { Width = 260, Tag = Loc.T("Texto de ajuda"), Margin = new Thickness(0, 0, 12, 0) },
            new System.Windows.Controls.ComboBox { Width = 200, ItemsSource = new[] { Loc.T("Primeira opção"), Loc.T("Segunda") }, SelectedIndex = 0 }));
        panel.Children.Add(new System.Windows.Controls.ProgressBar { Value = 62, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new System.Windows.Controls.ProgressBar { IsIndeterminate = true });
        w.Content = panel;
        return w;
    }

    private static IEnumerable<(string Name, Func<Window> Create)> Tools(MainWindow main) =>
    [
        ("limpeza-avancada", () => new AdvancedCleanWindow(main)),
        ("menu-de-contexto", () => new ContextMenuWindow(main)),
        ("desfragmentar", () => new DefragWindow(main)),
        ("drivers", () => new DriversWindow(main)),
        ("drivers-fabricante", () => new DriversWindow(main, oem: true)),
        ("duplicados", () => new DuplicatesWindow(main, [])),
        ("recuperar-ntfs", () => new NtfsRecoveryWindow(main)),
        ("recuperar", () => new RecoveryWindow(main)),
        ("registro", () => new RegistryCheckWindow(main)),
        ("apagar-de-vez", () => new SecureEraseWindow(main)),
        ("soneca", () => new SnoozeWindow(main)),
        ("atualizar-programas", () => new UpdatesWindow(main)),
        ("ferramentas-do-windows", () => new WindowsToolsWindow(main)),
        ("verificar-malware", () => new SecurityWindow(main)),
    ];

    private const double SmallWidth = 911, SmallHeight = 480;

    /// <summary>Encolhe a janela até a área útil do notebook pequeno, como o WindowFit faz no PC de verdade.</summary>
    private static void Shrink(Window w)
    {
        w.SizeToContent = SizeToContent.Manual;
        w.MinWidth = Math.Min(w.MinWidth, SmallWidth);
        w.MinHeight = Math.Min(w.MinHeight, SmallHeight);
        w.Width = Math.Min(w.ActualWidth > 0 ? w.ActualWidth : w.Width, SmallWidth);
        w.Height = Math.Min(w.ActualHeight > 0 ? w.ActualHeight : w.Height, SmallHeight);
    }

    private static Task Tool(string theme, string folder, string name, Func<Window> create, bool small = false) => Step($"{theme}-{name}", async () =>
    {
        var w = create();
        w.ShowActivated = false;
        w.Show();
        await Idle(300);
        if (small) Shrink(w);
        await Idle(600);
        Save(w, folder, $"{theme}-{name}");
        w.Hide(); // Hide, e não Close: janelas ocupadas recusam fechar, e o processo termina logo depois
    });

    /// <summary>Diálogos modais: um temporizador fotografa e fecha a janela nova enquanto o ShowDialog espera.</summary>
    private static Task Modal(string theme, string folder, string name, Action open) => Step($"{theme}-{name}", async () =>
    {
        var before = Application.Current.Windows.OfType<Window>().ToHashSet();
        Exception? failure = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        timer.Tick += (_, _) =>
        {
            var dlg = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => !before.Contains(w) && w.IsVisible);
            if (dlg is null) return;
            timer.Stop();
            try { Save(dlg, folder, $"{theme}-{name}"); } catch (Exception ex) { failure = ex; }
            dlg.Close();
        };
        timer.Start();
        open();
        timer.Stop();
        await Idle(200);
        if (failure is not null) throw failure;
    });

    private static async Task Idle(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Desenha a janela inteira (o conteúdo, sobre a cor de fundo do tema) num PNG do tamanho dela.</summary>
    private static void Save(Window w, string folder, string name)
    {
        w.UpdateLayout();
        // A área cliente inteira (o primeiro elemento do modelo da janela), e não só o conteúdo: o conteúdo costuma ter margem, e
        // desenhá-lo pelo tamanho dele cortava a margem da direita e de baixo da imagem
        if (w.Content is not FrameworkElement content || content.ActualWidth < 1) throw new InvalidOperationException($"{name}: janela sem conteúdo");
        var root = VisualTreeHelper.GetChildrenCount(w) > 0 && VisualTreeHelper.GetChild(w, 0) is FrameworkElement client && client.ActualWidth >= 1 ? client : content;
        double dpi = VisualTreeHelper.GetDpi(w).PixelsPerInchX;
        double scale = dpi / 96;
        int width = (int)Math.Ceiling(root.ActualWidth * scale), height = (int)Math.Ceiling(root.ActualHeight * scale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = Application.Current.TryFindResource("WindowBackdrop") as Brush ?? Brushes.White;
            dc.DrawRectangle(bg, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            // Sem esticar: o pincel mostra a área do conteúdo em tamanho real, a partir do canto
            var brush = new VisualBrush(root)
            {
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, root.ActualWidth, root.ActualHeight),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        var bmp = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(visual);
        ReportUntranslated(w, name);
        ReportClipped(root, name);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(folder, name + ".png"));
        png.Save(fs);
    }
}
