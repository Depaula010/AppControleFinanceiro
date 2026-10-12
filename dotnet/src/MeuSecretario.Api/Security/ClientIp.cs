using MeuSecretario.Domain.Security;

namespace MeuSecretario.Api.Security;

/// <summary>IP do cliente já resolvido e se a chamada é interna (bypass do filtro e do rate limit).</summary>
public readonly record struct ClientIpInfo(string Ip, bool Internal);

/// <summary>
/// Resolução do IP (RN-009-01). Depois do <c>UseForwardedHeaders</c> o <c>RemoteIpAddress</c> já é o IP do cliente
/// (lido da direita para a esquerda sobre a rede privada confiável). <b>Interna</b> = origem confiável e o
/// <c>X-Forwarded-For</c> nunca existiu (nem o <c>X-Original-For</c>, que o middleware cria ao processá-lo): o bot, o
/// health do compose e o Ofelia chamam assim e não passam pelo filtro.
/// </summary>
public static class ClientIp
{
    public const string Unknown = "unknown";

    public static ClientIpInfo Resolve(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null)
        {
            // Sem endereço de origem (host em memória): não há de quem filtrar.
            return new ClientIpInfo(Unknown, true);
        }

        var headers = context.Request.Headers;
        var forwarded = headers.ContainsKey("X-Forwarded-For") || headers.ContainsKey("X-Original-For");
        return new ClientIpInfo(IpClassifier.Normalize(remote), !forwarded && IpClassifier.IsTrusted(remote));
    }
}
