using System.Net;
using MeuSecretario.Api.Bills;
using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Bills;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Bills;

/// <summary><c>DELETE /api/bills/{id}</c> (spec 005 §3.4).</summary>
[Trait("Category", "Docker")]
public class BillsDeleteEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly BillsHttp _http = new(postgres);
    private readonly BillsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync();
        _account = await _data.InsertAccountAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [Trait("AC", "005-70")]
    [Trait("AC", "005-81")]
    [InlineData("/api/bills")]
    [InlineData("/api/contas-mensais")]
    public async Task Delete_DeactivatesTheBill_KeepsTheRow_AndItLeavesTheListAndTheSummary(string path)
    {
        var id = await _data.InsertScheduleAsync(_user, _account, valor: 100m);

        var (status, body) = await _http.Delete(_user, id, path);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","message":"Conta mensal removida com sucesso"}""");
        (await _data.ScalarAsync<bool>("SELECT ativo FROM agendamentos WHERE id = @id", new { id })).ShouldBeFalse();
        (await _http.Get(_user, path)).Body.ShouldBe("""{"status":"success","data":[]}""");
        (await _http.Get(_user, "/api/bills/summary")).Body.ShouldContain("\"total_despesas_mensais\":0,");
    }

    [Fact]
    [Trait("AC", "005-71")]
    public async Task Delete_MissingTwiceOrForeign_Is404_AndTheOthersStayActive()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        var foreign = await _data.InsertScheduleAsync(other, otherAccount);
        var mine = await _data.InsertScheduleAsync(_user, _account);
        await _http.Delete(_user, mine);

        foreach (var id in new long[] { 99999, mine, foreign })
        {
            var (status, body) = await _http.Delete(_user, id);

            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe(BillsHttp.Error("Conta mensal não encontrada"));
        }

        (await _data.ScalarAsync<bool>("SELECT ativo FROM agendamentos WHERE id = @foreign", new { foreign })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "005-72")]
    public async Task Delete_BillWithTransactions_IsAccepted_AndTheLinkStays()
    {
        var id = await _data.InsertScheduleAsync(_user, _account);
        var transaction = await _data.Accounts.InsertTransactionAsync(_user, _account, -10m);
        await _data.ExecuteAsync("UPDATE transacoes SET agendamento_id = @id WHERE id = @transaction", new { id, transaction });

        var (status, _) = await _http.Delete(_user, id);

        status.ShouldBe(HttpStatusCode.OK);
        (await _data.ScalarAsync<int>("SELECT agendamento_id FROM transacoes WHERE id = @transaction", new { transaction })).ShouldBe(id);
    }

    [Fact]
    [Trait("AC", "005-85")]
    public async Task Delete_WithAnIdThatDoesNotFitAnInt_Is404Json()
    {
        var (status, body) = await _http.Delete(_user, long.MaxValue);
        var (bigStatus, big) = await _http.SendAsync(HttpMethod.Delete, "/api/bills/99999999999999999999", userId: _user);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(BillsHttp.Error("Conta mensal não encontrada"));
        bigStatus.ShouldBe(HttpStatusCode.NotFound);
        big.ShouldBe(BillsHttp.Error("Conta mensal não encontrada"));
    }
}

