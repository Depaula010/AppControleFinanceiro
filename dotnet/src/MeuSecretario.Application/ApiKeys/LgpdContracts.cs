using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.Application.ApiKeys;

// ---------------------------------------------------------------- linhas lidas do banco

public sealed record ConsentRow(int Id, string Type, bool Given, string Version, DateTime? ConsentedAt);

/// <summary><c>StoredKey</c> é o token Fernet (ou texto puro legado) como está no banco.</summary>
public sealed record ExportKeyRow(int Id, string Provider, string StoredKey, bool? Active, DateTime? LastUsedAt, DateTime? CreatedAt, DateTime? UpdatedAt);

public sealed record ExportUsageRow(string Provider, string KeyType, int? Calls, string Month);

public sealed record ExportLogRow(string Provider, string KeyType, bool Success, string? ErrorMessage, DateTime? AccessedAt);

public sealed record SubscriptionRow(
    int Id, string PlanName, decimal MonthlyPrice, int? GeminiLimit, int? WeatherLimit, int? OpenRouteLimit,
    DateTime? StartDate, DateTime? EndDate, string Status);

public sealed record ExportData(
    IReadOnlyList<ExportKeyRow> Keys,
    IReadOnlyList<PreferenceRow> Preferences,
    IReadOnlyList<ExportUsageRow> Usage,
    IReadOnlyList<ExportLogRow> Logs,
    SubscriptionRow? Subscription);

/// <summary>Linhas apagadas por <c>deletar-conta</c> (§4.14); cascatas (<c>faturas</c> etc.) não entram.</summary>
public sealed record AccountDeletionCounts(
    int ApiKeys, int Preferences, int Logs, int Usage, int Consents, int Subscriptions,
    int Transactions, int Schedules, int Accounts, int Pots, int Subcategories, int Macrocategories, int Users)
{
    public int Total =>
        ApiKeys + Preferences + Logs + Usage + Consents + Subscriptions
        + Transactions + Schedules + Accounts + Pots + Subcategories + Macrocategories + Users;
}

public abstract record UpsertConsentResult
{
    public sealed record Saved(int Id) : UpsertConsentResult;

    public sealed record UserNotFound : UpsertConsentResult;
}

// ---------------------------------------------------------------- portas

public interface IConsentRepository
{
    /// <summary>Upsert por (usuário, tipo, versão); <c>false</c> grava <c>revogado_em</c>, <c>true</c> o zera (desvio 041).</summary>
    Task<UpsertConsentResult> UpsertAsync(int userId, string type, bool given, string version, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConsentRow>> ListAsync(int userId, CancellationToken cancellationToken);

    /// <summary><c>consentimento_dado</c> do registro mais recente do tipo; <c>null</c> se não houver.</summary>
    Task<bool?> LatestGivenAsync(int userId, string type, CancellationToken cancellationToken);
}

public interface IAccountDataRepository
{
    /// <summary>Todas as leituras da exportação (§4.13), em uma conexão e um snapshot.</summary>
    Task<ExportData> ExportAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Apaga a conta de fato numa transação, em ordem explícita (§4.14); <c>null</c> se o usuário não existe.</summary>
    Task<AccountDeletionCounts?> DeleteAccountCompletelyAsync(int userId, CancellationToken cancellationToken);
}

/// <summary>
/// Apaga o cache do usuário (chaves Redis <c>pending_*</c> e <c>nightly_checkin*</c>) depois de <c>deletar-conta</c> (decisão Q5).
/// A implementação padrão não faz nada; a 010a (cliente Redis) a substitui.
/// </summary>
public interface IUserCacheCleaner
{
    Task ClearAsync(int userId, CancellationToken cancellationToken);
}

public sealed class NullUserCacheCleaner : IUserCacheCleaner
{
    public Task ClearAsync(int userId, CancellationToken cancellationToken) => Task.CompletedTask;
}
