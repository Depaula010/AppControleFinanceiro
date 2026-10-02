using System.Globalization;

namespace MeuSecretario.Api.Http;

/// <summary>
/// Formato do <c>datetime.isoformat()</c> do Python, usado nos timestamps das respostas (spec 008 §4).
/// A fração (6 dígitos) só aparece quando os microssegundos não são zero. A produção usa a sessão do
/// banco em UTC, então os instantes com fuso saem sempre com <c>+00:00</c>.
/// </summary>
public static class PythonDateFormat
{
    /// <summary>Instante com fuso: <c>2026-10-02T13:45:10.123456+00:00</c>; <c>null</c> se nulo.</summary>
    public static string? Iso(DateTimeOffset? value) =>
        value is { } instant ? Format(instant.UtcDateTime) + "+00:00" : null;

    /// <summary>
    /// <c>timestamptz</c> lido do Npgsql como <see cref="DateTime"/>. <c>Kind = Unspecified</c> é tratado como UTC
    /// (nunca como hora local do servidor).
    /// </summary>
    public static string? Iso(DateTime? value) =>
        value is { } moment
            ? Format(moment.Kind == DateTimeKind.Local ? moment.ToUniversalTime() : moment) + "+00:00"
            : null;

    /// <summary>Data/hora sem fuso (<c>datetime.now().isoformat()</c>): <c>2026-10-02T08:30:00.123456</c>.</summary>
    public static string IsoLocal(DateTime value) => Format(value);

    private static string Format(DateTime value)
    {
        var text = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var microseconds = value.Ticks % TimeSpan.TicksPerSecond / 10;
        return microseconds == 0
            ? text
            : text + "." + microseconds.ToString("D6", CultureInfo.InvariantCulture);
    }
}