/// <summary>Roteamento do Flask reproduzido: 404 HTML antes da autenticação, 405, <c>summary</c> (spec 005 §2).</summary>
[Trait("Category", "Docker")]
public class BillsRoutingTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string FlaskNotFoundHtml =
        "<!doctype html>\n<html lang=en>\n<title>404 Not Found</title>\n<h1>Not Found</h1>\n" +
        "<p>The requested URL was not found on the server. If you entered the URL manually please check your spelling and try again.</p>\n";

    private readonly BillsHttp _http = new(postgres);
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await new BillsTestData(postgres.DataSource).InsertUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> NonMatchingPaths
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var method in new[] { "GET", "POST", "PUT", "DELETE" })
            {
                foreach (var path in new[]
                {
                    "/api/bills/", "/api/bills/abc", "/api/bills/-1", "/api/contas-mensais/1.5", "/api/contas-mensais/",
                    "/api/contas-mensais/1a", "/api/bills/summary/",
                })
                {
                    data.Add(method, path);
                }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NonMatchingPaths))]
    [Trait("AC", "005-82")]
    public async Task TrailingSlashOrNonNumericId_Returns404Html_EvenWithoutToken(string method, string path)
    {
        var (status, body) = await _http.SendAsync(new HttpMethod(method), path, json: method is "POST" or "PUT" ? "{}" : null);

        // GET /api/bills/abc: o literal summary é a única rota textual; "abc" não existe.
        status.ShouldBe(HttpStatusCode.NotFound, $"{method} {path}");
        body.ShouldBe(FlaskNotFoundHtml);
    }

    [Theory]
    [Trait("AC", "005-82")]
    [InlineData("/api/bills/abc")]
    [InlineData("/api/contas-mensais/-1")]
    public async Task NonNumericId_Returns404Html_WithAValidTokenToo(string path)
    {
        var (status, body) = await _http.SendAsync(HttpMethod.Delete, path, userId: _user);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(FlaskNotFoundHtml);
    }

    [Theory]
    [Trait("AC", "005-82")]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ContasMensaisSummary_DoesNotExist_Is404Html(string method)
    {
        var (status, body) = await _http.SendAsync(new HttpMethod(method), "/api/contas-mensais/summary", userId: _user);

        if (method == "GET")
        {
            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe(FlaskNotFoundHtml);
        }
        else
        {
            status.ShouldBe(HttpStatusCode.NotFound, $"{method} => {body}");
        }
    }

    [Theory]
    [Trait("AC", "005-83")]
    [InlineData("GET", "/api/bills/1")]
    [InlineData("POST", "/api/bills/1")]
    [InlineData("PATCH", "/api/bills/1")]
    [InlineData("DELETE", "/api/bills")]
    [InlineData("PUT", "/api/bills")]
    [InlineData("GET", "/api/contas-mensais/1")]
    [InlineData("DELETE", "/api/contas-mensais")]
    [InlineData("POST", "/api/bills/summary")]
    [InlineData("PUT", "/api/bills/summary")]
    [InlineData("DELETE", "/api/bills/summary")]
    [InlineData("PATCH", "/api/bills/summary")]
    public async Task MethodNotAllowed_Returns405(string method, string path)
    {
        var (status, body) = await _http.SendAsync(new HttpMethod(method), path, userId: _user);

        status.ShouldBe(HttpStatusCode.MethodNotAllowed, $"{method} {path} => {(int)status} {body}");
    }
}

