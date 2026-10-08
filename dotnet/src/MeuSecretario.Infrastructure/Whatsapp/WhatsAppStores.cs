using System.Text.Json.Nodes;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Whatsapp;

/// <summary><c>SELECT id, nome FROM Usuarios WHERE numero_whatsapp = :num</c> (<c>user_service.check_user_exists</c>).</summary>
public sealed class WhatsAppUserLookup(IDbConnectionFactory connections) : IWhatsAppUserLookup
{
    private sealed class Row
    {
        public int Id { get; set; }

        public string Nome { get; set; } = "";
    }

    public async Task<WhatsAppUser?> FindByNumberAsync(string number, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT id AS Id, nome AS Nome FROM usuarios WHERE numero_whatsapp = @number ORDER BY id LIMIT 1",
            new { number },
            cancellationToken: cancellationToken));
        return row is null ? null : new WhatsAppUser(row.Id, row.Nome);
    }
}

/// <summary><c>pending_tx:{numero}:{id}</c> e <c>last_pending:{numero}</c> (spec 012 §7.2); formato idêntico ao da 011.</summary>
public sealed class PendingTransactionState(ICache cache) : IPendingTransactionState
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(300);

    public Task<string?> GetLastPendingIdAsync(string number, CancellationToken cancellationToken) =>
        cache.GetJsonAsync<string>($"last_pending:{number}", cancellationToken);

    public async Task<JsonObject?> GetAsync(string number, string id, CancellationToken cancellationToken)
    {
        var element = await cache.GetJsonElementAsync($"pending_tx:{number}:{id}", cancellationToken);
        return element is { } value ? JsonNode.Parse(value.GetRawText()) as JsonObject : null;
    }

    public async Task SaveAsync(string number, string id, JsonObject data, CancellationToken cancellationToken) =>
        await cache.SetJsonAsync($"pending_tx:{number}:{id}", data, Ttl, cancellationToken);

    public async Task DeleteBothAsync(string number, string id, CancellationToken cancellationToken)
    {
        await cache.DeleteAsync($"pending_tx:{number}:{id}", cancellationToken);
        await cache.DeleteAsync($"last_pending:{number}", cancellationToken);
    }

    public async Task DeleteLastPendingAsync(string number, CancellationToken cancellationToken) =>
        await cache.DeleteAsync($"last_pending:{number}", cancellationToken);
}

/// <summary><c>pending_event:{numero}:{id}</c> (<c>event_confirmation_service.py</c>); id de 8 hexadecimais, TTL de 300 s.</summary>
public sealed class PendingEventStore(ICache cache, IPendingIdGenerator ids) : IPendingEventStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(300);

    public async Task<string?> CreateAsync(string number, PendingEvent data, CancellationToken cancellationToken)
    {
        var id = ids.NewId();
        return await cache.SetJsonAsync($"pending_event:{number}:{id}", data, Ttl, cancellationToken) ? id : null;
    }

    public async Task<LatestPendingEvent> GetLatestAsync(string number, CancellationToken cancellationToken)
    {
        var keys = await cache.KeysByPatternAsync($"pending_event:{number}:*", cancellationToken);
        if (keys.Count == 0)
        {
            return new LatestPendingEvent(null, null);
        }

        var latest = keys[^1];
        var id = latest[(latest.LastIndexOf(':') + 1)..];
        return new LatestPendingEvent(id, await cache.GetJsonAsync<PendingEvent>(latest, cancellationToken));
    }

    public Task<PendingEvent?> GetAsync(string number, string id, CancellationToken cancellationToken) =>
        cache.GetJsonAsync<PendingEvent>($"pending_event:{number}:{id}", cancellationToken);

    public async Task DeleteAsync(string number, string id, CancellationToken cancellationToken) =>
        await cache.DeleteAsync($"pending_event:{number}:{id}", cancellationToken);
}

/// <summary><c>nightly_checkin_active:{numero}</c>: o valor é o id da sessão (JSON de texto).</summary>
public sealed class CheckinSessionState(ICache cache) : ICheckinSession
{
    public Task<string?> GetActiveIdAsync(string number, CancellationToken cancellationToken) =>
        cache.GetJsonAsync<string>($"nightly_checkin_active:{number}", cancellationToken);

    public async Task ClearActiveAsync(string number, CancellationToken cancellationToken) =>
        await cache.DeleteAsync($"nightly_checkin_active:{number}", cancellationToken);
}
