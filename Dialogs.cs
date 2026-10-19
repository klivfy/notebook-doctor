using System.Windows;
using System.Windows.Controls;

namespace NotebookDoctor;

/// <summary>Diálogos Material 3 (substituem o MessageBox do Windows, que ignora o tema).</summary>
public static class Dialogs
{
    public static bool Confirm(Window owner, string title, string message, string confirmText = "Confirmar", bool danger = false) =>
        Show(owner, title, message, confirmText, "Cancelar", danger);

    public static void Info(Window owner, string title, string message) =>
        Show(owner, title, message, "OK", null, false);

    /// <summary>M14: boas-vindas na primeira execução, com as escolhas principais já à mão.</summary>
    public static void ShowWelcome(Window owner, Services.AppSettings s)
    {
        var dlg = NewWindow(owner, "Bem-vindo ao Notebook Doctor", 520);
        var heading = Heading("Bem-vindo ao Notebook Doctor", "FsTitleLarge", new Thickness(0, 0, 0, 8));
        var intro = new TextBlock
        {
            Text = "O app acompanha CPU, memória e disco, explica o que deixa o notebook lento e corrige com segurança. " +
                   "Nada é apagado ou encerrado sem a sua confirmação, e as mudanças de sistema podem ser desfeitas na seção Ações. " +
                   "O tema segue o Windows; dá para trocar depois em Configurações.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");

        var start = new CheckBox { Content = "Iniciar com o Windows (fica discreto na bandeja)", IsChecked = true };
        var alerts = new CheckBox { Content = "Avisar quando algo pesar demais", IsChecked = s.AlertsEnabled };
        var weekly = new CheckBox { Content = "Limpar temporários toda semana, com o PC parado", IsChecked = true };

        var go = new Button { Content = "Começar", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), IsDefault = true };
        go.Click += (_, _) => dlg.Close();
        dlg.Content = new StackPanel { Margin = new Thickness(24), Children = { heading, intro, start, alerts, weekly, go } };
        dlg.ShowDialog();

        s.StartWithWindows = start.IsChecked == true;
        s.AlertsEnabled = alerts.IsChecked == true;
        s.WeeklyCleanup = weekly.IsChecked == true;
        s.FirstRunDone = true;
        s.Save();
        s.ApplyStartWithWindows();
    }

    /// <summary>A47: cada processo de um programa, com PID, memória, CPU, threads, início e caminho.</summary>
    public static void ShowProcessDetails(Window owner, string name)
    {
        var rows = new List<object>();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            using (p)
            {
                string Try(Func<string> f) { try { return f(); } catch { return "—"; } }
                rows.Add(new
                {
                    PID = p.Id,
                    RAM = $"{p.WorkingSet64 / 1048576:N0} MB",
                    CPU = Try(() => $"{p.TotalProcessorTime.TotalSeconds:N0} s"),
                    Threads = Try(() => p.Threads.Count.ToString()),
                    Handles = Try(() => p.HandleCount.ToString()),
                    Inicio = Try(() => p.StartTime.ToString("dd/MM HH:mm")),
                    Prioridade = Try(() => p.PriorityClass switch
                    {
                        System.Diagnostics.ProcessPriorityClass.BelowNormal => "Abaixo do normal",
                        System.Diagnostics.ProcessPriorityClass.Normal => "Normal",
                        System.Diagnostics.ProcessPriorityClass.AboveNormal => "Acima do normal",
                        System.Diagnostics.ProcessPriorityClass.High => "Alta",
                        System.Diagnostics.ProcessPriorityClass.Idle => "Ociosa",
                        var x => x.ToString(),
                    }),
                    Caminho = Try(() => p.MainModule?.FileName ?? "—"),
                });
            }
        var about = Services.ProcessKnowledge.Describe(name);
        var dlg = NewWindow(owner, $"Detalhes: {name}", 900);
        dlg.SizeToContent = SizeToContent.Manual;
        dlg.Height = 520;
        dlg.ResizeMode = ResizeMode.CanResize;
        var heading = Heading($"{name} · {Services.Pt.N(rows.Count, "processo", "processos")}", "FsTitle", new Thickness(0, 0, 0, 4));
        var sub = new TextBlock { Text = $"{about.Description} · Fechar: {about.SafetyText}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = true };
        var close = new Button { Content = "Fechar", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), IsCancel = true };
        close.Click += (_, _) => dlg.Close();
        var root = new DockPanel { Margin = new Thickness(24) };
        DockPanel.SetDock(heading, Dock.Top); DockPanel.SetDock(sub, Dock.Top); DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(heading); root.Children.Add(sub); root.Children.Add(close); root.Children.Add(grid);
        dlg.Content = root;
        dlg.ShowDialog();
    }

    private static Window NewWindow(Window owner, string title, double width)
    {
        var dlg = new Window
        {
            Title = title, Owner = owner, SizeToContent = SizeToContent.Height, Width = width,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        Theme(dlg);
        return dlg;
    }

    /// <summary>Cores, fonte e tamanho de texto do tema (segue o "Tamanho do texto" do Windows).</summary>
    private static void Theme(Window dlg)
    {
        dlg.SetResourceReference(Control.BackgroundProperty, "SurfaceContainer");
        dlg.SetResourceReference(TextBlock.ForegroundProperty, "OnSurface");
        dlg.SetResourceReference(Control.FontFamilyProperty, "FontBody");
        dlg.SetResourceReference(Control.FontSizeProperty, "FsBody");
        dlg.Language = System.Windows.Markup.XmlLanguage.GetLanguage("pt-BR");
        dlg.SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(dlg);
    }

    private static TextBlock Heading(string text, string sizeKey, Thickness margin)
    {
        var t = new TextBlock { Text = text, Margin = margin, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontMedium");
        t.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(t, System.Windows.Automation.AutomationHeadingLevel.Level1);
        return t;
    }

    private static bool Show(Window owner, string title, string message, string ok, string? cancel, bool danger)
    {
        var dlg = new Window
        {
            Title = title,
            Owner = owner,
            SizeToContent = SizeToContent.Height,
            Width = 440,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        Theme(dlg);

        var heading = Heading(title, "FsTitleLarge", new Thickness(0, 0, 0, 12));
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 24) };
        body.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");

        bool result = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (cancel is not null)
        {
            var c = new Button { Content = cancel, IsCancel = true };
            c.SetResourceReference(FrameworkElement.StyleProperty, "TextButton");
            buttons.Children.Add(c);
        }
        var o = new Button { Content = ok, IsDefault = cancel is null, Margin = new Thickness(0) };
        if (danger) o.SetResourceReference(FrameworkElement.StyleProperty, "DangerButton");
        o.Click += (_, _) => { result = true; dlg.Close(); };
        buttons.Children.Add(o);

        dlg.Content = new StackPanel { Margin = new Thickness(24), Children = { heading, body, buttons } };
        dlg.ShowDialog();
        return result;
    }
}
