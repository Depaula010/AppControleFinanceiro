using System.Net;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Api.Transactions;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static MeuSecretario.IntegrationTests.Transactions.TransactionsTestData;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary><c>DELETE /api/transactions/{id}</c> (spec 004 §3.4), corpo como texto exato.</summary>
[Trait("Category", "Docker")]
public class TransactionsDeleteEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Deleted = """{"status":"success","message":"Transação deletada com sucesso"}""";

    private readonly TransactionsHttp _http = new(postgres);
    private readonly TransactionsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync("5511900000001", "Ana");
        _account = await _data.InsertAccountAsync(_user, "Corrente");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<int> Count() => _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes");

    [Fact]
    [Trait("AC", "004-70")]
    public async Task SimpleTransaction_IsDeleted_WithoutDataInTheBody()
    {
        var id = await _data.InsertTransactionAsync(_user, _account);

        var (status, body) = await _http.Delete(_user, id);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(Deleted);
        (await Count()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-71")]
    public async Task UnknownId_Returns404()
    {
        var (status, body) = await _http.Delete(_user, 99999);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(TransactionsHttp.Error("Transação não encontrada"));
    }

    [Fact]
    [Trait("AC", "004-72")]
    public async Task SecondDelete_Returns404()
    {
        var id = await _data.InsertTransactionAsync(_user, _account);

        (await _http.Delete(_user, id)).Status.ShouldBe(HttpStatusCode.OK);
        (await _http.Delete(_user, id)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [Trait("AC", "004-73")]
    [InlineData("Transferência")]
    [InlineData("Pagamento Fatura")]
    public async Task EitherLegOfAPair_DeletesBothLegs(string type)
    {
        var second = await _data.InsertAccountAsync(_user, "Poupança");
        var first = await _data.InsertTransactionAsync(_user, _account, type, -100m);
        var other = await _data.InsertTransactionAsync(_user, second, type, 100m);
        await _data.LinkPairAsync(first, other);

        (await _http.Delete(_user, other)).Body.ShouldBe(Deleted);

        (await Count()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-74")]
    public async Task APairOfAnotherUser_IsNotDeleted()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        var mine = await _data.InsertTransactionAsync(_user, _account, "Transferência", -100m);
        var foreign = await _data.InsertTransactionAsync(other, otherAccount, "Transferência", 100m);
        await _data.LinkPairAsync(mine, foreign);

        (await _http.Delete(_user, mine)).Status.ShouldBe(HttpStatusCode.OK);

        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes WHERE id = @foreign", new { foreign })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "004-76")]
    public async Task AnotherUsersTransaction_Returns404_AndStays()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var id = await _data.InsertTransactionAsync(_user, _account);

        var (status, body) = await _http.Delete(other, id);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(TransactionsHttp.Error("Transação não encontrada"));
        (await Count()).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "004-77")]
    public async Task ExpenseOnACard_KeepsTheInvoiceAndItsStatus()
    {
        var card = await _data.InsertCardAsync(_user);
        var invoice = await _data.InsertInvoiceAsync(card, "2026-04-15", "2026-04-10", "Fechada");
        var id = await _data.InsertTransactionAsync(_user, card, faturaId: invoice);

        (await _http.Delete(_user, id)).Status.ShouldBe(HttpStatusCode.OK);

        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @invoice", new { invoice })).ShouldBe("Fechada");
    }

    [Fact]
    [Trait("AC", "004-70")]
    public async Task DeletingTheInvoicePayment_ReopensTheInvoice()
    {
        var card = await _data.InsertCardAsync(_user);
        var invoice = await _data.InsertInvoiceAsync(card, "2020-01-15", "2020-01-10", "Paga");
        var outgoing = await _data.InsertTransactionAsync(_user, _account, "Pagamento Fatura", -500m, faturaId: invoice);
        var incoming = await _data.InsertTransactionAsync(_user, card, "Pagamento Fatura", 500m, faturaId: invoice);
        await _data.LinkPairAsync(outgoing, incoming);

        (await _http.Delete(_user, outgoing)).Status.ShouldBe(HttpStatusCode.OK);

        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @invoice", new { invoice })).ShouldBe("Fechada");
    }

    [Fact]
    [Trait("AC", "004-84")]
    public async Task HugeId_Returns404Json()
    {
        var (status, body) = await _http.SendAsync(HttpMethod.Delete, "/api/transactions/99999999999999999999", userId: _user);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(TransactionsHttp.Error("Transação não encontrada"));
    }
}

/// <summary>Roteamento do Flask reproduzido: 404 HTML antes da autenticação, 405, barra final (spec 004 §2).</summary>
[Trait("Category", "Docker")]
public class TransactionsRoutingTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string FlaskNotFoundHtml =
        "<!doctype html>\n<html lang=en>\n<title>404 Not Found</title>\n<h1>Not Found</h1>\n" +
        "<p>The requested URL was not found on the server. If you entered the URL manually please check your spelling and try again.</p>\n";

    private readonly TransactionsHttp _http = new(postgres);
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await new TransactionsTestData(postgres.DataSource).InsertUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> NonMatchingPaths
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var method in new[] { "GET", "POST", "PUT", "DELETE" })
            {
                foreach (var path in new[] { "/api/transactions/", "/api/transactions/abc", "/api/transactions/-1", "/api/transactions/1.5", "/api/transactions/1a" })
                {
                    data.Add(method, path);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NonMatchingPaths))]
    [Trait("AC", "004-81")]
    public async Task TrailingSlashOrNonNumericId_Returns404Html_EvenWithoutToken(string method, string path)
    {
        var (status, body) = await _http.SendAsync(new HttpMethod(method), path, json: method is "POST" or "PUT" ? "{}" : null);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(FlaskNotFoundHtml);
    }

    [Theory]
    [Trait("AC", "004-82")]
    [InlineData("GET", "/api/transactions/1")]
    [InlineData("POST", "/api/transactions/1")]
    [InlineData("PATCH", "/api/transactions/1")]
    [InlineData("DELETE", "/api/transactions")]
    [InlineData("PUT", "/api/transactions")]
    [InlineData("PATCH", "/api/transactions")]
    [InlineData("PUT", "/api/transactions/recent")]
    [InlineData("DELETE", "/api/transactions/recent")]
    [InlineData("POST", "/api/transactions/recent")]
    public async Task MethodNotAllowed_Returns405(string method, string path)
    {
        var (status, body) = await _http.SendAsync(new HttpMethod(method), path, userId: _user);

        status.ShouldBe(HttpStatusCode.MethodNotAllowed, $"{method} {path} => {(int)status} {body}");
    }
}