/// <summary>Usuário B nunca vê, altera nem remove agendamento do usuário A (RN-005-02): as 9 operações.</summary>
[Trait("Category", "Docker")]
public class BillsTenantIsolationTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly BillsHttp _http = new(postgres);
    private readonly BillsTestData _data = new(postgres.DataSource);
    private int _a;
    private int _b;
    private int _accountA;
    private int _accountB;
    private int _billA;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _a = await _data.InsertUserAsync("5511900000001", "Ana");
        _b = await _data.InsertUserAsync("5511900000002", "Beto");
        _accountA = await _data.InsertAccountAsync(_a, "ContaDoA");
        _accountB = await _data.InsertAccountAsync(_b, "ContaDoB");
        _billA = await _data.InsertScheduleAsync(_a, _accountA, "SoDoA", valor: 900m, incluirNaReserva: true);
        await _data.InsertScheduleAsync(_b, _accountB, "SoDoB", valor: 5m);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task AssertBillOfAIsIntact()
    {
        var row = (await _data.QueryAsync<(string Descricao, bool Ativo, decimal Valor, int Conta)>(
            "SELECT descricao, ativo, valor_previsto, conta_id FROM agendamentos WHERE id = @_billA", new { _billA })).Single();
        row.ShouldBe(("SoDoA", true, 900m, _accountA));
    }

    [Theory]
    [Trait("AC", "005-04")]
    [Trait("AC", "005-86")]
    [InlineData("/api/bills")]
    [InlineData("/api/contas-mensais")]
    public async Task Get_EachUserSeesOnlyTheirOwn(string path)
    {
        var (_, forB) = await _http.Get(_b, path);
        var (_, forA) = await _http.Get(_a, path);

        forB.ShouldContain("SoDoB");
        forB.ShouldNotContain("SoDoA");
        forA.ShouldContain("SoDoA");
        forA.ShouldNotContain("SoDoB");
    }

    [Fact]
    [Trait("AC", "005-99")]
    public async Task Summary_NothingOfTheOtherUserEnters()
    {
        var (_, forB) = await _http.Get(_b, "/api/bills/summary");

        forB.ShouldContain("\"total_despesas_mensais\":5,");
        forB.ShouldContain("\"meta\":30,");
    }

    [Theory]
    [Trait("AC", "005-59")]
    [InlineData("/api/bills")]
    [InlineData("/api/contas-mensais")]
    public async Task Put_OnAnotherUsersBill_Is404_AndItIsUntouched(string path)
    {
        var (status, body) = await _http.Put(_b, _billA, """{"descricao":"Roubada","valor_previsto":1}""", path);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(BillsHttp.Error("Conta mensal não encontrada"));
        await AssertBillOfAIsIntact();
    }

    [Fact]
    [Trait("AC", "005-59")]
    public async Task Put_WithTheAccountOfAnotherUser_Is404()
    {
        var billB = await _data.InsertScheduleAsync(_b, _accountB, "DoB2");

        var (status, body) = await _http.Put(_b, billB, $$"""{"conta_id":{{_accountA}}}""");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(BillsHttp.Error("Conta bancária não encontrada"));
        (await _data.ScalarAsync<int>("SELECT conta_id FROM agendamentos WHERE id = @billB", new { billB })).ShouldBe(_accountB);
    }

    [Theory]
    [Trait("AC", "005-76")]
    [InlineData("/api/bills")]
    [InlineData("/api/contas-mensais")]
    public async Task Delete_OnAnotherUsersBill_Is404_AndItStaysActive(string path)
    {
        var (status, body) = await _http.Delete(_b, _billA, path);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(BillsHttp.Error("Conta mensal não encontrada"));
        await AssertBillOfAIsIntact();
    }

    [Theory]
    [Trait("AC", "005-24")]
    [InlineData("/api/bills")]
    [InlineData("/api/contas-mensais")]
    public async Task Post_WithTheAccountOfAnotherUser_Is404_AndNothingIsCreated(string path)
    {
        var json = $$"""{"descricao":"X","tipo_agendamento":"FIXO","periodicidade":"MENSAL","subcategoria_id":59,"conta_id":{{_accountA}},"dia_execucao":5,"valor_previsto":10}""";

        var (status, body) = await _http.Post(_b, json, path);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(BillsHttp.Error("Conta bancária não encontrada"));
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM agendamentos")).ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "005-25")]
    public async Task OwnerInTheBodyIsIgnored_TheBillBelongsToTheTokenUser()
    {
        var json = $$"""{"descricao":"Dele","tipo_agendamento":"FIXO","periodicidade":"MENSAL","subcategoria_id":59,"conta_id":{{_accountB}},"dia_execucao":5,"valor_previsto":10,"usuario_id":{{_a}}}""";

        var (status, _) = await _http.Post(_b, json);

        status.ShouldBe(HttpStatusCode.Created);
        (await _data.ScalarAsync<int>("SELECT usuario_id FROM agendamentos WHERE descricao = 'Dele'")).ShouldBe(_b);
    }

    [Fact]
    [Trait("AC", "005-26")]
    public async Task SubcategoryOfAnotherUser_IsRefused_SoItsNameNeverLeaks()
    {
        var foreign = await _data.InsertUserSubcategoryAsync(_a, "CategoriaSecretaDoA");
        var json = $$"""{"descricao":"X","tipo_agendamento":"FIXO","periodicidade":"MENSAL","subcategoria_id":{{foreign}},"conta_id":{{_accountB}},"dia_execucao":5,"valor_previsto":10}""";

        var (status, body) = await _http.Post(_b, json);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldNotContain("CategoriaSecretaDoA");
    }
}

