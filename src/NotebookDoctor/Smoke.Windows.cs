using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// Prioridade 6: verificações que dependem de um Windows de verdade e rodam no job Windows da CI junto com a verificação visual.
/// Não substituem o teste manual com Narrador, Alto Contraste e toque (ver QA-WINDOWS.md): conferem o que dá para medir sem pessoa.
/// O que for só informação (por exemplo, se o COM do WinGet responde nesta máquina) vai para <c>windows-real.txt</c> e não derruba a CI.
/// </summary>
public static partial class Smoke
{
    private static readonly StringBuilder Real = new();

    private static async Task RealWindowsAsync(MainWindow main, string folder)
    {
        ThemeManager.Apply("light");
        main.SizeToContent = SizeToContent.Manual;
        main.Width = 1360; main.Height = 800;
        await Idle(600);
        Real.AppendLine($"Windows {Environment.OSVersion.Version} · animações do Windows: {SystemParameters.ClientAreaAnimation} · alto contraste: {SystemParameters.HighContrast} · Motion.Enabled: {Motion.Enabled}");

        await Step("windows-real-acessibilidade", () => AccessibilityTreeAsync(main));
        await Step("windows-real-estrelas", () => StarsAsync(main));
        await Step("windows-real-efeito-de-clique", () => RippleAsync(main));
        await Step("windows-real-winget", WingetProbeAsync);

        File.WriteAllText(Path.Combine(folder, "windows-real.txt"), Real.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// O que o Narrador e o Windows Voz de Acesso enxergam: cada cartão-linha (ActionRow) é um botão só, com nome e descrição, que
    /// recebe foco e tem o padrão de acionar; nada de botão dentro de botão. Botão qualquer sem nome vira aviso.
    /// </summary>
    private static async Task AccessibilityTreeAsync(MainWindow main)
    {
        var rowStyle = (Style)Application.Current.FindResource("ActionRow");
        int rows = 0, unnamed = 0;
        for (int tab = 0; tab <= AppTabs.Settings; tab++)
        {
            main.SelectTab(tab);
            await Idle(400);
            main.UpdateLayout();
            foreach (var button in Descendants(main).OfType<Button>().Where(b => b.IsVisible))
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(button);
                if (peer is null) continue;
                string name = peer.GetName(), where = $"aba {tab}, \"{name}\"";
                bool isRow = ReferenceEquals(button.Style, rowStyle);
                if (!isRow)
                {
                    if (name.Length == 0 && button.ToolTip is null) { unnamed++; Log.AppendLine($"aviso botão sem nome para o leitor de tela (aba {tab}): {button.GetType().Name} {button.Name}"); }
                    continue;
                }
                rows++;
                if (name.Length == 0) Errors.Add($"Cartão-linha sem nome para o leitor de tela (aba {tab})");
                if (peer.GetAutomationControlType() != AutomationControlType.Button) Errors.Add($"Cartão-linha que não é botão para o Narrador: {where}");
                if (peer.GetPattern(PatternInterface.Invoke) is null) Errors.Add($"Cartão-linha sem o padrão de acionar: {where}");
                if (!peer.IsKeyboardFocusable() || !button.IsTabStop) Errors.Add($"Cartão-linha que o teclado não alcança: {where}");
                if (peer.GetHelpText().Length == 0) Errors.Add($"Cartão-linha sem descrição falada: {where}");
                // Papel único: dentro do botão não pode haver outro botão nem outro item acionável
                var inner = Descendants(button).OfType<UIElement>().Select(UIElementAutomationPeer.CreatePeerForElement).Where(p => p is not null && p != peer)
                    .Where(p => p!.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.CheckBox or AutomationControlType.RadioButton).ToList();
                if (inner.Count > 0) Errors.Add($"Cartão-linha com outro controle acionável dentro: {where}");
            }
        }
        Real.AppendLine($"Árvore de acessibilidade: {rows} cartões-linha conferidos (botão único, nome, descrição, teclado); {unnamed} botões sem nome (avisos no smoke.txt)");
        if (rows < 14) Errors.Add($"Árvore de acessibilidade: só {rows} cartões-linha encontrados; a conferência não está vendo as telas");
    }

    /// <summary>As estrelas da avaliação são botões de opção que dizem a nota e o estado "selecionado".</summary>
    private static async Task StarsAsync(MainWindow main)
    {
        main.SelectTab(AppTabs.Settings);
        await Idle(400);
        var stars = Enumerable.Range(1, 5).Select(i => main.FindName("Star" + i) as RadioButton).ToList();
        if (stars.Any(s => s is null)) { Errors.Add("Avaliação: estrelas não encontradas"); return; }
        stars[3]!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Idle(300);
        for (int i = 0; i < 5; i++)
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(stars[i]!);
            string name = peer.GetName();
            var selection = peer.GetPattern(PatternInterface.SelectionItem) as System.Windows.Automation.Provider.ISelectionItemProvider;
            if (peer.GetAutomationControlType() != AutomationControlType.RadioButton) Errors.Add($"Avaliação: a estrela {i + 1} não é botão de opção");
            if (!name.Contains((i + 1).ToString())) Errors.Add($"Avaliação: a estrela {i + 1} fala \"{name}\"");
            if (selection is null || selection.IsSelected != (i == 3)) Errors.Add($"Avaliação: a estrela {i + 1} diz selecionada={selection?.IsSelected}, esperado {i == 3}");
        }
        Real.AppendLine($"Avaliação: 5 estrelas como botões de opção; nota escolhida na estrela 4 ({stars[3]!.Content})");
        // Deixa a conta como estava
        var clear = Descendants(main).OfType<Button>().FirstOrDefault(b => b.Content as string == Loc.T("Limpar avaliação"));
        clear?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    /// <summary>
    /// O brilho nasce no clique e some sozinho. Com as animações do Windows desligadas ou em Alto Contraste (como pode estar nesta
    /// máquina) o efeito não deve aparecer, e isso também é conferido.
    /// </summary>
    private static async Task RippleAsync(MainWindow main)
    {
        main.SelectTab(AppTabs.Settings);
        await Idle(400);
        var button = Descendants(main).OfType<Button>().FirstOrDefault(b => b.IsVisible && b.Template?.FindName("Ripple", b) is Border && b.ActualWidth > 40);
        if (button is null) { Errors.Add("Efeito de clique: nenhum botão com o desenho do efeito"); return; }
        var ripple = (Border)button.Template.FindName("Ripple", button);
        // Duas rodadas: com o movimento como o Windows desta máquina o configurou e com ele forçado ligado (a CI roda com as animações
        // desligadas, e sem isso o brilho nunca seria exercitado)
        bool drawn = false, gone = false;
        foreach (bool? forced in new bool?[] { null, true })
        {
            Motion.ForTesting = forced;
            try
            {
                var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = button };
                button.RaiseEvent(down);
                await Idle(60);
                drawn = ripple.Background is RadialGradientBrush;
                await Idle(ClickEffect.NormalMs + 250);
                gone = ripple.Background is null && ripple.Opacity <= 0.001;
                if (Motion.Enabled)
                {
                    if (!drawn) Errors.Add("Efeito de clique: o movimento está ligado e o brilho não apareceu");
                    if (!gone) Errors.Add("Efeito de clique: o brilho não sumiu depois de 250 ms");
                }
                else if (drawn) Errors.Add("Efeito de clique: apareceu com as animações do Windows desligadas ou em Alto Contraste");
                Real.AppendLine($"Efeito de clique ({(forced is null ? "como o Windows está" : "movimento forçado ligado")}): Motion.Enabled={Motion.Enabled}, brilho desenhado={drawn}, apagou sozinho={gone}");
            }
            finally { Motion.ForTesting = null; }
        }
    }

