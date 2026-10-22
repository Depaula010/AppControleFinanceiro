using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Domain.Settings;

/// <summary>Valor novo de uma coluna enviada (distingue "nÃ£o enviada" de <c>null</c> explÃ­cito).</summary>
public readonly record struct FieldChange<T>(T Value);

/// <summary>SÃ³ as colunas enviadas entram no <c>UPDATE</c> (spec 007 Â§3.2).</summary>
public sealed record ProfileChanges(
    FieldChange<string?>? Name,
    FieldChange<string?>? Email,
    FieldChange<string?>? City,
    FieldChange<string?>? State,
    FieldChange<string?>? TimeZone,
    FieldChange<int?>? ReserveMonths);

public enum ProfileError
{
    NothingToUpdate,
    MonthsNotInteger,
    NameTooShort,
    StateLength,
    MonthsRange,
    InvalidData,
    NameTooLong,
    EmailTooLong,
    CityTooLong,
    InvalidTimeZone,
}

public abstract record ProfileValidation
{
    private ProfileValidation()
    {
    }

    public sealed record Ok(ProfileChanges Changes) : ProfileValidation;

    public sealed record Rejected(ProfileError Error) : ProfileValidation;
}

/// <summary>
/// <c>PUT /api/user/profile</c> (spec 007 Â§3.2, <c>api.py</c>), na ordem da spec: nada a atualizar, conversÃ£o de
/// <c>meses</c>, nome, estado, faixa de meses; depois as regras dos desvios 084 e 085 (tipos, limites, fuso).
/// </summary>
public static class ProfileRules
{
    public const int NameMax = 255;
    public const int EmailMax = 255;
    public const int CityMax = 100;
    private const int TimeZoneMax = 50;

    private static readonly string[] Keys = ["nome", "email", "cidade", "estado", "fuso_horario", "meses_reserva_emergencia"];

    public static ProfileValidation Validate(RawBody? body)
    {
        if (body is null || !Keys.Any(body.Has))
        {
            return Reject(ProfileError.NothingToUpdate);
        }

        // 2. int() de meses; null Ã© aceito e grava NULL
        var hasMonths = body.Has("meses_reserva_emergencia");
        int? months = null;
        if (hasMonths && body.Get("meses_reserva_emergencia") is not RawValue.NullValue)
        {
            if (!PyNumber.TryInt(body.Get("meses_reserva_emergencia"), out var parsed))
            {
                return Reject(ProfileError.MonthsNotInteger);
            }
            months = parsed;
        }

        // 3. nome: validado depois do trim e gravado com trim (desvio 085)
        var hasName = body.Has("nome");
        string? name = null;
        if (hasName)
        {
            switch (body.Get("nome"))
            {
                case RawValue.TextValue t:
                    name = t.Value.Trim();
                    if (Length(name) < 2)
                    {
                        return Reject(ProfileError.NameTooShort);
                    }
                    break;
                case RawValue.NullValue:
                    return Reject(ProfileError.NameTooShort);
                default:
                    return Reject(ProfileError.InvalidData);
            }
        }

        // "" vira null (sÃ³ a string vazia; " " Ã© mantida) antes de qualquer validaÃ§Ã£o
        var email = ReadNullableText(body.Get("email"), out var emailOk);
        var city = ReadNullableText(body.Get("cidade"), out var cityOk);
        var state = ReadNullableText(body.Get("estado"), out var stateOk);

        // 4. estado
        if (!stateOk)
        {
            return Reject(ProfileError.InvalidData);
        }
        if (state is not null && Length(state) != 2)
        {
            return Reject(ProfileError.StateLength);
        }

        // 5. faixa de meses
        if (months is < 1 or > 36)
        {
            return Reject(ProfileError.MonthsRange);
        }

        // 6. tipos dos demais campos (desvio 084)
        if (!emailOk || !cityOk)
        {
            return Reject(ProfileError.InvalidData);
        }

        var hasTimeZone = body.Has("fuso_horario");
        string? timeZone = null;
        if (hasTimeZone)
        {
            switch (body.Get("fuso_horario"))
            {
                case RawValue.TextValue t:
                    timeZone = t.Value;
                    break;
                case RawValue.NullValue:
                    break;
                default:
                    return Reject(ProfileError.InvalidData);
            }
        }

        // 7. limites das colunas (desvio 085)
        if (name is not null && Length(name) > NameMax)
        {
            return Reject(ProfileError.NameTooLong);
        }
        if (email is not null && Length(email) > EmailMax)
        {
            return Reject(ProfileError.EmailTooLong);
        }
        if (city is not null && Length(city) > CityMax)
        {
            return Reject(ProfileError.CityTooLong);
        }

        // 8. fuso IANA conhecido; null tambÃ©m Ã© recusado
        if (hasTimeZone && !IsKnownTimeZone(timeZone))
        {
            return Reject(ProfileError.InvalidTimeZone);
        }

        return new ProfileValidation.Ok(new ProfileChanges(
            hasName ? new FieldChange<string?>(name) : null,
            body.Has("email") ? new FieldChange<string?>(email) : null,
            body.Has("cidade") ? new FieldChange<string?>(city) : null,
            body.Has("estado") ? new FieldChange<string?>(state) : null,
            hasTimeZone ? new FieldChange<string?>(timeZone) : null,
            hasMonths ? new FieldChange<int?>(months) : null));
    }

    /// <summary>Texto, <c>null</c> ou ausente (valor <c>null</c>); <c>""</c> vira <c>null</c>. Outro tipo: <paramref name="ok"/> falso.</summary>
    private static string? ReadNullableText(RawValue value, out bool ok)
    {
        ok = true;
        switch (value)
        {
            case RawValue.TextValue t:
                return t.Value.Length == 0 ? null : t.Value;
            case RawValue.AbsentValue or RawValue.NullValue:
                return null;
            default:
                ok = false;
                return null;
        }
    }

    /// <summary>Id IANA (sem espaÃ§os: ids do Windows nÃ£o valem) que o sistema conhece.</summary>
    private static bool IsKnownTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsWhiteSpace) || id.Length > TimeZoneMax)
        {
            return false;
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Comprimento em pontos de cÃ³digo, como o <c>len()</c> do Python e o <c>varchar(n)</c> do banco.</summary>
    private static int Length(string text) => text.EnumerateRunes().Count();

    private static ProfileValidation.Rejected Reject(ProfileError error) => new(error);
}