/// <summary>500 com mensagem fixa e log só com o tipo do erro (desvio 008, AC-005-84).</summary>
[Trait("Category", "Docker")]
public class BillsFailureTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Secret = "segredo-interno 5511999999999 Internet";

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private const string ValidPost =
        """{"descricao":"Internet","tipo_agendamento":"FIXO","periodicidade":"MENSAL","subcategoria_id":59,"conta_id":3,"dia_execucao":5,"valor_previsto":10}""";

    public static TheoryData<string, string, string?, string> Operations => new()
    {
        { "GET", "/api/bills", null, "Erro ao carregar contas mensais" },
        { "POST", "/api/bills", ValidPost, "Erro ao criar conta mensal" },
        { "PUT", "/api/bills/7", """{"descricao":"Internet"}""", "Erro ao atualizar conta mensal" },
        { "DELETE", "/api/bills/7", null, "Erro ao remover conta mensal" },
        { "GET", "/api/bills/summary", null, "Erro ao calcular resumo" },
        { "GET", "/api/contas-mensais", null, "Erro ao carregar contas mensais" },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "005-84")]
    [Trait("AC", "005-08")]
    public async Task RepositoryFailure_Returns500WithTheFixedMessage_AndLogsOnlyTheErrorType(
        string method, string path, string? json, string message)
    {
        var log = new CapturingLogger();
        await using var factory = postgres.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddScoped<IBillRepository>(_ => new ThrowingRepository());
            services.AddSingleton<ILogger<BillsModule>>(log);
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
        body.ShouldBe(BillsHttp.Error(message));
        body.ShouldNotContain("segredo");
        log.Messages.Count.ShouldBe(1);
        log.Messages[0].ShouldStartWith(message);
        log.Messages[0].ShouldContain("PostgresException (SqlState 23505)");
        log.Messages[0].ShouldContain("usuário 42");
        log.Messages[0].ShouldNotContain("segredo");
        log.Messages[0].ShouldNotContain("5511999999999");
        log.Messages[0].ShouldNotContain("Internet");
    }

    private sealed class ThrowingRepository : IBillRepository
    {
        private static PostgresException Failure() => new(Secret, "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        public Task<IReadOnlyList<BillListItem>> ListActiveAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        public Task<CurrentBill?> GetCurrentAsync(int userId, int billId, CancellationToken cancellationToken) => throw Failure();

        public Task<bool> ActiveAccountOwnedAsync(int userId, int accountId, CancellationToken cancellationToken) => throw Failure();

        public Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken cancellationToken) => throw Failure();

        public Task<BillDetails> CreateAsync(int userId, NewBillData data, CancellationToken cancellationToken) => throw Failure();

        public Task<BillDetails?> UpdateAsync(int userId, int billId, BillChanges changes, CancellationToken cancellationToken) => throw Failure();

        public Task<bool> DeactivateAsync(int userId, int billId, CancellationToken cancellationToken) => throw Failure();

        public Task<IReadOnlyList<SummaryRow>> ListSummaryRowsAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        public Task<IReadOnlyList<PeriodTotal>> ListReserveTotalsAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        public Task<IReadOnlyList<ReserveItemRow>> ListReserveItemsAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        public Task<int> GetReserveMonthsAsync(int userId, CancellationToken cancellationToken) => throw Failure();
    }

    private sealed class CapturingLogger : ILogger<BillsModule>
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
