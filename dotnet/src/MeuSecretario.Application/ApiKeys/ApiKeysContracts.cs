using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.Application.ApiKeys;

// ---------------------------------------------------------------- linhas lidas do banco

/// <summary>Chave listada (<c>GET /usuario/&lt;id&gt;</c>); nunca carrega a chave. <c>Active</c> é anulável no banco.</summary>
public sealed record ApiKeyRow(int Id, string Provider, bool? Active, DateTime? LastUsedAt, DateTime? CreatedAt, DateTime? UpdatedAt);

/// <summary>Provedor e dono da chave afetada por um <c>UPDATE ... RETURNING</c>.</summary>
public sealed record KeyRef(string Provider, int UserId);

public sealed record PreferenceRow(int Id, string Provider, bool UseOwnKey, DateTime? UpdatedAt);

public sealed record AccessLogRow(int Id, string Provider, string KeyType, string Operation, bool Success, string? ErrorMessage, DateTime? CreatedAt);

/// <summary>Plano ativo do usuário; limite <c>null</c> = ilimitado.</summary>
public sealed record ActivePlan(string PlanName, int? GeminiLimit, int? WeatherLimit, int? OpenRouteLimit)
{
    public int? LimitFor(string provider) => provider switch
    {
        ApiProviders.Gemini => GeminiLimit,
        ApiProviders.Weather => WeatherLimit,
        ApiProviders.OpenRoute => OpenRouteLimit,
        _ => null,
    };
}

// ---------------------------------------------------------------- resultados de gravação

public abstract record InsertKeyResult
{
    public sealed record Created(int Id) : InsertKeyResult;

    public sealed record UserNotFound : InsertKeyResult;

    public sealed record AlreadyExists : InsertKeyResult;
}

public abstract record UpsertPreferenceResult
{
    public sealed record Saved(int Id) : UpsertPreferenceResult;

    public sealed record UserNotFound : UpsertPreferenceResult;
}

// ---------------------------------------------------------------- portas

public interface IApiKeyRepository
{
    Task<InsertKeyResult> InsertAsync(int userId, string provider, string encryptedKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiKeyRow>> ListByUserAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Atualiza só se a chave pertencer a <paramref name="ownerUserId"/> (desvios 006 e 045); <c>null</c> se não houver linha.</summary>
    Task<KeyRef?> UpdateAsync(int keyId, int ownerUserId, string? encryptedKey, bool? active, CancellationToken cancellationToken);

    /// <summary>Soft delete (<c>ativo = false</c>) só se for do dono; <c>null</c> se não houver linha.</summary>
    Task<KeyRef?> DeactivateAsync(int keyId, int ownerUserId, CancellationToken cancellationToken);

    /// <summary>Token (ou texto puro legado) da chave ativa do provedor; <c>null</c> se não houver.</summary>
    Task<string?> FindActiveEncryptedAsync(int userId, string provider, CancellationToken cancellationToken);

    Task TouchLastUseAsync(int userId, string provider, CancellationToken cancellationToken);
}

public interface IPreferenceRepository
{
    Task<UpsertPreferenceResult> UpsertAsync(int userId, string provider, bool useOwnKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreferenceRow>> ListAsync(int userId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int userId, string provider, CancellationToken cancellationToken);

    /// <summary><c>null</c> se não houver preferência.</summary>
    Task<bool?> FindAsync(int userId, string provider, CancellationToken cancellationToken);
}

public interface IUsageRepository
{
    Task<IReadOnlyList<UsageRow>> QueryMonthAsync(int userId, string month, string? provider, CancellationToken cancellationToken);

    /// <summary>Upsert com <c>+1</c> em (usuário, provedor, tipo de chave, mês).</summary>
    Task IncrementAsync(int userId, string provider, string keyType, string month, CancellationToken cancellationToken);

    /// <summary>Soma do mês só do tipo <c>sistema</c> (desvio 046).</summary>
    Task<long> SumSystemMonthAsync(int userId, string provider, string month, CancellationToken cancellationToken);
}

public interface IAccessLogRepository
{
    Task<IReadOnlyList<AccessLogRow>> ListAsync(int userId, string? provider, bool? success, int limit, CancellationToken cancellationToken);

    Task InsertAsync(int userId, string provider, string keyType, string operation, bool success, string? errorMessage, CancellationToken cancellationToken);
}

public interface IPlanRepository
{
    /// <summary>Assinatura com <c>status = 'ativo'</c> e <c>data_fim</c> nula ou futura; <c>null</c> = sem limite.</summary>
    Task<ActivePlan?> FindActivePlanAsync(int userId, CancellationToken cancellationToken);
}

/// <summary>Chave do sistema por provedor (<c>GEMINI_API_KEY</c>, <c>WEATHER_API_KEY</c>, <c>OPENROUTE_API_KEY</c>); vazia = indisponível.</summary>
public interface ISystemApiKeys
{
    string? KeyFor(string provider);
}

/// <summary>
/// Destino das falhas engolidas de propósito (último uso, contagem, log, limite, leitura de preferência).
/// Recebe só o nome da operação e a descrição segura do erro (nunca chave, corpo ou mensagem do banco).
/// </summary>
public interface IApiKeyDiagnostics
{
    void Swallowed(string operation, string description);
}

/// <summary>Descrição de exceção segura para log: tipo e, se houver, o <c>SqlState</c> (desvio 008).</summary>
public static class ErrorText
{
    public static string Describe(Exception exception)
    {
        var sqlState = exception.GetType().GetProperty("SqlState")?.GetValue(exception) as string;
        return sqlState is null ? exception.GetType().Name : $"{exception.GetType().Name} (SqlState {sqlState})";
    }
}
