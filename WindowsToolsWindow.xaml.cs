using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Leva 8.4 (R16): painel de atalhos para as ferramentas do Windows. Só abre; não altera nada.</summary>
public partial class WindowsToolsWindow : Window
{
    public WindowsToolsWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Build();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string System32 => Environment.SystemDirectory;

    private void Build()
    {
        string dir = System32;
        var tools = WindowsTools.Available(f => File.Exists(Path.Combine(dir, f)) || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), f))).Where(WindowsTools.IsSafe);
        foreach (var group in WindowsTools.Grouped(tools))
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = Loc.T(group.Key), Style = (Style)FindResource("TitleMedium"), Margin = new Thickness(0, 0, 0, 8) });
            Host.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = card });
            var panel = new WrapPanel();
            card.Children.Add(panel);
            foreach (var tool in group)
            {
                var button = new Button
                {
                    Content = Loc.T(tool.Name), ToolTip = Loc.T(tool.Description), Style = (Style)FindResource("TonalButton"), Margin = new Thickness(0, 0, 8, 8),
                    Tag = tool,
                };
                System.Windows.Automation.AutomationProperties.SetHelpText(button, Loc.T(tool.Description));
                button.Click += Open_Click;
                panel.Children.Add(button);
            }
        }
        if (Host.Children.Count == 0) Msg.Text = Loc.T("Nenhuma ferramenta encontrada neste computador.");
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WindowsTool tool } || !WindowsTools.IsSafe(tool)) return;
        try
        {
            var psi = new ProcessStartInfo(tool.File, tool.Arguments) { UseShellExecute = true };
            using var _ = Process.Start(psi);
            Msg.Text = Loc.F("Abrindo: {0}", Loc.T(tool.Name));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // 1223 = pedido de permissão recusado pela pessoa: não é erro
            Msg.Text = ex is Win32Exception { NativeErrorCode: 1223 } ? Loc.T("Permissão não concedida.") : Loc.F("Não consegui abrir {0}.", Loc.T(tool.Name));
        }
    }
}
