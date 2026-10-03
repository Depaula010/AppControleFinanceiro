using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.Application.ApiKeys;

// =============================================================== POST /lgpd/consentimento

/// <param name="UserId">Inteiro do corpo; <c>null</c> se ausente/nulo.</param>
public sealed record RegisterConsentInput(int? UserId, string? Type, bool? Given, string? Version);

public abstract record RegisterConsentResult
{
    public sealed record Success(int Id, string Type, bool Given) : RegisterConsentResult;

    public sealed record MissingFields : RegisterConsentResult;

    public sealed record InvalidType(string Message) : RegisterConsentResult;

    public sealed record UserNotFound(int UserId) : RegisterConsentResult;
}

/// <summary>Registra, atualiza ou revoga um consentimento (§4.10; desvios 041 e 043). Versão padrão "1.0"; IP não capturado.</summary>
public sealed class RegisterConsent(IConsentRepository consents)
{
    public async Task<RegisterConsentResult> ExecuteAsync(RegisterConsentInput input, CancellationToken cancellationToken)
    {
        if (input.UserId is not > 0 || string.IsNullOrEmpty(input.Type) || input.Given is null)
        {
            return new RegisterConsentResult.MissingFields();
        }

        if (!ConsentCatalog.IsValid(input.Type))
        {
            return new RegisterConsentResult.InvalidType(ConsentCatalog.InvalidTypeMessage(input.Type));
        }

        var version = string.IsNullOrEmpty(input.Version) ? ConsentCatalog.CurrentVersion : input.Version;
        var result = await consents.UpsertAsync(input.UserId.Value, input.Type, input.Given.Value, version, cancellationToken);
        return result switch
        {
            UpsertConsentResult.Saved saved => new RegisterConsentResult.Success(saved.Id, input.Type, input.Given.Value),
            UpsertConsentResult.UserNotFound => new RegisterConsentResult.UserNotFound(input.UserId.Value),
            _ => throw new InvalidOperationException("Resultado de consentimento desconhecido."),
        };
    }
}

// =============================================================== GET /lgpd/consentimentos/<id>

/// <summary>
/// Lista os consentimentos (§4.11). Erro de banco devolve lista vazia, como o serviço do Flask
/// (que é o que escondia o bug de coluna); a falha vai para o log.
/// </summary>
public sealed class ListConsents(IConsentRepository consents, IApiKeyDiagnostics diagnostics)
{
    public async Task<IReadOnlyList<ConsentRow>> ExecuteAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            return await consents.ListAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Swallowed("listar_consentimentos", ErrorText.Describe(ex));
            return [];
        }
    }
}

// =============================================================== GET /lgpd/verificar-consentimento/<id>/<tipo>

/// <summary>Consentimento ativo = o registro mais recente do tipo tem <c>consentimento_dado = true</c>; qualquer erro ⇒ <c>false</c> (fail-closed).</summary>
public sealed class CheckConsent(IConsentRepository consents, IApiKeyDiagnostics diagnostics)
{
    public async Task<bool> ExecuteAsync(int userId, string type, CancellationToken cancellationToken)
    {
        try
        {
            return await consents.LatestGivenAsync(userId, type, cancellationToken) ?? false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Swallowed("verificar_consentimento", ErrorText.Describe(ex));
            return false;
        }
    }
}

// =============================================================== GET /lgpd/exportar/<id>

public sealed record ExportedKey(int Id, string Provider, string Key, bool? Active, DateTime? LastUsedAt, DateTime? CreatedAt, DateTime? UpdatedAt);

public sealed record ExportedSubscription(
    int Id, string PlanName, decimal MonthlyPrice, int? GeminiLimit, int? WeatherLimit, int? OpenRouteLimit,
    DateTime? StartDate, DateTime? EndDate, bool Active);

public sealed record ExportedUserData(
    int UserId,
    DateTime ExportedAt,
    IReadOnlyList<ExportedKey> Keys,
    IReadOnlyList<PreferenceRow> Preferences,
    IReadOnlyList<ExportUsageRow> Usage,
    IReadOnlyList<ExportLogRow> Logs,
    IReadOnlyList<ConsentRow> Consents,
    ExportedSubscription? Subscription);

/// <summary>
/// Exportação LGPD (§4.13). As chaves saem decifradas (portabilidade; desvio 051 rejeitado). Texto puro legado passa como veio;
/// token com formato Fernet que não decifra lança exceção (500), nunca devolve o texto cifrado como se fosse a chave (Q7).
/// </summary>
public sealed class ExportUserData(
    IAccountDataRepository data, IConsentRepository consents, IEncryptionService encryption, IClock clock)
{
    public async Task<ExportedUserData> ExecuteAsync(int userId, CancellationToken cancellationToken)
    {
        var exported = await data.ExportAsync(userId, cancellationToken);
        var consentRows = await consents.ListAsync(userId, cancellationToken);

        var keys = exported.Keys
            .Select(k => new ExportedKey(
                k.Id, k.Provider, encryption.LooksEncrypted(k.StoredKey) ? encryption.Decrypt(k.StoredKey) : k.StoredKey,
                k.Active, k.LastUsedAt, k.CreatedAt, k.UpdatedAt))
            .ToList();

        var now = clock.UtcNow.UtcDateTime;
        var subscription = exported.Subscription is { } s
            ? new ExportedSubscription(
                s.Id, s.PlanName, s.MonthlyPrice, s.GeminiLimit, s.WeatherLimit, s.OpenRouteLimit, s.StartDate, s.EndDate,
                Active: s.Status == "ativo" && (s.EndDate is null || DateTime.SpecifyKind(s.EndDate.Value, DateTimeKind.Utc) > now))
            : null;

        return new ExportedUserData(userId, clock.Now, keys, exported.Preferences, exported.Usage, exported.Logs, consentRows, subscription);
    }
}

// =============================================================== DELETE /lgpd/deletar-conta/<id>

/// <param name="Confirmation">Texto do corpo; <c>null</c> se ausente ou não for texto.</param>
public sealed record DeleteAccountInput(int UserId, string? Confirmation);

public abstract record DeleteAccountResult
{
    public sealed record Success(int UserId, AccountDeletionCounts Counts) : DeleteAccountResult;

    public sealed record NotConfirmed : DeleteAccountResult;

    public sealed record UserNotFound(int UserId) : DeleteAccountResult;
}

/// <summary>
/// Deleta a conta de fato (§4.14; desvio 044): confirmação exata, limpeza do cache do usuário e uma transação em ordem
/// explícita. A limpeza vem ANTES da deleção porque as chaves do Redis usam o número de WhatsApp, que só existe enquanto a
/// linha de <c>usuarios</c> existe; falha na limpeza é registrada e não impede a deleção. Para um usuário inexistente a
/// limpeza não encontra número e não faz nada.
/// </summary>
public sealed class DeleteAccount(IAccountDataRepository data, IUserCacheCleaner cache, IApiKeyDiagnostics diagnostics)
{
    public async Task<DeleteAccountResult> ExecuteAsync(DeleteAccountInput input, CancellationToken cancellationToken)
    {
        if (!AccountDeletion.IsConfirmed(input.Confirmation))
        {
            return new DeleteAccountResult.NotConfirmed();
        }

        try
        {
            await cache.ClearAsync(input.UserId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Swallowed("limpar_cache_usuario", ErrorText.Describe(ex));
        }

        var counts = await data.DeleteAccountCompletelyAsync(input.UserId, cancellationToken);
        return counts is null
            ? new DeleteAccountResult.UserNotFound(input.UserId)
            : new DeleteAccountResult.Success(input.UserId, counts);
    }
}
