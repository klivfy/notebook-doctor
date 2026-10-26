using System.Windows;
using System.Windows.Controls;
using NotebookDoctor.Services;

namespace NotebookDoctor;

/// <summary>
/// B25: avaliação do app (Configurações › Sobre) e M18: estatísticas anônimas opcionais (Configurações › Privacidade).
/// Nada sai do computador sem clique: a avaliação só no botão "Enviar avaliação", depois de mostrar o texto exato; as
/// estatísticas só com a opção ligada (nasce desligada), no máximo uma vez por semana, e a pessoa pode ver o texto antes.
/// </summary>
public partial class MainWindow
{
    private RadioButton[] Stars => [Star1, Star2, Star3, Star4, Star5];

    private static string Clean(string? text) =>
        Feedback.CleanComment(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName, Environment.UserDomainName);

    private void LoadRatingAndPrivacy()
    {
        var s = Host.Settings;
        CfgTelemetry.IsChecked = s.TelemetryEnabled;
        RatingComment.Text = s.RatingComment;
        ShowStars(s.RatingStars);
    }

    /// <summary>Estrelas cheias até a nota, na cor de destaque; as demais vazias. A seleção também vale para o leitor de tela.</summary>
    private void ShowStars(int rating)
    {
        for (int i = 0; i < 5; i++)
        {
            var star = Stars[i];
            bool filled = i < rating;
            star.Content = filled ? "" : "";
            star.IsChecked = i == rating - 1;
            star.SetResourceReference(Control.ForegroundProperty, filled ? "AccentText" : "TextSecondary");
        }
        var s = Host.Settings;
        RatingLabel.Text = rating == 0 ? Loc.T("Escolha de 1 a 5 estrelas.")
            : s.RatingSentAt != DateTime.MinValue ? Loc.F("Sua nota: {0} de 5. Enviada em {1:d}.", rating, s.RatingSentAt)
            : Loc.F("Sua nota: {0} de 5. Guardada só neste computador.", rating);
        RatingSendBtn.IsEnabled = rating > 0;
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out int stars) || !Feedback.ValidStars(stars)) return;
        var s = Host.Settings;
        s.RatingStars = stars;
        s.RatingSentAt = DateTime.MinValue; // a nota mudou: a que foi enviada antes já não vale
        s.Save();
        ShowStars(stars);
        RatingMsg.Text = "";
    }

    private void RatingComment_LostFocus(object sender, RoutedEventArgs e)
    {
        string clean = Clean(RatingComment.Text);
        if (clean != RatingComment.Text) RatingComment.Text = clean; // o que aparece na caixa é o que fica guardado e o que seria enviado
        var s = Host.Settings;
        if (s.RatingComment == clean) return;
        s.RatingComment = clean;
        s.RatingSentAt = DateTime.MinValue;
        s.Save();
        ShowStars(s.RatingStars);
    }

    private void RatingClear_Click(object sender, RoutedEventArgs e)
    {
        var s = Host.Settings;
        s.RatingStars = 0; s.RatingComment = ""; s.RatingSentAt = DateTime.MinValue;
        s.Save();
        RatingComment.Text = "";
        ShowStars(0);
        RatingMsg.Text = Loc.T("Avaliação apagada deste computador.");
    }

    private async void RatingSend_Click(object sender, RoutedEventArgs e)
    {
        var s = Host.Settings;
        if (!Feedback.ValidStars(s.RatingStars)) { RatingMsg.Text = Loc.T("Escolha de 1 a 5 estrelas antes de enviar."); return; }
        RatingComment_LostFocus(this, e);
        if (AppPaths.Demo) { RatingMsg.Text = Loc.T("No modo demonstração nada é enviado."); return; }

        string payload = Feedback.Payload(s.RatingStars, s.RatingComment, AppInfo.Version, Loc.Language);
        bool ok = Dialogs.Confirm(this, Loc.T("Enviar avaliação"),
            Loc.F("O app vai enviar exatamente isto para klivfy.com, e mais nada:\n\n{0}\n\nNão vai identificador seu nem do computador. Quer enviar?", payload),
            Loc.T("Enviar"));
        if (!ok) return;

        RatingSendBtn.IsEnabled = false;
        RatingMsg.Text = Loc.T("Enviando…");
        try
        {
            switch (await UsageStats.PostAsync(KlivfyUrls.Rating, payload))
            {
                case SendResult.Sent:
                    s.RatingSentAt = DateTime.Now;
                    s.Save();
                    RatingMsg.Text = Loc.T("Avaliação enviada. Obrigado!");
                    break;
                case SendResult.Offline:
                    RatingMsg.Text = Loc.T("Sem conexão com klivfy.com. Sua avaliação continua guardada; tente de novo mais tarde.");
                    break;
                default:
                    RatingMsg.Text = Loc.T("A Klivfy não aceitou a avaliação agora. Ela continua guardada; tente de novo mais tarde.");
                    break;
            }
        }
        finally { ShowStars(s.RatingStars); }
    }

    // ---------- M18: estatísticas anônimas ----------
    private void Telemetry_Click(object sender, RoutedEventArgs e)
    {
        var s = Host.Settings;
        bool on = CfgTelemetry.IsChecked == true;
        if (on && AppPaths.Demo) { CfgTelemetry.IsChecked = false; TelemetryMsg.Text = Loc.T("No modo demonstração nada é enviado."); return; }
        s.TelemetryEnabled = on;
        if (!on) { s.TelemetryUses.Clear(); s.TelemetryFailures.Clear(); } // desligar apaga a contagem guardada
        s.Save();
        TelemetryMsg.Text = on
            ? Loc.T("Ligado. O app começa a contar agora e envia a contagem no máximo uma vez por semana. Desligue aqui quando quiser.")
            : Loc.T("Desligado. Nada é contado nem enviado.");
    }

    private void TelemetryPreview_Click(object sender, RoutedEventArgs e) =>
        Dialogs.ShowText(this, Loc.T("O que seria enviado"),
            Loc.F("Este é o texto exato que o app enviaria agora para klivfy.com. {0}\n\n{1}",
                  Host.Settings.TelemetryEnabled ? Loc.T("A opção está ligada.") : Loc.T("A opção está desligada, então nada é enviado."), UsageStats.PendingPayload()));

    private void TelemetryClear_Click(object sender, RoutedEventArgs e)
    {
        UsageStats.Clear();
        TelemetryMsg.Text = Loc.T("Contagem apagada deste computador.");
    }
}
