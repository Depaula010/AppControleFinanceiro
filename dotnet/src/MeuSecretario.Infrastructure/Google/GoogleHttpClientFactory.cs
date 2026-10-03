using GoogleHttp = Google.Apis.Http;

namespace MeuSecretario.Infrastructure.Google;

/// <summary>
/// <see cref="GoogleHttp.IHttpClientFactory"/> do SDK do Google com timeout de 60 s (RN-010-54), compartilhada por d1 e d2 e
/// substituível em teste. Cada chamada cria um cliente novo, sem estado entre usuários.
/// </summary>
public sealed class GoogleHttpClientFactory : GoogleHttp.IHttpClientFactory
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly GoogleHttp.HttpClientFactory _inner = new();

    public GoogleHttp.ConfigurableHttpClient CreateHttpClient(GoogleHttp.CreateHttpClientArgs args)
    {
        var client = _inner.CreateHttpClient(args);
        client.Timeout = Timeout;
        return client;
    }
}
