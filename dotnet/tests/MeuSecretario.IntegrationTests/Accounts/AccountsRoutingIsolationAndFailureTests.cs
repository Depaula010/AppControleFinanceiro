using System.Net;
using MeuSecretario.Api.Accounts;
using MeuSecretario.Application.Accounts;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Accounts;

/// <summary>Roteamento do Flask reproduzido: 404 HTML antes da autenticação, 405, barra final (spec 002 §2).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class AccountsRoutingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string FlaskNotFoundHtml =
        "<!doctype html>\n<html lang=en>\n<title>404 Not Found</title>\n<h1>Not Found</h1>\n" +
        "<p>The requested URL was not found on the server. If you entered the URL manually please check your spelling and try again.</p>\n";

    private readonly AccountsHttp _http = new(postgres);
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await new AccountsTestData(postgres.DataSource).InsertUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> NonMatchingPaths
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var method in new[] { "GET", "POST", "PUT", "DELETE" })
            {
                foreach (var path in new[] { "/api/accounts/", "/api/accounts/abc", "/api/accounts/-1", "/api/contas/1.5", "/api/contas/", "/api/contas/1a" })
                {
                    data.Add(method, path);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NonMatchingPaths))]
    [Trait("AC", "002-83")]
    public async Task TrailingSlashOrNonNumericId_Returns404Html_EvenWithoutToken(string method, string path)
    {
        var (status, body) = await _http.SendAsync(new HttpMethod(method), path, json: method is "POST" or "PUT" ? "{}" : null);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(FlaskNotFoundHtml);
    }

    [Theory]
    [Trait("AC", "002-83")]
    [InlineData("/api/accounts/abc")]
    [InlineData("/api/contas/-1")]
    public async Task NonNumericId_Returns404Html_WithAValidTokenToo(string path)
    {
        var (status, body) = await _http.SendAsync(HttpMethod.Delete, path, userId: _user);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(FlaskNotFoundHtml);
    }

    [Theory]
    [Trait("AC", "002-84")]
    [InlineData("GET", "/api/accounts/1")]
    [InlineData("POST", "/api/accounts/1")]
    [InlineData("DELETE", "/api/accounts")]
    [InlineData("PUT", "/api/accounts")]
    [InlineData("PATCH", "/api/accounts")]
    [InlineData("PATCH", "/api/accounts/1")]
    [InlineData("GET", "/api/contas/1")]
    [InlineData("DELETE", "/api/contas")]
    public async Task MethodNotAllowed_Returns405(string method, string path)
    {
        var (status, body) = await _http.SendAsync(new HttpMethod(method), path, userId: _user);

        status.ShouldBe(HttpStatusCode.MethodNotAllowed, $"{method} {path} => {(int)status} {body}");
    }
}

/// <summary>Usuário B nunca vê, altera nem remove conta do usuário A (RN-002-02): as 8 operações.</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class AccountsTenantIsolationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly AccountsHttp _http = new(postgres);
    private readonly AccountsTestData _data = new(postgres.DataSource);
    private int _a;
    private int _b;
    private int _accountOfA;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _a = await _data.InsertUserAsync("5511900000001", "Ana");
        _b = await _data.InsertUserAsync("5511900000002", "Beto");
        _accountOfA = await _data.InsertAccountAsync(_a, "Compartilhado", saldoInicial: 100m);
        await _data.InsertAccountAsync(_b, "SoDoB", saldoInicial: 5m);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task AssertAccountOfAIsIntact()
    {
        var row = (await _data.QueryAsync<(string Nome, bool Ativa, decimal Saldo)>(
            "SELECT nome_conta, ativa, saldo_inicial FROM contas WHERE id = @_accountOfA", new { _accountOfA })).Single();
        row.ShouldBe(("Compartilhado", true, 100m));
    }

    [Theory]
    [Trait("AC", "002-04")]
    [Trait("AC", "002-81")]
    [InlineData("/api/accounts")]
    [InlineData("/api/contas")]
    public async Task Get_EachUserSeesOnlyTheirOwn(string path)
    {
        var (_, forB) = await _http.Get(_b, path);
        var (_, forA) = await _http.Get(_a, path);

        forB.ShouldContain("SoDoB");
        forB.ShouldNotContain("Compartilhado");
        forA.ShouldContain("Compartilhado");
        forA.ShouldNotContain("SoDoB");
    }

    [Theory]
    [Trait("AC", "002-53")]
    [InlineData("/api/accounts")]
    [InlineData("/api/contas")]
    public async Task Put_OnAnotherUsersAccount_Returns404_AndItIsUntouched(string path)
    {
        var (status, body) = await _http.Put(_b, _accountOfA, """{"nome_conta":"Roubada","saldo_inicial":1}""", path);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AccountsHttp.Error("Conta não encontrada"));
        await AssertAccountOfAIsIntact();
    }

    [Theory]
    [Trait("AC", "002-72")]
    [InlineData("/api/accounts")]
    [InlineData("/api/contas")]
    public async Task Delete_OnAnotherUsersAccount_Returns404_AndItStaysActive(string path)
    {
        var (status, body) = await _http.Delete(_b, _accountOfA, path);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AccountsHttp.Error("Conta não encontrada"));
        await AssertAccountOfAIsIntact();
    }

    [Theory]
    [Trait("AC", "002-22")]
    [InlineData("/api/accounts")]
    [InlineData("/api/contas")]
    public async Task Post_WithTheSameNameAsAnotherUsersAccount_CreatesAnIndependentOne(string path)
    {
        var (status, _) = await _http.Post(_b, """{"nome_conta":"Compartilhado","tipo_conta":"Dinheiro"}""", path);

        status.ShouldBe(HttpStatusCode.Created);
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM contas WHERE nome_conta = 'Compartilhado'")).ShouldBe(2);
        await AssertAccountOfAIsIntact();
    }

    [Fact]
    [Trait("AC", "002-22")]
    public async Task Post_WithTheNameOfAnotherUsersInactiveAccount_DoesNotReactivateIt()
    {
        var inactiveOfA = await _data.InsertAccountAsync(_a, "Velha", ativa: false);

        var (status, _) = await _http.Post(_b, """{"nome_conta":"Velha","tipo_conta":"Dinheiro"}""");

        status.ShouldBe(HttpStatusCode.Created);
        (await _data.ScalarAsync<bool>("SELECT ativa FROM contas WHERE id = @inactiveOfA", new { inactiveOfA })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "002-81")]
    public async Task OwnerInTheBodyIsIgnored_TheAccountBelongsToTheTokenUser()
    {
        var (status, _) = await _http.Post(_b, $$"""{"nome_conta":"Dele","tipo_conta":"Dinheiro","usuario_id":{{_a}}}""");

        status.ShouldBe(HttpStatusCode.Created);
        (await _data.ScalarAsync<int>("SELECT usuario_id FROM contas WHERE nome_conta = 'Dele'")).ShouldBe(_b);
    }
}

