namespace NotebookDoctor.Services;

/// <summary>Há quanto tempo o Windows está ligado desde o último boot.</summary>
public static class Uptime
{
    public static TimeSpan Current => TimeSpan.FromMilliseconds(Environment.TickCount64);
}
