using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;

namespace MeuSecretario.IntegrationTests.Accounts;

/// <summary>Cliente HTTP dos testes de contas: envia JSON como texto e devolve status e corpo como texto exato.</summary>
internal sealed class AccountsHttp(PostgresFixture postgres)
{
    public static string TokenFor(int userId) =>
        new JwtTokenService(AuthFixtures.JwtSecret, TimeProvider.System).Issue(userId);

    public static string Error(string message) => $$"""{"status":"error","message":"{{message}}"}""";

    public static string Success(string message) => $$"""{"status":"success","message":"{{message}}"}""";

    /// <param name="userId">Usuário do token; <c>null</c> envia sem token (ou com <paramref name="authorization"/>).</param>
    public async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpMethod method, string path, string? json = null, int? userId = null, string? authorization = null, string contentType = "application/json")
    {
        using var client = postgres.Api.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, contentType);
        }

        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
        else if (userId is { } id)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(id));
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public Task<(HttpStatusCode Status, string Body)> Get(int userId, string path = "/api/accounts") =>
        SendAsync(HttpMethod.Get, path, userId: userId);

    public Task<(HttpStatusCode Status, string Body)> Post(int userId, string? json, string path = "/api/accounts", string contentType = "application/json") =>
        SendAsync(HttpMethod.Post, path, json, userId, contentType: contentType);

    public Task<(HttpStatusCode Status, string Body)> Put(int userId, int id, string? json, string basePath = "/api/accounts") =>
        SendAsync(HttpMethod.Put, $"{basePath}/{id}", json, userId);

    public Task<(HttpStatusCode Status, string Body)> Delete(int userId, int id, string basePath = "/api/accounts") =>
        SendAsync(HttpMethod.Delete, $"{basePath}/{id}", userId: userId);
}
