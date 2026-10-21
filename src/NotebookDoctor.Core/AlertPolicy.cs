using System.Globalization;

namespace NotebookDoctor.Services;

/// <summary>M44: quando um aviso pode virar notificação.</summary>
public static class AlertPolicy
{
    /// <summary>Horário silencioso; aceita faixa que atravessa a meia-noite (22:00–07:00). Início igual ao fim = desligado.</summary>
    public static bool InQuietHours(TimeOnly now, TimeOnly start, TimeOnly end) =>
        start != end && (start < end ? now >= start && now < end : now >= start || now < end);

    /// <summary>"22:00", "7:30" ou "7" viram hora do dia.</summary>
    public static bool TryParseClock(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact((text ?? "").Trim(), ["H:mm", "HH:mm", "%H"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

/// <summary>M45: uma métrica só conta como alta depois de ficar acima do limite por alguns minutos seguidos.</summary>
public sealed class Sustained
{
    private DateTime _since = DateTime.MinValue;

    /// <summary>Retorna true quando a métrica está acima do limite há pelo menos <paramref name="minutes"/> minutos.</summary>
    public bool Update(bool above, DateTime time, int minutes)
    {
        if (!above) { _since = DateTime.MinValue; return false; }
        if (_since == DateTime.MinValue) _since = time;
        return (time - _since).TotalMinutes >= minutes;
    }
}
