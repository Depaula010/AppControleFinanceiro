using System.Globalization;
using System.Numerics;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.Application.ApiKeys;

// =============================================================== POST /usuario/<id>

/// <param name="Provider">Texto do corpo; <c>null</c> se ausente.</param>
public sealed record RegisterKeyInput(int UserId, string? Provider, string? ApiKey);

public abstract record RegisterKeyResult
{
    public sealed record Success(int Id, string Provider, int UserId) : RegisterKeyResult;

    public sealed record MissingFields : RegisterKeyResult;

    public sealed record InvalidProvider : RegisterKeyResult;

    public sealed record UserNotFound : RegisterKeyResult;

    public sealed record AlreadyExists : RegisterKeyResult;
}

/// <summary>Cadastra a chave cifrada com Fernet, sem <c>trim</c> (§4.1; desvios 042 e 043).</summary>
public sealed class RegisterApiKey(IApiKeyRepository keys, IEncryptionService encryption)
{
    public async Task<RegisterKeyResult> ExecuteAsync(RegisterKeyInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(input.Provider) || string.IsNullOrEmpty(input.ApiKey))
        {
            return new RegisterKeyResult.MissingFields();
        }

        if (!ApiProviders.IsValid(input.Provider))
        {
            return new RegisterKeyResult.InvalidProvider();
        }

        var result = await keys.InsertAsync(input.UserId, input.Provider, encryption.Encrypt(input.ApiKey), cancellationToken);
        return result switch
        {
            InsertKeyResult.Created created => new RegisterKeyResult.Success(created.Id, input.Provider, input.UserId),
            InsertKeyResult.UserNotFound => new RegisterKeyResult.UserNotFound(),
            InsertKeyResult.AlreadyExists => new RegisterKeyResult.AlreadyExists(),
            _ => throw new InvalidOperationException("Resultado de cadastro de chave desconhecido."),
        };
    }
}

// =============================================================== GET /usuario/<id>

public sealed class ListApiKeys(IApiKeyRepository keys)
{
    public Task<IReadOnlyList<ApiKeyRow>> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        keys.ListByUserAsync(userId, cancellationToken);
}

// =============================================================== PUT /chave/<id>

/// <param name="OwnerUserId">Inteiro do corpo; <c>null</c> se ausente/nulo (400, desvio 045).</param>
public sealed record UpdateKeyInput(int KeyId, int? OwnerUserId, string? ApiKey, bool? Active);

public abstract record UpdateKeyResult
{
    public sealed record Success(int KeyId, string Provider, int OwnerUserId) : UpdateKeyResult;

    public sealed record NothingToUpdate : UpdateKeyResult;

    public sealed record OwnerRequired : UpdateKeyResult;

    public sealed record NotFound : UpdateKeyResult;
}

/// <summary>Troca a chave e/ou o <c>ativo</c> (§4.3). Só o dono pode (desvios 006 e 045).</summary>
public sealed class UpdateApiKey(IApiKeyRepository keys, IEncryptionService encryption)
{
    public async Task<UpdateKeyResult> ExecuteAsync(UpdateKeyInput input, CancellationToken cancellationToken)
    {
        var hasKey = !string.IsNullOrEmpty(input.ApiKey);
        if (!hasKey && input.Active is null)
        {
            return new UpdateKeyResult.NothingToUpdate();
        }

        if (input.OwnerUserId is not > 0)
        {
            return new UpdateKeyResult.OwnerRequired();
        }

        var updated = await keys.UpdateAsync(
            input.KeyId, input.OwnerUserId.Value, hasKey ? encryption.Encrypt(input.ApiKey!) : null, input.Active, cancellationToken);

        return updated is null
            ? new UpdateKeyResult.NotFound()
            : new UpdateKeyResult.Success(input.KeyId, updated.Provider, updated.UserId);
    }
}

// =============================================================== DELETE /chave/<id>

public abstract record DeactivateKeyResult
{
    public sealed record Success(int KeyId, string Provider) : DeactivateKeyResult;

    public sealed record OwnerRequired : DeactivateKeyResult;

    public sealed record NotFound : DeactivateKeyResult;
}

public sealed class DeactivateApiKey(IApiKeyRepository keys)
{
    /// <param name="ownerUserId"><c>null</c> se ausente; <c>-1</c> (valor não inteiro) nunca é dono de nada.</param>
    public async Task<DeactivateKeyResult> ExecuteAsync(int keyId, int? ownerUserId, CancellationToken cancellationToken)
    {
        if (ownerUserId is null)
        {
            return new DeactivateKeyResult.OwnerRequired();
        }

        var deactivated = await keys.DeactivateAsync(keyId, ownerUserId.Value, cancellationToken);
        return deactivated is null
            ? new DeactivateKeyResult.NotFound()
            : new DeactivateKeyResult.Success(keyId, deactivated.Provider);
    }
}

