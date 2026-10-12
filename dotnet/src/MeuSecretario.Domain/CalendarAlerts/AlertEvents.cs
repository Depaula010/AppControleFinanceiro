using System.Globalization;

namespace MeuSecretario.Domain.CalendarAlerts;

/// <summary>Evento que deve gerar alerta (RN-014-12). <paramref name="Start"/> é o texto <c>dateTime</c> do Google, como veio.</summary>
public sealed record UpcomingEvent(string Id, string Summary, string Start, string Location, string Description, string CalendarName);

/// <summary>
/// Janela de busca do ciclo: <c>(alvo - 60 s, alvo + 30 s]</c>, com alvo = agora + minutos (RN-014-11, desvio 121). Os 60 s
/// para trás deixam o ciclo seguinte ver o evento que o anterior perdeu (o claim de 2 h evita o aviso repetido).
/// </summary>
public static class AlertWindow
{
    public static readonly TimeSpan Before = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan After = TimeSpan.FromSeconds(30);

    /// <summary>Desvio 123: tolerância de ciclos perdidos; o evento segue visível por mais 3 min antes do início.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(3);

    public static (DateTimeOffset Start, DateTimeOffset End) For(DateTimeOffset now, int minutesBefore)
    {
        var target = now.AddMinutes(minutesBefore);
        return (target - Before - Grace, target + After);
    }

    /// <summary>
    /// Minutos que o texto do alerta anuncia (desvio 123): os que de fato restam, entre 1 e o configurado. No ciclo normal
    /// o resto arredondado é o próprio <paramref name="minutesBefore"/>.
    /// </summary>
    public static int RemainingMinutes(DateTimeOffset now, DateTimeOffset eventStart, int minutesBefore)
    {
        var remaining = (int)Math.Round((eventStart - now).TotalMinutes, MidpointRounding.AwayFromZero);
        return Math.Clamp(remaining, 1, Math.Max(1, minutesBefore));
    }

    /// <summary>Início do evento dentro da janela: limite inferior exclusivo, superior inclusivo.</summary>
    public static bool Contains((DateTimeOffset Start, DateTimeOffset End) window, DateTimeOffset eventStart) =>
        eventStart > window.Start && eventStart <= window.End;
}

/// <summary>Leitura do horário ISO 8601 do Google e texto do alerta (RN-014-12 e RN-014-14).</summary>
public static class AlertMessage
{
    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd",
    ];

    /// <summary>Equivale ao <c>datetime.fromisoformat</c> do Python; o deslocamento escrito no texto é mantido.</summary>
    public static bool TryParseIso(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParseExact(text, IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary><c>strftime('%H:%M')</c> da hora escrita no texto; texto que não é ISO volta cru.</summary>
    public static string FormatTime(string startText) =>
        TryParseIso(startText, out var parsed) ? parsed.ToString("HH:mm", CultureInfo.InvariantCulture) : startText;

    /// <summary>Texto literal do Flask (<c>calendar_alert_service.py:243-250</c>).</summary>
    public static string Build(string title, string startText, string location, int minutesBefore)
    {
        var message = string.Create(CultureInfo.InvariantCulture, $"⚠️ Faltam {minutesBefore} min: {title}") +
                      "\n🕒 " + FormatTime(startText);
        return location.Length > 0 ? message + "\n📍 " + location : message;
    }
}
