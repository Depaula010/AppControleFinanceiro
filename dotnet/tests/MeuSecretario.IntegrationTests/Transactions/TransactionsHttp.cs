using System.Net;
using MeuSecretario.IntegrationTests.Accounts;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary>Cliente HTTP dos testes de transações (reutiliza o da 002): corpo comparado como texto exato.</summary>
internal sealed class TransactionsHttp(PostgresFixture postgres)
{
    private const string Path = "/api/transactions";

    private readonly AccountsHttp _http = new(postgres);

    public static string Error(string message) => AccountsHttp.Error(message);

    public Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpMethod method, string path, string? json = null, int? userId = null, string? authorization = null) =>
        _http.SendAsync(method, path, json, userId, authorization);

    public Task<(HttpStatusCode Status, string Body)> Get(int userId, string query = "") =>
        _http.SendAsync(HttpMethod.Get, Path + query, userId: userId);

    public Task<(HttpStatusCode Status, string Body)> Post(int userId, string? json, string contentType = "application/json") =>
        _http.Post(userId, json, Path, contentType);

    public Task<(HttpStatusCode Status, string Body)> Put(int userId, int id, string? json) => _http.Put(userId, id, json, Path);

    public Task<(HttpStatusCode Status, string Body)> Delete(int userId, int id) => _http.Delete(userId, id, Path);
}
