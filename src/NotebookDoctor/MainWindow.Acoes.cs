using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>Aba Ações: registro das mudanças feitas pelo app e Desfazer.</summary>
public partial class MainWindow
{
    // ---------- Ações (registro + desfazer) ----------
    private void LoadActions() => ActionsGrid.ItemsSource = ActionLog.Load().OrderByDescending(a => a.Time).ToList();

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionEntry entry) { ActionsMsg.Text = Loc.T("Selecione uma ação na lista."); return; }
        if (!entry.CanUndo) { ActionsMsg.Text = Loc.T(entry.Undone ? "Essa ação já foi desfeita." : "Essa ação é definitiva e não pode ser desfeita."); return; }
        if (Simulate(Loc.F("Desfazer: {0}", entry.TitleText), entry.DetailText)) return;
        ActionsMsg.Text = ActionLog.Undo(entry);
        ActionLog.Add(Loc.F("Desfeito: {0}", entry.TitleText), entry.DetailText);
        RefreshStartup();
        LoadActions();
        _ = Host.RefreshHealthAsync();
    }
}