    /// <summary>
    /// Informação, não falha: se o Instalador de Aplicativo existe nesta máquina, se o COM do WinGet ativa e quanto demora a lista de
    /// atualizações. Sem aceitar termos (o app nunca aceita por ninguém), a consulta pode recusar; o motivo fica registrado.
    /// </summary>
    private static async Task WingetProbeAsync()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool cli = false;
        try { cli = await WingetRunner.AvailableAsync(); } catch (Exception ex) { Real.AppendLine("WinGet (linha de comando): erro " + ex.GetType().Name); }
        Real.AppendLine($"WinGet (linha de comando) responde: {cli} ({clock.ElapsedMilliseconds} ms)");

        var engine = UpdateEngines.Shared;
        clock.Restart();
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var scan = await engine.ListUpgradesAsync(sourceTermsAccepted: false, limit.Token);
            Real.AppendLine($"Consulta de atualizações: motor {scan.Engine}, {scan.List.Items.Count} programas, {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception ex) { Real.AppendLine($"Consulta de atualizações falhou: {ex.GetType().Name}: {ex.Message}"); }
        Real.AppendLine($"Motor principal (COM) em uso depois da consulta: {engine.UsingPrimary}" + (engine.LastPrimaryError is { } e ? $"; falha do COM: {Describe(e)}" : ""));
    }

    /// <summary>A cadeia de exceções com o código de cada uma, para saber por que o COM não ativou.</summary>
    private static string Describe(Exception e)
    {
        var parts = new List<string>();
        for (var x = e; x is not null; x = x.InnerException) parts.Add($"{x.GetType().Name} 0x{x.HResult:X8} {x.Message.Replace('\n', ' ').Trim()}");
        return string.Join(" <- ", parts);
    }
}
