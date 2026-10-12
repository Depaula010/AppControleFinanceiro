namespace MeuSecretario.Application.Abstractions;

/// <summary>Gemini atrás de uma porta (spec 010 §6). A chave é escolhida por chamada (<see cref="IProviderKeySelector"/>).</summary>
public interface ILlmClient
{
    Task<string> GenerateTextAsync(LlmRequest request, CancellationToken cancellationToken);
}

/// <param name="Prompt">Enviado byte a byte como o chamador montou.</param>
/// <param name="UserId">Nulo = chave do sistema; preenchido = chave resolvida pelo 008.</param>
/// <param name="Cache">Nulo = chamada direta, sem cache.</param>
public sealed record LlmRequest(string Prompt, int? UserId = null, LlmCachePolicy? Cache = null);

public sealed record LlmCachePolicy(string Prefix, TimeSpan Ttl, bool UserSpecific);

/// <summary>Falha genérica do Gemini (código HTTP, sem corpo nem chave).</summary>
public class LlmException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>HTTP 429 / <c>RESOURCE_EXHAUSTED</c> / quota. A mensagem é literal (o webhook procura "429", "quota").</summary>
public sealed class LlmQuotaExceededException(string message) : LlmException(message);

/// <summary>HTTP 403 / <c>PERMISSION_DENIED</c>. Mensagem literal.</summary>
public sealed class LlmPermissionDeniedException(string message) : LlmException(message);

/// <summary>Resposta bloqueada ou sem texto.</summary>
public sealed class LlmBlockedException(string message) : LlmException(message);

/// <summary>Falha do 008 ao resolver a chave; a mensagem é o texto literal para o usuário.</summary>
public sealed class LlmKeyUnavailableException(string message) : LlmException(message);
