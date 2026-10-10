using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>Usuário identificado pelo número (<c>SELECT id, nome FROM usuarios WHERE numero_whatsapp = ...</c>).</summary>
public sealed record WhatsAppUser(int Id, string Name);

/// <summary>
/// Arquivo enviado junto com a mensagem (<c>media_data</c>, <c>media_type</c>, <c>media_filename</c>). <paramref name="HasData"/> é a
/// verdade do Python sobre <c>media_data</c>; <paramref name="Data"/> só tem valor quando ele é texto (base64).
/// </summary>
public sealed record MediaPayload(bool HasData, string? Data, string? Type, string? FileName);

/// <summary>Corpo já validado do webhook (<c>texto</c> e <c>numero_remetente</c> são texto verdadeiro).</summary>
public sealed record WebhookInput(string Text, string RawNumber, MediaPayload? Media);

public interface IWhatsAppUserLookup
{
    /// <summary>Sem filtrar <c>ativo</c>, como o Flask (<c>check_user_exists</c>).</summary>
    Task<WhatsAppUser?> FindByNumberAsync(string number, CancellationToken cancellationToken);
}

/// <summary>
/// Pendência de transação no Redis (<c>pending_tx:{numero}:{id}</c> e <c>last_pending:{numero}</c>, TTL 300 s). O registro
/// é lido e regravado como <see cref="JsonObject"/> para preservar chaves que esta feature não conhece.
/// </summary>
public interface IPendingTransactionState
{
    /// <summary>O id guardado em <c>last_pending</c>; <c>null</c> quando ausente.</summary>
    Task<string?> GetLastPendingIdAsync(string number, CancellationToken cancellationToken);

    Task<JsonObject?> GetAsync(string number, string id, CancellationToken cancellationToken);

    /// <summary>Regrava <c>pending_tx</c> com TTL de 300 s.</summary>
    Task SaveAsync(string number, string id, JsonObject data, CancellationToken cancellationToken);

    /// <summary><c>delete_pending_transaction</c>: apaga <c>pending_tx</c> e depois <c>last_pending</c>.</summary>
    Task DeleteBothAsync(string number, string id, CancellationToken cancellationToken);

    /// <summary>Apaga só <c>last_pending</c> (<c>redis_service.delete(last_tx_key)</c>).</summary>
    Task DeleteLastPendingAsync(string number, CancellationToken cancellationToken);
}

/// <summary>Evento aguardando confirmação (<c>pending_event:{numero}:{id}</c>); as chaves são as do Flask.</summary>
public sealed record PendingEvent(
    [property: JsonPropertyName("usuario_id")] int UserId,
    [property: JsonPropertyName("titulo")] string Title,
    [property: JsonPropertyName("data_evento")] string EventDate,
    [property: JsonPropertyName("hora_inicio")] string? StartTime,
    [property: JsonPropertyName("hora_fim")] string? EndTime,
    [property: JsonPropertyName("descricao")] string? Description,
    [property: JsonPropertyName("localizacao")] string? Location);

/// <summary>Resultado de <c>get_latest_pending_event</c>: <c>Id</c> nulo = nenhuma chave; <c>Data</c> nulo = chave sem valor.</summary>
public sealed record LatestPendingEvent(string? Id, PendingEvent? Data);

public interface IPendingEventStore
{
    /// <summary>Grava com TTL de 300 s e devolve o id (8 hexadecimais); <c>null</c> se o Redis não gravou.</summary>
    Task<string?> CreateAsync(string number, PendingEvent data, CancellationToken cancellationToken);

    /// <summary>O último evento criado (<c>last_pending_event</c>, desvio 155); sem ponteiro, a última chave do <c>KEYS</c> como o Flask.</summary>
    Task<LatestPendingEvent> GetLatestAsync(string number, CancellationToken cancellationToken);

    Task<PendingEvent?> GetAsync(string number, string id, CancellationToken cancellationToken);

    Task DeleteAsync(string number, string id, CancellationToken cancellationToken);
}

/// <summary>Sessão de check-in noturno ativa (<c>nightly_checkin_active:{numero}</c>), criada pelo job da 013.</summary>
public interface ICheckinSession
{
    Task<string?> GetActiveIdAsync(string number, CancellationToken cancellationToken);

    Task ClearActiveAsync(string number, CancellationToken cancellationToken);
}

/// <summary>
/// <c>NightlyCheckinService.process_response</c> (013). Devolve o texto da resposta ou <c>null</c> se a mensagem não foi
/// tratada (o pipeline segue). O padrão registrado pela 012 devolve sempre <c>null</c>: sem o job da 013 nenhuma sessão existe.
/// </summary>
public interface INightlyCheckinResponder
{
    Task<string?> RespondAsync(string number, string message, string checkinId, CancellationToken cancellationToken);
}

public sealed class NullNightlyCheckinResponder : INightlyCheckinResponder
{
    public Task<string?> RespondAsync(string number, string message, string checkinId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}

/// <summary>Diagnóstico sem PII (a Application não conhece <c>ILogger</c>; a Api implementa): só o tipo da exceção.</summary>
public interface IWhatsAppDiagnostics
{
    void IntentFailed(string intent, string errorType);
}
