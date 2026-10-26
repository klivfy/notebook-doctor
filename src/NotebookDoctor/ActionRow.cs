using System.Windows;

namespace NotebookDoctor;

/// <summary>O que aparece no fim de um cartão-linha: seta de "abre outra tela ou janela" ou de "abre fora do app".</summary>
public enum ActionRowSlot { Detail, External }

/// <summary>
/// Cartão-linha (<c>ActionRow</c>): <c>Slot</c> escolhe o sinal da direita. <c>Detail</c> (›) abre uma subjanela ou outra seção;
/// <c>External</c> (↗) abre o navegador ou um programa do Windows. A linha inteira é um botão só: Enter e Espaço acionam, o anel
/// de foco aparece só no teclado e o Narrador lê o título e a descrição como um controle.
/// </summary>
public static class ActionRow
{
    public static readonly DependencyProperty SlotProperty =
        DependencyProperty.RegisterAttached("Slot", typeof(ActionRowSlot), typeof(ActionRow), new PropertyMetadata(ActionRowSlot.Detail));

    public static ActionRowSlot GetSlot(DependencyObject o) => (ActionRowSlot)o.GetValue(SlotProperty);
    public static void SetSlot(DependencyObject o, ActionRowSlot value) => o.SetValue(SlotProperty, value);

    /// <summary>Para onde o cartão-linha leva (nome do texto embutido ou endereço do site); o <c>Tag</c> já é o ícone.</summary>
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached("Target", typeof(string), typeof(ActionRow), new PropertyMetadata(null));

    public static string? GetTarget(DependencyObject o) => (string?)o.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject o, string? value) => o.SetValue(TargetProperty, value);
}
