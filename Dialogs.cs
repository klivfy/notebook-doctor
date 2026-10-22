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

    /// <summary>
    /// B36: confirmação com "Não perguntar de novo". Só para ações que não apagam nem fecham nada
    /// (ex.: reabrir como administrador, criar um plano de energia). Dispensada, devolve true sem mostrar.
    /// </summary>
    public static bool ConfirmOnce(Window owner, Services.AppSettings s, string key, string title, string message, string confirmText)
    {
        if (s.SkipConfirm.Contains(key)) return true;
        bool ok = Show(owner, title, message, confirmText, "Cancelar", false, out bool dontAsk, dontAskOption: true);
        if (ok && dontAsk) { s.SkipConfirm.Add(key); s.Save(); }
        return ok;
    }

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
    /// <param name="members">M22: processos de um aplicativo agrupado; sem isso, só o processo <paramref name="name"/>.</param>
    public static void ShowProcessDetails(Window owner, string name, IReadOnlyList<string>? members = null)
    {
        members ??= [name];
        var rows = new List<object>();
        foreach (var p in members.SelectMany(System.Diagnostics.Process.GetProcessesByName))
            using (p)
            {
                string Try(Func<string> f) { try { return f(); } catch { return "—"; } }
                rows.Add(new
                {
                    Processo = p.ProcessName,
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
        var about = Services.ProcessKnowledge.Describe(members[0]);
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

    /// <summary>
    /// Fase 5: revisão das sobras antes de apagar. O nível escolhido decide o que aparece (Seguro, Moderado ou
    /// Avançado); só o que está à vista e marcado é devolvido. Sem administrador, o que exige administrador fica de fora.
    /// Devolve null quando a pessoa pula.
    /// </summary>
    public static List<Services.Leftover>? ReviewLeftovers(Window owner, string program, IReadOnlyList<Services.Leftover> items, bool isAdmin)
    {
        if (!isAdmin) foreach (var l in items.Where(l => l.NeedsAdmin)) l.Selected = false;
        string title = Services.Loc.F("Sobras de {0}", program);
        var dlg = NewWindow(owner, title, 980);
        dlg.SizeToContent = SizeToContent.Manual;
        dlg.Height = 620;
        dlg.ResizeMode = ResizeMode.CanResize;

        var heading = Heading(title, "FsTitle", new Thickness(0, 0, 0, 4));
        var sub = new TextBlock
        {
            Text = Services.Loc.T("Revise antes de apagar. Tudo vai para um backup por 30 dias e pode ser desfeito na aba Ações."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");

        var level = new ComboBox { Width = 460, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        level.Items.Add(Services.Loc.T("Seguro: só o que aponta para a pasta do programa"));
        level.Items.Add(Services.Loc.T("Moderado: também pastas e chaves com o nome dele"));
        level.Items.Add(Services.Loc.T("Avançado: também nomes parecidos (confira um por um)"));
        level.SelectedIndex = 1;
        System.Windows.Automation.AutomationProperties.SetName(level, Services.Loc.T("Nível da busca"));

        var adminNote = new TextBlock
        {
            Text = Services.Loc.T("Itens com \"Exige administrador\" só saem com o app aberto como administrador (Ajustes › Reabrir como administrador)."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Visibility = !isAdmin && items.Any(l => l.NeedsAdmin) ? Visibility.Visible : Visibility.Collapsed,
        };
        adminNote.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");

        var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
        System.Windows.Automation.AutomationProperties.SetName(grid, title);
        // Marcar com um clique só (a coluna de caixa padrão do DataGrid pede dois)
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding(nameof(Services.Leftover.Selected)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        check.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new System.Windows.Data.Binding(nameof(Services.Leftover.Display)));
        check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        grid.Columns.Add(new DataGridTemplateColumn { Header = Services.Loc.T("Apagar"), CellTemplate = new DataTemplate { VisualTree = check }, Width = 70 });
        DataGridTextColumn Text(string header, string path, DataGridLength width) =>
            new() { Header = header, Binding = new System.Windows.Data.Binding(path), Width = width, IsReadOnly = true };
        grid.Columns.Add(Text(Services.Loc.T("Nível"), nameof(Services.Leftover.LevelText), 90));
        grid.Columns.Add(Text(Services.Loc.T("Tipo"), nameof(Services.Leftover.KindText), 130));
        grid.Columns.Add(Text(Services.Loc.T("Local"), nameof(Services.Leftover.Display), new DataGridLength(1, DataGridLengthUnitType.Star)));
        grid.Columns.Add(Text(Services.Loc.T("Motivo"), nameof(Services.Leftover.Reason), 230));
        grid.Columns.Add(Text(Services.Loc.T("Tamanho"), nameof(Services.Leftover.SizeText), 90));
        grid.Columns.Add(Text(Services.Loc.T("Atenção"), nameof(Services.Leftover.Notes), 170));

        List<Services.Leftover> Visible() => Services.LeftoverScanner.UpTo(items, (Services.LeftoverLevel)level.SelectedIndex).ToList();
        void Refresh() { grid.ItemsSource = Visible(); }
        level.SelectionChanged += (_, _) => Refresh();
        Refresh();

        List<Services.Leftover>? result = null;
        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        Button Btn(string text, string style)
        {
            var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0) };
            b.SetResourceReference(FrameworkElement.StyleProperty, style);
            return b;
        }
        var all = Btn(Services.Loc.T("Marcar todos"), "TextButton");
        var none = Btn(Services.Loc.T("Desmarcar todos"), "TextButton");
        all.Margin = new Thickness(0);
        void Mark(bool on)
        {
            foreach (var l in Visible()) l.Selected = on && (isAdmin || !l.NeedsAdmin);
            grid.Items.Refresh();
        }
        all.Click += (_, _) => Mark(true);
        none.Click += (_, _) => Mark(false);
        var skip = Btn(Services.Loc.T("Pular"), "TextButton");
        skip.IsCancel = true;
        skip.Click += (_, _) => dlg.Close();
        var delete = Btn(Services.Loc.T("Apagar selecionados"), "DangerButton");
        delete.Click += (_, _) =>
        {
            result = Visible().Where(l => l.Selected && (isAdmin || !l.NeedsAdmin)).ToList();
            dlg.Close();
        };
        DockPanel.SetDock(all, Dock.Left); DockPanel.SetDock(none, Dock.Left);
        DockPanel.SetDock(delete, Dock.Right); DockPanel.SetDock(skip, Dock.Right);
        buttons.Children.Add(all); buttons.Children.Add(none); buttons.Children.Add(delete); buttons.Children.Add(skip);

        var root = new DockPanel { Margin = new Thickness(24) };
        foreach (var top in new FrameworkElement[] { heading, sub, level, adminNote }) { DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); }
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(grid);
        dlg.Content = root;
        dlg.ShowDialog();
        return result;
    }

    /// <summary>Leva 5.3: escolhas da fila de desinstalação.</summary>
    public sealed record QueueChoice(bool Silent, bool AutoLeftovers);

    /// <summary>Leva 5.3: confirma a fila, com modo silencioso (padrão) e "apagar as sobras sem perguntar" (desligado).</summary>
    public static QueueChoice? ConfirmQueue(Window owner, IReadOnlyList<string> programs, IReadOnlyList<string> skipped)
    {
        string title = Services.Loc.F("Desinstalar {0}?", Services.Pt.N(programs.Count, Services.Loc.T("programa"), Services.Loc.T("programas")));
        var dlg = NewWindow(owner, title, 520);
        var heading = Heading(title, "FsTitleLarge", new Thickness(0, 0, 0, 12));
        var list = new ListBox { ItemsSource = programs, MaxHeight = 200, Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(list, title);
        var body = new TextBlock
        {
            Text = Services.Loc.T("Um de cada vez, com um ponto de restauração antes. Se um falhar, os outros continuam.")
                   + (skipped.Count > 0 ? "\n\n" + Services.Loc.F("Ficam de fora (o Windows não deixa remover por aqui): {0}", string.Join(", ", skipped)) : ""),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        var silent = new CheckBox
        {
            Content = Services.Loc.T("Sem janelas, quando o desinstalador permitir"), IsChecked = true, Margin = new Thickness(0, 0, 0, 8),
            ToolTip = Services.Loc.T("Pacotes MSI, Inno Setup, NSIS e outros conhecidos desinstalam sem perguntar nada. Os outros abrem a janela de sempre."),
        };
        var auto = new CheckBox
        {
            Content = Services.Loc.T("Apagar as sobras sem perguntar (nível Moderado, com backup)"), IsChecked = false, Margin = new Thickness(0, 0, 0, 20),
            ToolTip = Services.Loc.T("Desmarcado, o app mostra as sobras de cada programa para você revisar no fim da fila."),
        };
        QueueChoice? result = null;
        var cancel = new Button { Content = Services.Loc.T("Cancelar"), IsCancel = true };
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "TextButton");
        var ok = new Button { Content = Services.Loc.T("Desinstalar") };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "DangerButton");
        ok.Click += (_, _) => { result = new QueueChoice(silent.IsChecked == true, auto.IsChecked == true); dlg.Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } };
        dlg.Content = new StackPanel { Margin = new Thickness(24), Children = { heading, list, body, silent, auto, buttons } };
        dlg.ShowDialog();
        return result;
    }

    /// <summary>Leva 5.3: nome e, se a pessoa souber, a pasta de um programa que já saiu da lista, para procurar as sobras.</summary>
    public static (string Name, string? Folder)? AskLeftoverTarget(Window owner)
    {
        string title = Services.Loc.T("Procurar sobras de um programa já removido");
        var dlg = NewWindow(owner, title, 520);
        var heading = Heading(title, "FsTitleLarge", new Thickness(0, 0, 0, 12));
        var body = new TextBlock
        {
            Text = Services.Loc.T("Para programas que não aparecem mais na lista, mas deixaram pastas e registro. Com a pasta, a busca também acha o que aponta para ela."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        var nameLabel = new TextBlock { Text = Services.Loc.T("Nome do programa"), Margin = new Thickness(0, 0, 0, 4) };
        var name = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(name, nameLabel.Text);
        var folderLabel = new TextBlock { Text = Services.Loc.T("Pasta onde ele estava (opcional)"), Margin = new Thickness(0, 0, 0, 4) };
        var folder = new TextBox { Margin = new Thickness(0, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetName(folder, folderLabel.Text);
        var browse = new Button { Content = Services.Loc.T("Escolher…") };
        browse.SetResourceReference(FrameworkElement.StyleProperty, "TonalButton");
        browse.Click += (_, _) =>
        {
            var pick = new Microsoft.Win32.OpenFolderDialog { Title = folderLabel.Text };
            if (pick.ShowDialog(dlg) == true) folder.Text = pick.FolderName;
        };
        var folderRow = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(folder);
        (string, string?)? result = null;
        var cancel = new Button { Content = Services.Loc.T("Cancelar"), IsCancel = true };
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "TextButton");
        var ok = new Button { Content = Services.Loc.T("Procurar"), IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (name.Text.Trim().Length == 0) { name.Focus(); return; }
            result = (name.Text.Trim(), folder.Text.Trim().Length > 0 ? folder.Text.Trim() : null);
            dlg.Close();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } };
        dlg.Content = new StackPanel { Margin = new Thickness(24), Children = { heading, body, nameLabel, name, folderLabel, folderRow, buttons } };
        dlg.Loaded += (_, _) => name.Focus();
        dlg.ShowDialog();
        return result;
    }

    /// <summary>
    /// Leva 5.4: instalações monitoradas, com detalhes, exportar, importar e apagar. Devolve a que a pessoa pediu para
    /// desinstalar (quem chamou cuida disso), ou null.
    /// </summary>
    public static Services.InstallTrace? ShowTraces(Window owner)
    {
        string title = Services.Loc.T("Instalações monitoradas");
        var dlg = NewWindow(owner, title, 760);
        var heading = Heading(title, "FsTitle", new Thickness(0, 0, 0, 4));
        var sub = new TextBlock
        {
            Text = Services.Loc.T("O que cada instalação criou no disco e no registro. \"Desinstalar\" usa essa anotação e a busca de sobras; você revisa antes de apagar."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        var list = new ListBox { Height = 260, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(list, title);
        var msg = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetLiveSetting(msg, System.Windows.Automation.AutomationLiveSetting.Polite);
        Services.InstallTrace? result = null;

        void Load()
        {
            var traces = Services.TraceStore.List();
            list.ItemsSource = traces.Select(t => new ListBoxItem
            {
                Tag = t,
                Content = $"{t.Name} · {t.Started.ToString("g", Services.Loc.Culture)} · {Services.Pt.N(t.Count, Services.Loc.T("item"), Services.Loc.T("itens"))}"
                          + (t.Imported ? $" · {Services.Loc.T("importada")}" : ""),
            }).ToList();
            if (traces.Count == 0) msg.Text = Services.Loc.T("Nenhuma instalação monitorada ainda. Use \"Instalar monitorando…\" na aba Programas.");
        }
        Services.InstallTrace? Selected()
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is Services.InstallTrace t) return t;
            msg.Text = Services.Loc.T("Selecione uma instalação na lista.");
            return null;
        }
        Button Btn(string text, string? style = "TextButton")
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
            if (style is not null) b.SetResourceReference(FrameworkElement.StyleProperty, style);
            return b;
        }

        var uninstall = Btn(Services.Loc.T("Desinstalar…"), null);
        uninstall.Click += (_, _) => { if (Selected() is { } t) { result = t; dlg.Close(); } };
        var details = Btn(Services.Loc.T("Ver detalhes"), "TonalButton");
        details.Click += (_, _) => { if (Selected() is { } t) ShowText(dlg, t.Name, Services.TraceRules.Report(t)); };
        var export = Btn(Services.Loc.T("Exportar…"));
        export.Click += (_, _) =>
        {
            if (Selected() is not { } t) return;
            var save = new Microsoft.Win32.SaveFileDialog
            {
                FileName = t.Name,
                Filter = $"{Services.Loc.T("Para importar em outro PC (JSON)")}|*.json|{Services.Loc.T("Texto")}|*.txt|{Services.Loc.T("Página (HTML)")}|*.html",
            };
            if (save.ShowDialog(dlg) != true) return;
            try { Services.TraceStore.Export(t, save.FileName); msg.Text = Services.Loc.F("Salvo: {0}", save.FileName); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { msg.Text = ex.Message; }
        };
        var import = Btn(Services.Loc.T("Importar…"));
        import.Click += (_, _) =>
        {
            var open = new Microsoft.Win32.OpenFileDialog { Filter = $"{Services.Loc.T("Instalação monitorada (JSON)")}|*.json" };
            if (open.ShowDialog(dlg) != true) return;
            msg.Text = Services.TraceStore.Import(open.FileName) is { } t
                ? Services.Loc.F("{0} importada. Os itens dela entram como Moderado: confira na revisão.", t.Name)
                : Services.Loc.T("O arquivo não é uma instalação monitorada.");
            Load();
        };
        var delete = Btn(Services.Loc.T("Apagar"));
        delete.Click += (_, _) =>
        {
            if (Selected() is not { } t) return;
            if (!Confirm(dlg, Services.Loc.T("Apagar a anotação?"), Services.Loc.T("O programa continua instalado; só a anotação da instalação sai."), Services.Loc.T("Apagar"))) return;
            Services.TraceStore.Delete(t.Id);
            Load();
        };
        var close = Btn(Services.Loc.T("Fechar"));
        close.IsCancel = true;
        close.Click += (_, _) => dlg.Close();
        var buttons = new WrapPanel { Children = { uninstall, details, export, import, delete, close } };
        dlg.Content = new StackPanel { Margin = new Thickness(24), Children = { heading, sub, list, msg, buttons } };
        Load();
        dlg.ShowDialog();
        return result;
    }

    /// <summary>Fase 5: backups da remoção de sobras, com restaurar e apagar.</summary>
    public static void ShowBackups(Window owner, Services.BackupStore store)
    {
        string title = Services.Loc.T("Backups das sobras removidas");
        var dlg = NewWindow(owner, title, 720);
        var heading = Heading(title, "FsTitle", new Thickness(0, 0, 0, 4));
        var sub = new TextBlock
        {
            Text = Services.Loc.T("Cada remoção de sobras guarda o que apagou por 30 dias (até 2 GB no total). Restaurar devolve tudo ao lugar."),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        var list = new ListBox { Height = 260, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(list, title);
        var msg = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetLiveSetting(msg, System.Windows.Automation.AutomationLiveSetting.Polite);

        void Load()
        {
            var sessions = store.List();
            list.ItemsSource = sessions.Select(m => new ListBoxItem { Content = Services.BackupRules.Describe(m), Tag = m.Id }).ToList();
            if (sessions.Count == 0) msg.Text = Services.Loc.T("Nenhum backup guardado.");
        }
        string? Selected() => (list.SelectedItem as ListBoxItem)?.Tag as string;

        var restore = new Button { Content = Services.Loc.T("Restaurar"), Margin = new Thickness(0, 0, 8, 0) };
        restore.Click += (_, _) =>
        {
            if (Selected() is not { } id) { msg.Text = Services.Loc.T("Selecione um backup na lista."); return; }
            if (!Confirm(dlg, Services.Loc.T("Restaurar backup?"), Services.Loc.T("Os arquivos, chaves e tarefas guardados voltam para onde estavam."), Services.Loc.T("Restaurar"))) return;
            msg.Text = store.Restore(id);
            Services.ActionLog.Add("Backup de sobras restaurado", id);
            Load();
        };
        var delete = new Button { Content = Services.Loc.T("Apagar backup"), Margin = new Thickness(0, 0, 8, 0) };
        delete.SetResourceReference(FrameworkElement.StyleProperty, "TextButton");
        delete.Click += (_, _) =>
        {
            if (Selected() is not { } id) { msg.Text = Services.Loc.T("Selecione um backup na lista."); return; }
            if (!Confirm(dlg, Services.Loc.T("Apagar backup?"), Services.Loc.T("Depois disso, essa remoção de sobras não pode mais ser desfeita."), Services.Loc.T("Apagar"), danger: true)) return;
            try { store.Delete(id); msg.Text = Services.Loc.T("Backup apagado."); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { msg.Text = ex.Message; }
            Load();
        };
        var close = new Button { Content = Services.Loc.T("Fechar"), IsCancel = true };
        close.SetResourceReference(FrameworkElement.StyleProperty, "TextButton");
        close.Click += (_, _) => dlg.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { restore, delete, close } };
        dlg.Content = new StackPanel { Margin = new Thickness(24), Children = { heading, sub, list, msg, buttons } };
        Load();
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
        dlg.Language = System.Windows.Markup.XmlLanguage.GetLanguage(Services.Loc.Culture.IetfLanguageTag);
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

    /// <summary>B23/B24: texto longo (novidades, licenças, privacidade) com rolagem.</summary>
    public static void ShowText(Window owner, string title, string text)
    {
        var dlg = NewWindow(owner, title, 600);
        var heading = Heading(title, "FsTitleLarge", new Thickness(0, 0, 0, 12));
        var body = new TextBox
        {
            Text = text, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(0),
        };
        body.SetResourceReference(Control.ForegroundProperty, "OnSurfaceVariant");
        System.Windows.Automation.AutomationProperties.SetName(body, title);
        var scroll = new ScrollViewer { Content = body, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 16) };
        var ok = new Button { Content = "Fechar", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => dlg.Close();
        dlg.Content = new StackPanel { Margin = new Thickness(24), Children = { heading, scroll, ok } };
        dlg.Loaded += (_, _) => body.Focus(); // leitor de tela começa pelo texto; setas rolam
        dlg.ShowDialog();
    }

    /// <summary>
    /// B38: pede a senha das configurações. <paramref name="check"/> devolve o erro a mostrar ou null quando a senha
    /// está certa; com erro, o diálogo continua aberto. Devolve true se liberou.
    /// </summary>
    public static bool AskPassword(Window? owner, string what, Func<string, string?> check)
    {
        var dlg = NewWindow(owner!, "Senha das configurações", 420);
        if (owner is null) dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen; // janela principal escondida (bandeja)
        var heading = Heading("Senha das configurações", "FsTitleLarge", new Thickness(0, 0, 0, 8));
        var body = new TextBlock
        {
            Text = $"Para {what}, digite a senha definida pelo responsável por este computador.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        var box = new PasswordBox { Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(box, "Senha");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        // Vermelho do tema não tem contraste 4,5:1 sobre o fundo escuro: o erro vai em semi-negrito na cor do texto
        error.SetResourceReference(TextBlock.FontFamilyProperty, "FontSemi");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(error, System.Windows.Automation.AutomationLiveSetting.Assertive);

        bool result = false;
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "TextButton");
        var ok = new Button { Content = "Desbloquear", IsDefault = true, Margin = new Thickness(0) };
        ok.Click += (_, _) =>
        {
            string? msg = check(box.Password);
            if (msg is null) { result = true; dlg.Close(); return; }
            error.Text = msg;
            box.Clear();
            box.Focus();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } };
        dlg.Content = new StackPanel { Margin = new Thickness(24), Children = { heading, body, box, error, buttons } };
        dlg.Loaded += (_, _) => box.Focus();
        dlg.ShowDialog();
        return result;
    }

    private static bool Show(Window owner, string title, string message, string ok, string? cancel, bool danger) =>
        Show(owner, title, message, ok, cancel, danger, out _, dontAskOption: false);

    private static bool Show(Window owner, string title, string message, string ok, string? cancel, bool danger, out bool dontAsk, bool dontAskOption)
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

        var panel = new StackPanel { Margin = new Thickness(24), Children = { heading, body } };
        var skip = new CheckBox { Content = "Não perguntar de novo", Margin = new Thickness(0, -12, 0, 16) };
        if (dontAskOption) panel.Children.Add(skip);
        panel.Children.Add(buttons);
        dlg.Content = panel;
        dlg.ShowDialog();
        dontAsk = skip.IsChecked == true;
        return result;
    }
}
