using System.Net;
using Dapper;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Accounts;

/// <summary>Spec 002 §3.4: <c>DELETE /api/accounts/{id}</c> e <c>/api/contas/{id}</c>.</summary>
[Trait("Category", "Docker")]
public class AccountsDeleteEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly AccountsHttp _http = new(postgres);
    private readonly AccountsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _id;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync();
        _id = await _data.InsertAccountAsync(_user, "Carteira");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<bool> IsActive(int id) => _data.ScalarAsync<bool>("SELECT ativa FROM contas WHERE id = @id", new { id });

    [Fact]
    [Trait("AC", "002-70")]
    public async Task AccountWithoutTransactions_Returns200WithTheExactBody_AndKeepsTheRowInactive()
    {
        var (status, body) = await _http.Delete(_user, _id);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","message":"Conta removida com sucesso"}""");
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM contas WHERE id = @_id", new { _id })).ShouldBe(1);
        (await IsActive(_id)).ShouldBeFalse();
        (await _http.Get(_user)).Body.ShouldBe("""{"status":"success","data":[]}""");
    }

    [Fact]
    [Trait("AC", "002-71")]
    public async Task AccountWithTransactions_Returns400WithTheCount_AndStaysActive()
    {
        await _data.InsertTransactionAsync(_user, _id, -1m);
        await _data.InsertTransactionAsync(_user, _id, 2m);
        await _data.InsertTransactionAsync(_user, _id, -3m, consolidada: false);

        var (status, body) = await _http.Delete(_user, _id);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AccountsHttp.Error("Não é possível excluir esta conta. Existem 3 transações vinculadas."));
        (await IsActive(_id)).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "002-72")]
    public async Task UnknownInactiveAndForeignAccounts_Return404_AndTheOthersStayActive()
    {
        var other = await _data.InsertUserAsync("5511900000002");
        var foreign = await _data.InsertAccountAsync(other, "DoOutro");
        var inactive = await _data.InsertAccountAsync(_user, "Inativa", ativa: false);

        foreach (var id in new[] { 99999, inactive, foreign })
        {
            var (status, body) = await _http.Delete(_user, id);

            status.ShouldBe(HttpStatusCode.NotFound, $"id {id}");
            body.ShouldBe(AccountsHttp.Error("Conta não encontrada"));
        }

        (await IsActive(foreign)).ShouldBeTrue();
        (await IsActive(_id)).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "002-73")]
    public async Task SecondDelete_Returns404()
    {
        (await _http.Delete(_user, _id)).Status.ShouldBe(HttpStatusCode.OK);

        var (status, body) = await _http.Delete(_user, _id);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AccountsHttp.Error("Conta não encontrada"));
    }

    [Fact]
    [Trait("AC", "002-74")]
    public async Task RecreatingARemovedAccount_ReactivatesTheSameId()
    {
        await _http.Delete(_user, _id);

        var (status, body) = await _http.Post(_user, """{"nome_conta":"Carteira","tipo_conta":"Dinheiro"}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain($"\"id\":{_id},\"nome_conta\":\"Carteira\"");
        (await IsActive(_id)).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "002-75")]
    public async Task DeleteWaitsForAnInFlightTransaction_ThenAnswers400WithTheCount()
    {
        await using var writer = await postgres.DataSource.OpenConnectionAsync();
        await using var open = await writer.BeginTransactionAsync();
        await AccountsTestData.InsertTransactionAsync(writer, open, _user, _id, -10m);

        var delete = Task.Run(() => _http.Delete(_user, _id));
        await WaitUntilBlockedOnALockAsync(delete);
        delete.IsCompleted.ShouldBeFalse();

        await open.CommitAsync();

        var (status, body) = await delete;
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AccountsHttp.Error("Não é possível excluir esta conta. Existem 1 transações vinculadas."));
        (await IsActive(_id)).ShouldBeTrue();
    }

    private async Task WaitUntilBlockedOnALockAsync(Task pending)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !pending.IsCompleted)
        {
            var waiting = await _data.ScalarAsync<int>(
                "SELECT count(*)::int FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query ILIKE '%FOR UPDATE%' AND pid <> pg_backend_pid()");
            if (waiting > 0)
            {
                return;
            }
            await Task.Delay(25);
        }
        pending.IsCompleted.ShouldBeFalse("o DELETE terminou sem esperar a transação em andamento");
    }

    [Fact]
    [Trait("AC", "002-76")]
    public async Task ActiveSchedules_BlockTheRemoval_InactiveOnesDoNot()
    {
        await _data.InsertScheduleAsync(_user, _id);
        await _data.InsertScheduleAsync(_user, _id);
        await _data.InsertScheduleAsync(_user, _id, ativo: false);

        var (status, body) = await _http.Delete(_user, _id);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AccountsHttp.Error("Não é possível excluir esta conta. Existem 2 agendamentos ativos vinculados."));
        (await IsActive(_id)).ShouldBeTrue();

        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE agendamentos SET ativo = false");
        (await _http.Delete(_user, _id)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("AC", "002-76")]
    public async Task TransactionsAreReportedBeforeSchedules()
    {
        await _data.InsertTransactionAsync(_user, _id, -1m);
        await _data.InsertScheduleAsync(_user, _id);

        (await _http.Delete(_user, _id)).Body.ShouldBe(
            AccountsHttp.Error("Não é possível excluir esta conta. Existem 1 transações vinculadas."));
    }

    [Fact]
    [Trait("AC", "002-77")]
    public async Task DefaultAccountColumnsPointingToTheRemovedAccount_AreCleared()
    {
        var kept = await _data.InsertAccountAsync(_user, "Mantida");
        await _data.SetDefaultAccountsAsync(_user, padrao: _id, renda: _id, despesa: kept);

        (await _http.Delete(_user, _id)).Status.ShouldBe(HttpStatusCode.OK);

        var defaults = (await _data.QueryAsync<(int? Padrao, int? Renda, int? Despesa)>(
            "SELECT conta_padrao_id, conta_padrao_renda_id, conta_padrao_despesa_id FROM usuarios WHERE id = @_user", new { _user })).Single();
        defaults.ShouldBe((null, null, kept));
    }

    [Fact]
    [Trait("AC", "002-77")]
    public async Task BlockedRemoval_LeavesTheDefaultsAlone()
    {
        await _data.InsertScheduleAsync(_user, _id);
        await _data.SetDefaultAccountsAsync(_user, _id, _id, _id);

        (await _http.Delete(_user, _id)).Status.ShouldBe(HttpStatusCode.BadRequest);

        (await _data.ScalarAsync<int>(
            "SELECT count(*)::int FROM usuarios WHERE conta_padrao_id = @_id AND conta_padrao_renda_id = @_id AND conta_padrao_despesa_id = @_id", new { _id })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "002-86")]
    public async Task IdThatDoesNotFitAnInt_Returns404Json()
    {
        var (status, body) = await _http.SendAsync(HttpMethod.Delete, "/api/accounts/99999999999999999999", userId: _user);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AccountsHttp.Error("Conta não encontrada"));
    }

    [Fact]
    [Trait("AC", "002-82")]
    public async Task Alias_Contas_BehavesTheSame()
    {
        var (status, body) = await _http.Delete(_user, _id, "/api/contas");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","message":"Conta removida com sucesso"}""");
        (await _http.Delete(_user, _id, "/api/contas")).Status.ShouldBe(HttpStatusCode.NotFound);
    }
}
