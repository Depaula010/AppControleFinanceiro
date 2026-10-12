using System.Globalization;
using System.Numerics;
using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Domain.Settings;

/// <summary>Os seis valores que o <c>PUT /api/user/notifications</c> grava (PUT total, spec 007 §3.7).</summary>
public sealed record NotificationInput(
    bool MorningEnabled,
    string MorningTime,
    bool EveningEnabled,
    string EveningTime,
    bool FinancialEnabled,
    int DaysBeforeDue);

public enum NotificationError
{
    InvalidTimeFormat,
    EveningHourRange,
    InvalidData,
}

public abstract record NotificationValidation
{
    private NotificationValidation()
    {
    }

    public sealed record Ok(NotificationInput Input) : NotificationValidation;

    public sealed record Rejected(NotificationError Error) : NotificationValidation;
}

/// <summary>Padrões e leituras de apresentação (RN-007-07 e RN-007-08).</summary>
public static class NotificationDefaults
{
    public const string MorningTime = "07:00";
    public const string EveningTime = "19:00";
    public const int DaysBeforeDue = 3;

    /// <summary>O zero guardado é lido como o padrão (<c>or 3</c> do Flask).</summary>
    public static int ReadDaysBeforeDue(int stored) => stored == 0 ? DaysBeforeDue : stored;

    /// <summary>Os 5 primeiros caracteres do texto da hora (<c>07:00:00</c> vira <c>07:00</c>).</summary>
    public static string First5(string time) => time.Length <= 5 ? time : time[..5];
}

/// <summary>
/// <c>PUT /api/user/notifications</c> (spec 007 §3.7, <c>api.py:2730-2792</c>): ordem da spec (formato do check-in, hora 18 a 23)
/// seguida das regras dos desvios 084 e 085 (blocos e tipos, minutos, hora do resumo matinal).
/// </summary>
public static class NotificationRules
{
    public static NotificationValidation Validate(RawBody? body)
    {
        body ??= new RawBody(new Dictionary<string, RawValue>());

        if (!TryBlock(body, "morningBriefing", out var morning) ||
            !TryBlock(body, "eveningCheckIn", out var evening) ||
            !TryBlock(body, "financialAlerts", out var financial))
        {
            return Reject(NotificationError.InvalidData);
        }

        // 1 e 2. check-in noturno
        var eveningTime = ReadTime(evening, NotificationDefaults.EveningTime, out var eveningParts);
        if (eveningTime is null)
        {
            return Reject(NotificationError.InvalidTimeFormat);
        }
        if (eveningParts.Hour is < 18 or > 23)
        {
            return Reject(NotificationError.EveningHourRange);
        }
        if (eveningParts.Minute is < 0 or > 59)
        {
            return Reject(NotificationError.InvalidTimeFormat);
        }

        // 3. resumo matinal (desvio 085)
        var morningTime = ReadTime(morning, NotificationDefaults.MorningTime, out var morningParts);
        if (morningTime is null || morningParts.Hour is < 0 or > 23 || morningParts.Minute is < 0 or > 59)
        {
            return Reject(NotificationError.InvalidTimeFormat);
        }

        // 4. tipos (desvio 084)
        if (!TryEnabled(morning, out var morningEnabled) ||
            !TryEnabled(evening, out var eveningEnabled) ||
            !TryEnabled(financial, out var financialEnabled) ||
            !TryDays(financial.Get("daysBeforeDue"), out var days))
        {
            return Reject(NotificationError.InvalidData);
        }

        return new NotificationValidation.Ok(new NotificationInput(
            morningEnabled, morningTime, eveningEnabled, eveningTime, financialEnabled, days));
    }

    /// <summary>Ausente = bloco vazio; objeto = o bloco; qualquer outro valor (inclusive <c>null</c>) é inválido.</summary>
    private static bool TryBlock(RawBody body, string name, out RawBody block)
    {
        block = new RawBody(new Dictionary<string, RawValue>());
        switch (body.Get(name))
        {
            case RawValue.AbsentValue:
                return true;
            case ObjectValue o:
                block = o.Body;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// <c>split(":")</c> em exatamente 2 partes, cada uma aceita pelo <c>int()</c>; devolve o texto canônico <c>HH:MM</c>
    /// (<c>null</c> se o formato não vale). A faixa de cada parte fica com o chamador.
    /// </summary>
    private static string? ReadTime(RawBody block, string fallback, out (int Hour, int Minute) parts)
    {
        parts = default;
        var raw = block.Get("time");
        var text = raw is RawValue.AbsentValue ? fallback : raw is RawValue.TextValue t ? t.Value : null;
        if (text is null)
        {
            return null;
        }

        var pieces = text.Split(':');
        if (pieces.Length != 2 ||
            !PyNumber.TryInt(RawValue.Text(pieces[0]), out var hour) ||
            !PyNumber.TryInt(RawValue.Text(pieces[1]), out var minute))
        {
            return null;
        }

        parts = (hour, minute);
        return Format(parts);
    }

    private static string Format((int Hour, int Minute) parts) =>
        string.Create(CultureInfo.InvariantCulture, $"{parts.Hour:D2}:{parts.Minute:D2}");

    private static bool TryEnabled(RawBody block, out bool enabled)
    {
        enabled = true;
        switch (block.Get("enabled"))
        {
            case RawValue.AbsentValue:
                return true;
            case RawValue.BoolValue b:
                enabled = b.Value;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Inteiro JSON, texto de inteiro ou fração (arredondada como o banco); sem faixa. Outros tipos: inválido.</summary>
    private static bool TryDays(RawValue raw, out int days)
    {
        days = NotificationDefaults.DaysBeforeDue;
        switch (raw)
        {
            case RawValue.AbsentValue:
                return true;
            case RawValue.IntegerValue or RawValue.TextValue:
                return PyNumber.TryInt(raw, out days);
            case RawValue.DecimalValue d when double.IsFinite(d.Value):
                var rounded = Math.Round(d.Value, MidpointRounding.AwayFromZero);
                return PyNumber.TryInt(RawValue.Whole(new BigInteger(rounded)), out days);
            default:
                return false;
        }
    }

    private static NotificationValidation.Rejected Reject(NotificationError error) => new(error);
}