/// <summary>Usuário B nunca vê, altera nem remove transação do usuário A (RN-004-02): as 4 rotas.</summary>
[Trait("Category", "Docker")]
public class TransactionsTenantIsolationTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly TransactionsHttp _http = new(postgres);
    private readonly TransactionsTestData _data = new(postgres.DataSource);
    private int _a;
    private int _b;
    private int _accountA;
    private int _accountB;
    private int _transactionA;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _a = await _data.InsertUserAsync("5511900000001", "Ana");
        _b = await _data.InsertUserAsync("5511900000002", "Beto");
        _accountA = await _data.InsertAccountAsync(_a, "ContaDaAna");
        _accountB = await _data.InsertAccountAsync(_b, "ContaDoBeto");
        _transactionA = await _data.InsertTransactionAsync(_a, _accountA, descricao: "da-ana", valor: -10m);
        await _data.InsertTransactionAsync(_b, _accountB, descricao: "do-beto", valor: -20m);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "004-85")]
    public async Task Get_EachUserSeesOnlyTheirOwn()
    {
        var forA = (await _http.Get(_a)).Body;
        var forB = (await _http.Get(_b)).Body;

        forA.ShouldContain("da-ana");
        forA.ShouldNotContain("do-beto");
        forB.ShouldContain("do-beto");
        forB.ShouldNotContain("da-ana");
    }

    [Fact]
    [Trait("AC", "004-85")]
    public async Task Post_WithAnotherUsersAccount_Returns404()
    {
        var json = $$"""{"descricao":"x","valor":1,"tipo":"Despesa","data":"2026-03-10","subcategoria_id":{{ExpenseSubcategory}},"conta_id":{{_accountA}}}""";

        var (status, body) = await _http.Post(_b, json);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(TransactionsHttp.Error("Conta não encontrada ou não pertence ao usuário"));
    }

    [Fact]
    [Trait("AC", "004-85")]
    public async Task Post_WithAnotherUsersSubcategory_Returns404()
    {
        var foreign = await _data.ScalarAsync<int>(
            "INSERT INTO subcategoria (macro_id, nome_sub, usuario_id) VALUES (17, 'Da Ana', @_a) RETURNING id", new { _a });
        var json = $$"""{"descricao":"x","valor":1,"tipo":"Despesa","data":"2026-03-10","subcategoria_id":{{foreign}},"conta_id":{{_accountB}}}""";

        var (status, body) = await _http.Post(_b, json);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(TransactionsHttp.Error("Subcategoria não encontrada"));
    }

    [Fact]
    [Trait("AC", "004-85")]
    public async Task Put_AndDelete_OnAnotherUsersTransaction_Return404_AndNothingChanges()
    {
        var put = await _http.Put(_b, _transactionA, """{"descricao":"roubada","valor":999}""");
        var delete = await _http.Delete(_b, _transactionA);

        put.Status.ShouldBe(HttpStatusCode.NotFound);
        delete.Status.ShouldBe(HttpStatusCode.NotFound);
        put.Body.ShouldBe(TransactionsHttp.Error("Transação não encontrada"));
        var row = (await _data.QueryAsync<(string Desc, decimal Valor)>(
            "SELECT descricao, valor FROM transacoes WHERE id = @_transactionA", new { _transactionA })).Single();
        row.ShouldBe(("da-ana", -10m));
    }

    [Fact]
    [Trait("AC", "004-85")]
    public async Task ServicesNeverTouchAnotherUsersAccountsOrInvoices()
    {
        var cardA = await _data.InsertCardAsync(_a, "CartaoDaAna");
        var invoiceA = await _data.InsertInvoiceAsync(cardA, "2026-01-15", "2026-01-10", "Fechada");
        await using var scope = postgres.Api.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<ITransactionPosting>();

        var simple = await posting.PostSimpleAsync(
            new PostSimpleTransaction(_b, _accountA, ExpenseSubcategory, MeuSecretario.Domain.Transactions.TransactionKind.Expense, 1m, "x", Day("2026-03-10")),
            CancellationToken.None);
        var transfer = await posting.PostTransferAsync(new PostTransfer(_b, _accountB, _accountA, 1m, Day("2026-03-10")), CancellationToken.None);
        var payment = await posting.PostInvoicePaymentAsync(new PostInvoicePayment(_b, _accountB, cardA, 1m, Day("2026-03-10")), CancellationToken.None);

        simple.ShouldBeOfType<PostingResult<PostedTransaction>.Failed>().Error.ShouldBe(PostingError.AccountNotFound);
        transfer.ShouldBeOfType<PostingResult<PostedTransfer>.Failed>().Error.ShouldBe(PostingError.DestinationAccountNotFound);
        payment.ShouldBeOfType<PostingResult<PostedInvoicePayment>.Failed>().Error.ShouldBe(PostingError.DestinationAccountNotFound);
        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @invoiceA", new { invoiceA })).ShouldBe("Fechada");

        await scope.ServiceProvider.GetRequiredService<IInvoiceService>().EnsureCurrentAsync(_b, cardA, CancellationToken.None);
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM faturas WHERE conta_id = @cardA", new { cardA })).ShouldBe(1);
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(2);
    }
}

