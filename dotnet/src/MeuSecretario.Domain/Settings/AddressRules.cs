using System.Globalization;
using System.Numerics;
using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Domain.Settings;

/// <summary>Rótulos de endereço favorito (RN-007-10; também o CHECK do banco).</summary>
public static class AddressLabels
{
    public const string Home = "casa";
    public const string Work = "trabalho";
    public const string Other = "outro";

    /// <summary>Texto fixo da mensagem (lista ordenada alfabeticamente, como o <c>sorted</c> do Flask).</summary>
    public const string ValidListText = "casa, outro, trabalho";

    public static IReadOnlyList<string> All { get; } = [Home, Work, Other];

    /// <summary>Entrada do <c>POST</c>: <c>trim</c> + minúsculas.</summary>
    public static bool TryNormalize(string? raw, out string label)
    {
        label = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return All.Contains(label, StringComparer.Ordinal);
    }

    /// <summary>Parâmetro do <c>DELETE</c>: só minúsculas (o Flask não faz <c>trim</c> aqui).</summary>
    public static bool TryNormalizePath(string? raw, out string label)
    {
        label = (raw ?? string.Empty).ToLowerInvariant();
        return All.Contains(label, StringComparer.Ordinal);
    }
}

/// <summary>Coordenadas gravadas (qualquer lado pode faltar; a leitura só as devolve se ambas existirem).</summary>
public sealed record AddressCoordinates(decimal? Latitude, decimal? Longitude);

public enum SaveAddressError
{
    InvalidLabel,
    AddressRequired,
    InvalidData,
    InvalidCoordinates,
}

public abstract record SaveAddressValidation
{
    private SaveAddressValidation()
    {
    }

    /// <summary><see cref="Coordinates"/> nulo = a chave não veio (ou veio vazia): preserva as coordenadas se o texto não mudou (desvio 086).</summary>
    public sealed record Ok(string Label, string Address, AddressCoordinates? Coordinates) : SaveAddressValidation;

    public sealed record Rejected(SaveAddressError Error) : SaveAddressValidation;
}

/// <summary><c>POST /api/user/addresses</c> (spec 007 §3.9, <c>api.py</c>) com os desvios 084, 086 e 087.</summary>
public static class AddressRules
{
    private const decimal MaxLatitude = 90m;
    private const decimal MaxLongitude = 180m;

    public static SaveAddressValidation Validate(RawBody? body)
    {
        body ??= new RawBody(new Dictionary<string, RawValue>());

        // 1. rótulo: não-texto não pode ser um rótulo válido
        if (body.Get("label") is not RawValue.TextValue { Value: var rawLabel } || !AddressLabels.TryNormalize(rawLabel, out var label))
        {
            return Reject(SaveAddressError.InvalidLabel);
        }

        // 2. endereço: ausente, null, vazio, só espaços ou de outro tipo
        if (body.Get("address") is not RawValue.TextValue { Value: var rawAddress } || rawAddress.Trim().Length == 0)
        {
            return Reject(SaveAddressError.AddressRequired);
        }

        // 3. coordinates: falso (ausente, null, {}, [], 0, "") = não enviado; objeto = lido; outro valor = inválido
        var raw = body.Get("coordinates");
        AddressCoordinates? coordinates = null;
        if (PyTruth.IsTruthy(raw))
        {
            if (raw is not ObjectValue obj)
            {
                return Reject(SaveAddressError.InvalidData);
            }

            if (!TryCoordinate(obj.Body.Get("lat"), MaxLatitude, out var lat) ||
                !TryCoordinate(obj.Body.Get("lng"), MaxLongitude, out var lng))
            {
                return Reject(SaveAddressError.InvalidCoordinates);
            }
            coordinates = new AddressCoordinates(lat, lng);
        }

        return new SaveAddressValidation.Ok(label, rawAddress.Trim(), coordinates);
    }

    /// <summary>Ausente e <c>null</c> viram <c>NULL</c>; número ou texto numérico dentro da faixa vale (desvio 087).</summary>
    private static bool TryCoordinate(RawValue raw, decimal limit, out decimal? value)
    {
        value = null;
        decimal parsed;
        switch (raw)
        {
            case RawValue.AbsentValue or RawValue.NullValue:
                return true;
            case RawValue.IntegerValue i:
                if (BigInteger.Abs(i.Value) > new BigInteger(limit))
                {
                    return false;
                }
                parsed = (decimal)i.Value;
                break;
            case RawValue.DecimalValue d:
                if (!double.IsFinite(d.Value) ||
                    !decimal.TryParse(d.Value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    return false;
                }
                break;
            case RawValue.TextValue t:
                if (!decimal.TryParse(t.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    return false;
                }
                break;
            default:
                return false;
        }

        if (Math.Abs(parsed) > limit)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static SaveAddressValidation.Rejected Reject(SaveAddressError error) => new(error);
}