/// <summary>500 com mensagem fixa e log só com o tipo do erro (desvio 008, AC-002-85).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class AccountsFailureTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Secret = "segredo-interno 5511999999999 Nubank";

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string, string, string?, string> Operations => new()
    {
        { "GET", "/api/accounts", "Erro ao carregar contas", null, "Erro ao carregar contas" },
        { "POST", "/api/accounts", "Erro ao criar conta", """{"nome_conta":"Nubank","tipo_conta":"Dinheiro"}""", "Erro ao criar conta" },
        { "PUT", "/api/accounts/7", "Erro ao atualizar conta", """{"nome_conta":"Nubank"}""", "Erro ao atualizar conta" },
        { "DELETE", "/api/accounts/7", "Erro ao remover conta", null, "Erro ao remover conta" },
        { "GET", "/api/contas", "Erro ao carregar contas", null, "Erro ao carregar contas" },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "002-85")]
    public async Task RepositoryFailure_Returns500WithTheFixedMessage_AndLogsOnlyTheErrorType(
        string method, string path, string message, string? json, string logPrefix)
    {
        var log = new CapturingLogger();
        await using var factory = postgres.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddScoped<IAccountRepository>(_ => new ThrowingRepository());
            services.AddSingleton<ILogger<AccountsModule>>(log);
        }));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + AccountsHttp.TokenFor(42));
        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AccountsHttp.Error(message));
        body.ShouldNotContain("segredo");
        log.Messages.Count.ShouldBe(1);
        log.Messages[0].ShouldStartWith(logPrefix);
        log.Messages[0].ShouldContain("PostgresException (SqlState 23505)");
        log.Messages[0].ShouldContain("usuário 42");
        log.Messages[0].ShouldNotContain("segredo");
        log.Messages[0].ShouldNotContain("5511999999999");
        log.Messages[0].ShouldNotContain("Nubank");
    }

    private sealed class ThrowingRepository : IAccountRepository
    {
        private static PostgresException Failure() => new(Secret, "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        public Task<IReadOnlyList<AccountListItem>> ListActiveAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        public Task<AccountDetails?> GetActiveAsync(int userId, int accountId, CancellationToken cancellationToken) => throw Failure();

        public Task<bool> NameTakenAsync(int userId, string name, int exceptAccountId, CancellationToken cancellationToken) => throw Failure();

        public Task<CreateAccountResult> CreateAsync(int userId, NewAccountData data, CancellationToken cancellationToken) => throw Failure();

        public Task<UpdateAccountResult> UpdateAsync(int userId, int accountId, AccountChanges changes, CancellationToken cancellationToken) => throw Failure();

        public Task<DeleteAccountResult> SoftDeleteAsync(int userId, int accountId, CancellationToken cancellationToken) => throw Failure();
    }

    private sealed class CapturingLogger : ILogger<AccountsModule>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
