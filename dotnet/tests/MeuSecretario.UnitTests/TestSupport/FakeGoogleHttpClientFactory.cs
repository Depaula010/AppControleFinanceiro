using GoogleHttp = Google.Apis.Http;

namespace MeuSecretario.UnitTests.TestSupport;

/// <summary>
/// <see cref="GoogleHttp.IHttpClientFactory"/> do SDK do Google sobre um <see cref="FakeHttpHandler"/>: serviços do Calendar e do
/// Drive e o fluxo OAuth falam com o fake em vez da rede (<c>Initializer.HttpClientFactory</c>).
/// </summary>
public sealed class FakeGoogleHttpClientFactory(FakeHttpHandler handler) : GoogleHttp.IHttpClientFactory
{
    public FakeHttpHandler Handler { get; } = handler;

    public GoogleHttp.ConfigurableHttpClient CreateHttpClient(GoogleHttp.CreateHttpClientArgs args)
    {
        var client = new GoogleHttp.ConfigurableHttpClient(new GoogleHttp.ConfigurableMessageHandler(Handler));
        foreach (var initializer in args.Initializers)
        {
            initializer.Initialize(client);
        }

        return client;
    }
}
