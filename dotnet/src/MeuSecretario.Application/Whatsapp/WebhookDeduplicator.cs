using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// Idempotência do webhook (desvio 259): o bot pode reenviar a mesma mensagem (timeout, nova tentativa), e "gastei 50" não pode virar
/// duas pendências nem o check-in responder duas vezes. A deduplicação é pelo <c>message_id</c> que o bot manda; sem id (bot antigo)
/// nada é descartado, porque comparar só o texto engoliria mensagens repetidas de propósito ("sim", "ok").
/// </summary>
public interface IWebhookDeduplicator
{
    /// <summary><c>true</c> = esta mensagem já foi vista: ignorar. Sem id, ou com o Redis fora, processa (nunca perde mensagem).</summary>
    Task<bool> IsDuplicateAsync(string number, string? messageId, CancellationToken cancellationToken);

    /// <summary>O processamento falhou: libera o id para o reenvio do bot poder tentar de novo.</summary>
    Task ReleaseAsync(string number, string? messageId, CancellationToken cancellationToken);
}

public sealed class CacheWebhookDeduplicator(ICache cache) : IWebhookDeduplicator
{
    /// <summary>Quanto tempo um id fica marcado: mais que qualquer reenvio do bot, curto o bastante para não acumular chaves.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private const int MaxIdLength = 128;

    public async Task<bool> IsDuplicateAsync(string number, string? messageId, CancellationToken cancellationToken)
    {
        if (Key(number, messageId) is not { } key)
        {
            return false;
        }

        try
        {
            return !await cache.SetIfNotExistsAsync(key, "1", Window, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false; // Redis fora: processa (um possível duplicado é melhor que uma mensagem perdida).
        }
    }

    public async Task ReleaseAsync(string number, string? messageId, CancellationToken cancellationToken)
    {
        if (Key(number, messageId) is not { } key)
        {
            return;
        }

        try
        {
            await cache.DeleteAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // sem Redis não havia marca para liberar.
        }
    }

    /// <summary>Chave do id, ou <c>null</c> se não há id utilizável (vazio, comprido demais, com caractere de controle).</summary>
    public static string? Key(string number, string? messageId)
    {
        var id = messageId?.Trim();
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || id.Any(char.IsControl))
        {
            return null;
        }

        return $"webhook_msg:{number}:{id}";
    }
}
