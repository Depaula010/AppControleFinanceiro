using System.Net;
using System.Text;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.UnitTests.Infrastructure.External;

/// <summary>Seletor falso: chave por provedor para o usuário nulo; falha configurável para um usuário.</summary>
internal sealed class FakeKeySelector : IProviderKeySelector
{
    public string? SystemKey { get; set; } = "chave-do-sistema";

    public Dictionary<int, ProviderKeyResult> Users { get; } = [];

    public List<(int? UserId, string Provider)> Calls { get; } = [];

    public Task<ProviderKeyResult> SelectAsync(int? userId, string provider, CancellationToken cancellationToken)
    {
        Calls.Add((userId, provider));
        if (userId is { } id && Users.TryGetValue(id, out var result))
        {
            return Task.FromResult(result);
        }

        return Task.FromResult(string.IsNullOrEmpty(SystemKey) ? ProviderKeyResult.NoKey : ProviderKeyResult.WithKey(SystemKey));
    }

    public static ProviderKeyResult Failure(string message) => ProviderKeyResult.Failed(ApiKeyFailure.OwnKeyMissing, message);
}

internal static class Http
{
    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.Length > 1 ? p[1].Replace('+', ' ') : string.Empty));
}