// =============================================================== preferências

public sealed record SetPreferenceInput(int UserId, string? Provider, bool? UseOwnKey);

public abstract record SetPreferenceResult
{
    public sealed record Success(int Id, string Provider, bool UseOwnKey) : SetPreferenceResult;

    public sealed record MissingFields : SetPreferenceResult;

    public sealed record InvalidProvider : SetPreferenceResult;

    public sealed record UserNotFound : SetPreferenceResult;
}

public sealed class SetPreference(IPreferenceRepository preferences)
{
    public async Task<SetPreferenceResult> ExecuteAsync(SetPreferenceInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(input.Provider) || input.UseOwnKey is null)
        {
            return new SetPreferenceResult.MissingFields();
        }

        if (!ApiProviders.IsValid(input.Provider))
        {
            return new SetPreferenceResult.InvalidProvider();
        }

        var result = await preferences.UpsertAsync(input.UserId, input.Provider, input.UseOwnKey.Value, cancellationToken);
        return result switch
        {
            UpsertPreferenceResult.Saved saved => new SetPreferenceResult.Success(saved.Id, input.Provider, input.UseOwnKey.Value),
            UpsertPreferenceResult.UserNotFound => new SetPreferenceResult.UserNotFound(),
            _ => throw new InvalidOperationException("Resultado de preferência desconhecido."),
        };
    }
}

public sealed class ListPreferences(IPreferenceRepository preferences)
{
    public Task<IReadOnlyList<PreferenceRow>> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        preferences.ListAsync(userId, cancellationToken);
}

public sealed class DeletePreference(IPreferenceRepository preferences)
{
    /// <returns><c>true</c> se havia preferência e foi removida.</returns>
    public Task<bool> ExecuteAsync(int userId, string provider, CancellationToken cancellationToken) =>
        preferences.DeleteAsync(userId, provider, cancellationToken);
}

// =============================================================== GET /uso/<id>

public sealed record UsageResult(string Month, UsageSummary Summary, IReadOnlyList<UsageRow> Details);

/// <summary>Uso do mês (§4.8). Mês padrão = mês atual de São Paulo; qualquer texto é aceito; vazio conta como ausente.</summary>
public sealed class GetUsage(IUsageRepository usage, IClock clock)
{
    public async Task<UsageResult> ExecuteAsync(int userId, string? month, string? provider, CancellationToken cancellationToken)
    {
        var effectiveMonth = string.IsNullOrEmpty(month) ? UsageMonth.Of(clock.Now) : month;
        var rows = await usage.QueryMonthAsync(userId, effectiveMonth, string.IsNullOrEmpty(provider) ? null : provider, cancellationToken);
        return new UsageResult(effectiveMonth, UsageSummary.From(rows), rows);
    }
}

// =============================================================== GET /logs/<id>

public sealed record AccessLogQuery(int UserId, string? Provider, string? Success, string? Limit);

public abstract record AccessLogsResult
{
    public sealed record Success(IReadOnlyList<AccessLogRow> Logs) : AccessLogsResult;

    /// <summary><c>limit</c> não inteiro ou negativo: o Flask dá 500.</summary>
    public sealed record InvalidLimit : AccessLogsResult;
}

/// <summary>
/// Logs de acesso (§4.9). <c>limit</c> segue o <c>int()</c> do Python (espaços e sinal aceitos), padrão 50, teto 500.
/// <c>sucesso</c> presente (mesmo vazio): <c>true</c> sem diferenciar maiúsculas filtra verdadeiros, qualquer outro texto falsos.
/// </summary>
public sealed class GetAccessLogs(IAccessLogRepository logs)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 500;

    public async Task<AccessLogsResult> ExecuteAsync(AccessLogQuery query, CancellationToken cancellationToken)
    {
        var limit = DefaultLimit;
        if (query.Limit is not null)
        {
            if (!BigInteger.TryParse(
                    query.Limit,
                    NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var parsed) || parsed.Sign < 0)
            {
                return new AccessLogsResult.InvalidLimit();
            }

            limit = (int)BigInteger.Min(parsed, MaxLimit);
        }

        bool? success = query.Success is null ? null : string.Equals(query.Success, "true", StringComparison.OrdinalIgnoreCase);
        var rows = await logs.ListAsync(
            query.UserId, string.IsNullOrEmpty(query.Provider) ? null : query.Provider, success, limit, cancellationToken);
        return new AccessLogsResult.Success(rows);
    }
}