/// <summary>500 com mensagem fixa e log só com o tipo do erro (desvio 008, AC-004-83).</summary>
[Trait("Category", "Docker")]
public class TransactionsFailureTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Secret = "segredo-interno 5511999999999 Supermercado";

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string, string?, string, string> Operations => new()
    {
        { "GET", "/api/transactions", null, "Erro ao carregar transações", "Erro ao carregar transações" },
        {
            "POST", "/api/transactions",
            """{"descricao":"Supermercado","valor":1,"tipo":"Despesa","data":"2026-03-10","subcategoria_id":59,"conta_id":3}""",
            "Erro ao criar transação", "Erro ao criar transação"
        },
        { "PUT", "/api/transactions/7", """{"descricao":"Supermercado"}""", "Erro ao atualizar transação", "Erro ao atualizar transação" },
        { "DELETE", "/api/transactions/7", null, "Erro ao deletar transação", "Erro ao deletar transação" },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "004-83")]
    public async Task Failure_Returns500WithTheFixedMessage_AndLogsOnlyTheErrorType(
        string method, string path, string? json, string message, string logPrefix)
    {
        var log = new CapturingLogger();
        await using var factory = postgres.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddScoped<ITransactionRepository>(_ => new ThrowingRepository());
            services.AddScoped<ITransactionPosting>(_ => new ThrowingPosting());
            services.AddSingleton<ILogger<TransactionsModule>>(log);
        }));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + MeuSecretario.IntegrationTests.Accounts.AccountsHttp.TokenFor(42));
        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(TransactionsHttp.Error(message));
        body.ShouldNotContain("segredo");
        log.Messages.Count.ShouldBe(1);
        log.Messages[0].ShouldStartWith(logPrefix);
        log.Messages[0].ShouldContain("PostgresException (SqlState 23505)");
        log.Messages[0].ShouldContain("usuário 42");
        log.Messages[0].ShouldNotContain("segredo");
        log.Messages[0].ShouldNotContain("5511999999999");
        log.Messages[0].ShouldNotContain("Supermercado");
    }

    private static PostgresException Failure() => new(Secret, "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

    private sealed class ThrowingRepository : ITransactionRepository
    {
        public Task<TransactionPage> ListAsync(int userId, MeuSecretario.Domain.Transactions.ListFilter filter, CancellationToken cancellationToken) => throw Failure();

        public Task<CurrentTransaction?> GetAsync(int userId, int id, CancellationToken cancellationToken) => throw Failure();

        public Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken cancellationToken) => throw Failure();

        public Task<bool> ActiveAccountOwnedAsync(int userId, int accountId, CancellationToken cancellationToken) => throw Failure();

        public Task<UpdatedTransaction?> UpdateAsync(int userId, int id, MeuSecretario.Domain.Transactions.TransactionChanges changes, CancellationToken cancellationToken) => throw Failure();

        public Task<bool> DeleteWithPairAsync(int userId, int id, DateOnly today, CancellationToken cancellationToken) => throw Failure();
    }

    private sealed class ThrowingPosting : ITransactionPosting
    {
        public Task<PostingResult<PostedTransaction>> PostSimpleAsync(PostSimpleTransaction command, CancellationToken cancellationToken) => throw Failure();

        public Task<PostingResult<PostedTransfer>> PostTransferAsync(PostTransfer command, CancellationToken cancellationToken) => throw Failure();

        public Task<PostingResult<PostedInvoicePayment>> PostInvoicePaymentAsync(PostInvoicePayment command, CancellationToken cancellationToken) => throw Failure();

        public Task<PostingResult<PostedInstallmentPurchase>> PostInstallmentPurchaseAsync(PostInstallmentPurchase command, CancellationToken cancellationToken) => throw Failure();
    }

    private sealed class CapturingLogger : ILogger<TransactionsModule>
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
