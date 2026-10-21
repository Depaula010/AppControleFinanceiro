using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// Lembretes variáveis que o usuário confirmou e ainda esperam o valor real. O primeiro da lista é o que está sendo perguntado;
/// <paramref name="Total"/> é o tamanho original, para o contador "(2/3)".
/// </summary>
public sealed record CheckinPendingValues(List<CheckinSessionItem> Items, int Total);

/// <summary>Em que ponto está o diálogo do check-in noturno de um número (desvio 254: antes eram três chaves soltas no Redis).</summary>
public abstract record CheckinDialogState
{
    /// <summary>Não há sessão (passou a 1 h ou já foi encerrada).</summary>
    public sealed record Expired : CheckinDialogState;

    /// <summary>O check-in foi enviado e espera "ok", números ou "depois".</summary>
    public sealed record AwaitingConfirmation(CheckinSession Session) : CheckinDialogState;

    /// <summary>As contas foram confirmadas e falta o valor de um ou mais lembretes variáveis.</summary>
    public sealed record AwaitingValues(CheckinPendingValues Pending) : CheckinDialogState;
}

/// <summary>
/// Único dono das chaves Redis do diálogo do check-in: a sessão do job (<c>nightly_checkin:{numero}:{id}</c>, formato do Flask), a flag
/// ativa (<c>nightly_checkin_active:{numero}</c>) e a fila de valores (<c>nightly_checkin_values:{numero}:{id}</c>). Todas valem 1 h; a
/// flag e a fila são renovadas a cada passo. Quem responde ao check-in só enxerga <see cref="CheckinDialogState"/>.
/// </summary>
public sealed class CheckinDialogStore(ICache cache, ICheckinSession session)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public async Task<CheckinDialogState> LoadAsync(string number, string checkinId, CancellationToken cancellationToken)
    {
        var pending = await cache.GetJsonAsync<CheckinPendingValues>(CacheKeys.NightlyCheckinValues(number, checkinId), cancellationToken);
        if (pending is { Items.Count: > 0 })
        {
            return new CheckinDialogState.AwaitingValues(pending);
        }

        var data = await cache.GetJsonAsync<CheckinSession>(CacheKeys.NightlyCheckin(number, checkinId), cancellationToken);
        return data is null ? new CheckinDialogState.Expired() : new CheckinDialogState.AwaitingConfirmation(data);
    }

    /// <summary>A sessão sumiu: só a flag ativa fica para trás, e ela é limpa.</summary>
    public Task ExpireAsync(string number, CancellationToken cancellationToken) => session.ClearActiveAsync(number, cancellationToken);

    /// <summary>As contas foram confirmadas: a sessão some e a fila de valores passa a mandar, com a flag renovada.</summary>
    public async Task StartValuesAsync(string number, string checkinId, CheckinPendingValues pending, CancellationToken cancellationToken)
    {
        await cache.DeleteAsync(CacheKeys.NightlyCheckin(number, checkinId), cancellationToken);
        await SaveValuesAsync(number, checkinId, pending, cancellationToken);
    }

    public async Task SaveValuesAsync(string number, string checkinId, CheckinPendingValues pending, CancellationToken cancellationToken)
    {
        await cache.SetJsonAsync(CacheKeys.NightlyCheckinValues(number, checkinId), pending, Ttl, cancellationToken);
        await cache.SetJsonAsync(CacheKeys.NightlyCheckinActive(number), checkinId, Ttl, cancellationToken);
    }

    /// <summary>Encerra o diálogo: sessão, fila de valores e flag ativa.</summary>
    public async Task EndAsync(string number, string checkinId, CancellationToken cancellationToken)
    {
        await cache.DeleteAsync(CacheKeys.NightlyCheckin(number, checkinId), cancellationToken);
        await cache.DeleteAsync(CacheKeys.NightlyCheckinValues(number, checkinId), cancellationToken);
        await session.ClearActiveAsync(number, cancellationToken);
    }
}
