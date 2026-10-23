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
                await Tool(theme, folder, "limpeza-avancada", () => new AdvancedCleanWindow(main));
                await Tool(theme, folder, "menu-de-contexto", () => new ContextMenuWindow(main));
                await Tool(theme, folder, "desfragmentar", () => new DefragWindow(main));
                await Tool(theme, folder, "drivers", () => new DriversWindow(main));
                await Tool(theme, folder, "duplicados", () => new DuplicatesWindow(main, []));
                await Tool(theme, folder, "recuperar-ntfs", () => new NtfsRecoveryWindow(main));
                await Tool(theme, folder, "recuperar", () => new RecoveryWindow(main));
                await Tool(theme, folder, "registro", () => new RegistryCheckWindow(main));
                await Tool(theme, folder, "apagar-de-vez", () => new SecureEraseWindow(main));
                await Tool(theme, folder, "soneca", () => new SnoozeWindow(main));
                await Tool(theme, folder, "atualizar-programas", () => new UpdatesWindow(main));
                await Tool(theme, folder, "ferramentas-do-windows", () => new WindowsToolsWindow(main));
                // Controles em estados que as telas de demonstração não mostram sozinhas
                await Tool(theme, folder, "controles", () => Controls(main));
                await Modal(theme, folder, "dialogo-info", () => Dialogs.Info(main, "Informação", "Texto de exemplo de um aviso do app, com duas linhas para conferir a quebra."));
                await Modal(theme, folder, "dialogo-perigo", () => Dialogs.Confirm(main, "Apagar", "Confirmação de uma ação que não pode ser desfeita.", "Apagar", danger: true));
                await Modal(theme, folder, "boas-vindas", () => Dialogs.ShowWelcome(main, App.Current.Settings));
            }
        }
        catch (Exception ex) { Errors.Add("Falha geral: " + ex); }

        Log.AppendLine(Errors.Count == 0 ? "OK: todas as telas abriram." : $"FALHAS: {Errors.Count}");
        foreach (var e in Errors) Log.AppendLine(e).AppendLine();
        File.WriteAllText(Path.Combine(folder, "smoke.txt"), Log.ToString(), new UTF8Encoding(false));
        Console.Out.Write(Log.ToString());
        Environment.Exit(Errors.Count == 0 ? 0 : 1);
    }

    /// <summary>Com o app em outro idioma, texto visível que ainda é uma chave em português é tradução que faltou aplicar.</summary>
    private static void ReportUntranslated(Window w, string name)
    {
        if (Loc.IsSource) return;
        var seen = new HashSet<string>();
        void Walk(DependencyObject d)
        {
            if (d is System.Windows.Controls.TextBlock { IsVisible: true } tb && tb.Text is { Length: > 1 } t && Loc.TryT(t, out var en) && en != t && seen.Add(t))
                Log.AppendLine($"aviso [{name}] sem tradução: {t}");
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i));
        }
        Walk(w);
    }

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

    /// <summary>Uma folha com os controles em cada estado: botões, caixas, interruptor, campos e barras (inclusive a de andamento sem fim).</summary>
    private static Window Controls(Window owner)
    {
        var w = new Window { Owner = owner, Title = "Controles", Width = 720, Height = 520, ShowInTaskbar = false };
        w.SetResourceReference(Window.BackgroundProperty, "Surface");
        w.SetResourceReference(Window.FontFamilyProperty, "FontBody");
        w.SetResourceReference(Window.FontSizeProperty, "FsBody");
        w.SetResourceReference(Window.ForegroundProperty, "OnSurface");
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
        var sw = new System.Windows.Controls.CheckBox { Content = "Interruptor ligado", IsChecked = true, Width = 260 };
        sw.SetResourceReference(FrameworkElement.StyleProperty, "ToggleSwitch");
        var swOff = new System.Windows.Controls.CheckBox { Content = "Interruptor desligado", Width = 260 };
        swOff.SetResourceReference(FrameworkElement.StyleProperty, "ToggleSwitch");
        panel.Children.Add(Row(new System.Windows.Controls.CheckBox { Content = "Marcada", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) },
            new System.Windows.Controls.CheckBox { Content = "Desmarcada" }));
        panel.Children.Add(Row(sw, swOff));
        panel.Children.Add(Row(new System.Windows.Controls.TextBox { Width = 260, Tag = "Texto de ajuda", Margin = new Thickness(0, 0, 12, 0) },
            new System.Windows.Controls.ComboBox { Width = 200, ItemsSource = new[] { "Primeira opção", "Segunda" }, SelectedIndex = 0 }));
        panel.Children.Add(new System.Windows.Controls.ProgressBar { Value = 62, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new System.Windows.Controls.ProgressBar { IsIndeterminate = true });
        w.Content = panel;
        return w;
    }

    private static Task Tool(string theme, string folder, string name, Func<Window> create) => Step($"{theme}-{name}", async () =>
    {
        var w = create();
        w.ShowActivated = false;
        w.Show();
        await Idle(900);
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
        if (w.Content is not FrameworkElement root || root.ActualWidth < 1) throw new InvalidOperationException($"{name}: janela sem conteúdo");
        double dpi = VisualTreeHelper.GetDpi(w).PixelsPerInchX;
        double scale = dpi / 96;
        int width = (int)Math.Ceiling(root.ActualWidth * scale), height = (int)Math.Ceiling(root.ActualHeight * scale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bg = Application.Current.TryFindResource("Surface") as Brush ?? Brushes.White;
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
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(folder, name + ".png"));
        png.Save(fs);
    }
}
