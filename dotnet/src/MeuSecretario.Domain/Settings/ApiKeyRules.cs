using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.Domain.Settings;

public enum SaveKeyError
{
    InvalidProvider,
    InvalidData,
    KeyRequired,
}

public abstract record SaveKeyValidation
{
    private SaveKeyValidation()
    {
    }

    /// <summary><see cref="RawKey"/> já sem <c>trim</c> (RN-007-03); vazio quando <see cref="UseOwnKey"/> é falso e nada foi enviado.</summary>
    public sealed record Ok(string Provider, bool UseOwnKey, string RawKey) : SaveKeyValidation;

    public sealed record Rejected(SaveKeyError Error) : SaveKeyValidation;
}

public enum ValidateKeyError
{
    MissingFields,
    UnsupportedProvider,
}

public abstract record ValidateKeyValidation
{
    private ValidateKeyValidation()
    {
    }

    public sealed record Ok(string Provider, string RawKey) : ValidateKeyValidation;

    public sealed record Rejected(ValidateKeyError Error) : ValidateKeyValidation;
}

/// <summary>
/// Leitura de <c>type</c> (ou <c>provedor</c>), <c>useOwnKey</c> e <c>key</c> das rotas <c>PUT /api-keys</c> e
/// <c>POST /api-keys/validate</c> (spec 007 §3.4 e §3.5, <c>api.py</c>) com o desvio 084 (tipos errados dão 400).
/// </summary>
public static class ApiKeyRules
{
    /// <summary><c>type or provedor</c>: <c>""</c>, <c>null</c> e ausente caem no outro campo.</summary>
    private static RawValue ChosenProvider(RawBody body)
    {
        var type = body.Get("type");
        return PyTruth.IsTruthy(type) ? type : body.Get("provedor");
    }

    public static SaveKeyValidation ValidateSave(RawBody? body)
    {
        body ??= new RawBody(new Dictionary<string, RawValue>());

        // 1. provedor: comparação exata; qualquer outro tipo JSON é inválido
        if (ChosenProvider(body) is not RawValue.TextValue { Value: var provider } || !ApiProviders.IsValid(provider))
        {
            return new SaveKeyValidation.Rejected(SaveKeyError.InvalidProvider);
        }

        // useOwnKey: ausente e null valem falso; só booleano é aceito (desvio 084)
        var useOwn = false;
        switch (body.Get("useOwnKey"))
        {
            case RawValue.AbsentValue or RawValue.NullValue:
                break;
            case RawValue.BoolValue b:
                useOwn = b.Value;
                break;
            default:
                return new SaveKeyValidation.Rejected(SaveKeyError.InvalidData);
        }

        // key presente e que não seja texto (inclusive null) ⇒ chave obrigatória (desvio 084)
        var rawKey = string.Empty;
        switch (body.Get("key"))
        {
            case RawValue.AbsentValue:
                break;
            case RawValue.TextValue t:
                rawKey = t.Value.Trim();
                break;
            default:
                return new SaveKeyValidation.Rejected(SaveKeyError.KeyRequired);
        }

        // 2. chave obrigatória quando usar chave própria
        if (useOwn && rawKey.Length == 0)
        {
            return new SaveKeyValidation.Rejected(SaveKeyError.KeyRequired);
        }

        return new SaveKeyValidation.Ok(provider, useOwn, rawKey);
    }

    public static ValidateKeyValidation ValidateCheck(RawBody? body)
    {
        body ??= new RawBody(new Dictionary<string, RawValue>());

        var chosen = ChosenProvider(body);
        var key = body.Get("key") is RawValue.TextValue t ? t.Value.Trim() : string.Empty;

        // 1. provedor falso ou chave vazia (ou que não seja texto)
        if (!PyTruth.IsTruthy(chosen) || key.Length == 0)
        {
            return new ValidateKeyValidation.Rejected(ValidateKeyError.MissingFields);
        }

        // 2. provedor fora da lista (antes de qualquer chamada de rede)
        if (chosen is not RawValue.TextValue { Value: var provider } || !ApiProviders.IsValid(provider))
        {
            return new ValidateKeyValidation.Rejected(ValidateKeyError.UnsupportedProvider);
        }

        return new ValidateKeyValidation.Ok(provider, key);
    }
}
