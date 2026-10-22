using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.Application.ApiKeys;

/// <summary>
/// Implementação de <see cref="IApiKeyProvider"/> (spec 008 §5; <c>gerenciador_chaves_api.py</c>).
/// Tarefas acessórias são "best effort": o erro é engolido e vai para <see cref="IApiKeyDiagnostics"/> sem texto do banco.
/// Desvios: 046 (limite só conta uso do sistema) e 047 (grava o log de acesso).
/// </summary>
public sealed class ApiKeyProvider(
    IApiKeyRepository keys,
    IPreferenceRepository preferences,
    IUsageRepository usage,
    IPlanRepository plans,
    IAccessLogRepository accessLog,
    ISystemApiKeys systemKeys,
    IEncryptionService encryption,
    IClock clock,
    IApiKeyDiagnostics diagnostics) : IApiKeyProvider
{
    private const string Operation = "obter_chave";
    private const string NoKeyType = "n/a";

    public async Task<ApiKeyResolution> ResolveAsync(int userId, string provider, CancellationToken cancellationToken)
    {
        if (!ApiProviders.IsValid(provider))
        {
            return new ApiKeyResolution.Failed(ApiKeyFailure.InvalidProvider, ApiKeyMessages.InvalidProvider(provider));
        }

        bool? useOwnKey = null;
        try
        {
            useOwnKey = await preferences.FindAsync(userId, provider, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Swallowed("buscar_preferencia", ErrorText.Describe(ex));
        }

        if (useOwnKey is null)
        {
            await LogAsync(userId, provider, NoKeyType, success: false, ApiKeyFailure.NoPreference, cancellationToken);
            return new ApiKeyResolution.Failed(ApiKeyFailure.NoPreference, ApiKeyMessages.NoPreference(provider));
        }

        return useOwnKey.Value
            ? await UseOwnKeyAsync(userId, provider, cancellationToken)
            : await UseSystemKeyAsync(userId, provider, cancellationToken);
    }

    private async Task<ApiKeyResolution> UseOwnKeyAsync(int userId, string provider, CancellationToken cancellationToken)
    {
        var stored = await keys.FindActiveEncryptedAsync(userId, provider, cancellationToken);
        if (stored is null)
        {
            await LogAsync(userId, provider, KeyTypes.Own, success: false, ApiKeyFailure.OwnKeyMissing, cancellationToken);
            return new ApiKeyResolution.Failed(ApiKeyFailure.OwnKeyMissing, ApiKeyMessages.OwnKeyMissing(provider));
        }

        string key;
        try
        {
            // Texto puro legado passa como veio; token com formato Fernet que não decifra falha (pergunta 7),
            // nunca devolve o texto cifrado como se fosse a chave.
            key = encryption.LooksEncrypted(stored) ? encryption.Decrypt(stored) : stored;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await LogAsync(userId, provider, KeyTypes.Own, success: false, "KeyUnreadable", cancellationToken);
            diagnostics.Swallowed("decifrar_chave", ErrorText.Describe(ex));
            throw;
        }

        await BestEffortAsync("atualizar_ultimo_uso", () => keys.TouchLastUseAsync(userId, provider, cancellationToken));
        await RegisterUsageAsync(userId, provider, KeyTypes.Own, cancellationToken);
        await LogAsync(userId, provider, KeyTypes.Own, success: true, error: null, cancellationToken);
        return new ApiKeyResolution.Resolved(key, KeyTypes.Own);
    }

    private async Task<ApiKeyResolution> UseSystemKeyAsync(int userId, string provider, CancellationToken cancellationToken)
    {
        var systemKey = systemKeys.KeyFor(provider);
        if (string.IsNullOrEmpty(systemKey))
        {
            await LogAsync(userId, provider, KeyTypes.System, success: false, ApiKeyFailure.SystemKeyUnavailable, cancellationToken);
            return new ApiKeyResolution.Failed(ApiKeyFailure.SystemKeyUnavailable, ApiKeyMessages.SystemKeyUnavailable(provider));
        }

        var limitReached = await CheckPlanLimitAsync(userId, provider, cancellationToken);
        if (limitReached is not null)
        {
            await LogAsync(userId, provider, KeyTypes.System, success: false, ApiKeyFailure.LimitReached, cancellationToken);
            return limitReached;
        }

        await RegisterUsageAsync(userId, provider, KeyTypes.System, cancellationToken);
        await LogAsync(userId, provider, KeyTypes.System, success: true, error: null, cancellationToken);
        return new ApiKeyResolution.Resolved(systemKey, KeyTypes.System);
    }

    /// <summary>Falha se o plano ativo tem limite e o uso do sistema no mês já o alcançou; erro de leitura passa (fail-open).</summary>
    private async Task<ApiKeyResolution.Failed?> CheckPlanLimitAsync(int userId, string provider, CancellationToken cancellationToken)
    {
        try
        {
            var plan = await plans.FindActivePlanAsync(userId, cancellationToken);
            if (plan is null)
            {
                return null;
            }

            var limit = plan.LimitFor(provider);
            if (limit is null)
            {
                return null;
            }

            var used = await usage.SumSystemMonthAsync(userId, provider, UsageMonth.Of(clock.Now), cancellationToken);
            return PlanLimit.Exceeded(limit, used)
                ? new ApiKeyResolution.Failed(ApiKeyFailure.LimitReached, ApiKeyMessages.LimitReached(provider, plan.PlanName, limit.Value, used))
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Swallowed("verificar_limite", ErrorText.Describe(ex));
            return null;
        }
    }

    private Task RegisterUsageAsync(int userId, string provider, string keyType, CancellationToken cancellationToken) =>
        BestEffortAsync("registrar_uso", () => usage.IncrementAsync(userId, provider, keyType, UsageMonth.Of(clock.Now), cancellationToken));

    private Task LogAsync(int userId, string provider, string keyType, bool success, ApiKeyFailure reason, CancellationToken cancellationToken) =>
        LogAsync(userId, provider, keyType, success, reason.ToString(), cancellationToken);

    private Task LogAsync(int userId, string provider, string keyType, bool success, string? error, CancellationToken cancellationToken) =>
        BestEffortAsync("registrar_log_acesso", () => accessLog.InsertAsync(userId, provider, keyType, Operation, success, error, cancellationToken));

    private async Task BestEffortAsync(string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Swallowed(operation, ErrorText.Describe(ex));
        }
    }
}
