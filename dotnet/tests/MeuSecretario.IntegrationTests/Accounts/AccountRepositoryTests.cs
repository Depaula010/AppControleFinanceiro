using MeuSecretario.Application.Accounts;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Infrastructure.Accounts;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Accounts;

[Trait("Category", "Docker")]
public class AccountRepositoryTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly AccountRepository _repository = new(postgres.Connections);
    private readonly AccountsTestData _data = new(postgres.DataSource);

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static NewAccountData New(string name = "Nubank", string type = "Conta Corrente", decimal balance = 0m,
        string? color = "#3B82F6", string? icon = "wallet", decimal? limit = null, int? closing = null, int? due = null) =>
        new(name, type, balance, color, icon, limit, closing, due);

    private static AccountChanges Changes(
        Change<string> nome = default, Change<string> tipo = default, Change<decimal> saldo = default,
        Change<string?> cor = default, Change<string?> icone = default, Change<decimal?> limite = default,
        Change<int?> fechamento = default, Change<int?> vencimento = default) =>
        new(nome, tipo, saldo, cor, icone, limite, fechamento, vencimento);

    // ============================== listar ==============================

    [Fact]
    [Trait("AC", "002-03")]
    public async Task List_BalanceCountsOnlyConsolidatedTransactionsOfThatAccount()
    {
        var user = await _data.InsertUserAsync();
        var account = await _data.InsertAccountAsync(user, "Carteira", saldoInicial: 100m);
        var other = await _data.InsertAccountAsync(user, "Outra", saldoInicial: 5m);
        await _data.InsertTransactionAsync(user, account, -30m);
        await _data.InsertTransactionAsync(user, account, -50m, consolidada: false);
        await _data.InsertTransactionAsync(user, other, 1000m);

        var list = await _repository.ListActiveAsync(user, CancellationToken.None);

        list.Single(a => a.NomeConta == "Carteira").Saldo.ShouldBe(70m);
        list.Single(a => a.NomeConta == "Outra").Saldo.ShouldBe(1005m);
    }

    [Fact]
    [Trait("AC", "002-03")]
    public async Task List_WithoutTransactions_BalanceIsTheInitialBalance()
    {
        var user = await _data.InsertUserAsync();
        await _data.InsertAccountAsync(user, "Carteira", saldoInicial: 150.5m);

        var item = (await _repository.ListActiveAsync(user, CancellationToken.None)).Single();

        item.Saldo.ShouldBe(150.5m);
        item.SaldoInicial.ShouldBe(150.5m);
    }

    [Fact]
    [Trait("AC", "002-04")]
    public async Task List_HidesInactiveAccountsAndOtherUsersAccounts()
    {
        var a = await _data.InsertUserAsync("5511900000001");
        var b = await _data.InsertUserAsync("5511900000002");
        await _data.InsertAccountAsync(a, "Ativa");
        await _data.InsertAccountAsync(a, "Inativa", ativa: false);
        await _data.InsertAccountAsync(b, "Do B");

        var list = await _repository.ListActiveAsync(a, CancellationToken.None);

        list.Select(x => x.NomeConta).ShouldBe(["Ativa"]);
    }

    [Fact]
    [Trait("AC", "002-02")]
    public async Task List_IsOrderedByTypeThenName()
    {
        var user = await _data.InsertUserAsync();
        await _data.InsertAccountAsync(user, "Zeta", "Dinheiro");
        await _data.InsertAccountAsync(user, "Beta", "Dinheiro");
        await _data.InsertAccountAsync(user, "Alfa", "Conta Corrente");
        await _data.InsertAccountAsync(user, "Gama", "Investimento");

        var list = await _repository.ListActiveAsync(user, CancellationToken.None);

        list.Select(x => x.NomeConta).ShouldBe(["Alfa", "Beta", "Zeta", "Gama"]);
    }

    [Fact]
    [Trait("AC", "002-07")]
    public async Task List_CarriesTheExtraFields()
    {
        var user = await _data.InsertUserAsync();
        await _data.InsertAccountAsync(user, "Nubank", AccountTypes.Card, 10m, fechamento: 5, vencimento: 10, cor: "#112233", icone: "card", limite: 5000m);

        var item = (await _repository.ListActiveAsync(user, CancellationToken.None)).Single();

        item.ShouldBe(new AccountListItem(item.Id, "Nubank", AccountTypes.Card, 10m, "#112233", "card", 5000m, 5, 10, 10m));
    }

    // ============================== buscar e nome em uso ==============================

    [Fact]
    [Trait("AC", "002-41")]
    public async Task GetActive_ReturnsOnlyActiveAccountsOfTheUser()
    {
        var a = await _data.InsertUserAsync("5511900000001");
        var b = await _data.InsertUserAsync("5511900000002");
        var active = await _data.InsertAccountAsync(a, "Ativa");
        var inactive = await _data.InsertAccountAsync(a, "Inativa", ativa: false);

        (await _repository.GetActiveAsync(a, active, CancellationToken.None))!.NomeConta.ShouldBe("Ativa");
        (await _repository.GetActiveAsync(a, inactive, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetActiveAsync(b, active, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetActiveAsync(a, 99999, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-44")]
    public async Task NameTaken_LooksAtActiveAndInactiveAccounts_ExceptTheOneBeingEdited()
    {
        var a = await _data.InsertUserAsync("5511900000001");
        var b = await _data.InsertUserAsync("5511900000002");
        var self = await _data.InsertAccountAsync(a, "Propria");
        await _data.InsertAccountAsync(a, "Outra");
        await _data.InsertAccountAsync(a, "Antiga", ativa: false);
        await _data.InsertAccountAsync(b, "DoB");

        (await _repository.NameTakenAsync(a, "Outra", self, CancellationToken.None)).ShouldBeTrue();
        (await _repository.NameTakenAsync(a, "Antiga", self, CancellationToken.None)).ShouldBeTrue();
        (await _repository.NameTakenAsync(a, "Propria", self, CancellationToken.None)).ShouldBeFalse();
        (await _repository.NameTakenAsync(a, "DoB", self, CancellationToken.None)).ShouldBeFalse();
        (await _repository.NameTakenAsync(a, "outra", self, CancellationToken.None)).ShouldBeFalse();
    }

    // ============================== criar e reativar ==============================

    [Fact]
    [Trait("AC", "002-10")]
    public async Task Create_InsertsActiveAccountForTheUser()
    {
        var user = await _data.InsertUserAsync();

        var result = await _repository.CreateAsync(user, New(limit: 0m), CancellationToken.None);

        var created = result.ShouldBeOfType<CreateAccountResult.Created>().Account;
        created.ShouldBe(new AccountDetails(created.Id, "Nubank", "Conta Corrente", 0m, "#3B82F6", "wallet", 0m, null, null));
        (await _data.ScalarAsync<bool>("SELECT ativa AND inclui_saldo_total FROM contas WHERE id = @id", new { id = created.Id })).ShouldBeTrue();
        (await _data.ScalarAsync<int>("SELECT usuario_id FROM contas WHERE id = @id", new { id = created.Id })).ShouldBe(user);
    }

    [Fact]
    [Trait("AC", "002-20")]
    public async Task Create_WithTheNameOfAnActiveAccount_IsNameTaken_AndSameNameWithOtherCaseIsFine()
    {
        var user = await _data.InsertUserAsync();
        await _data.InsertAccountAsync(user, "Nubank");

        (await _repository.CreateAsync(user, New("Nubank"), CancellationToken.None)).ShouldBeOfType<CreateAccountResult.NameTaken>();
        (await _repository.CreateAsync(user, New("nubank"), CancellationToken.None)).ShouldBeOfType<CreateAccountResult.Created>();
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM contas")).ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "002-21")]
    public async Task Create_WithTheNameOfAnInactiveAccount_ReactivatesItWithTheNewValues()
    {
        var user = await _data.InsertUserAsync();
        var old = await _data.InsertAccountAsync(
            user, "Nubank", AccountTypes.Card, 99m, ativa: false, fechamento: 5, vencimento: 10, cor: "#000000", icone: "old", limite: 1m);

        var result = await _repository.CreateAsync(user, New("Nubank", "Dinheiro", 7m, null, null), CancellationToken.None);

        var account = result.ShouldBeOfType<CreateAccountResult.Created>().Account;
        account.ShouldBe(new AccountDetails(old, "Nubank", "Dinheiro", 7m, null, null, null, null, null));
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM contas")).ShouldBe(1);
        (await _data.ScalarAsync<bool>("SELECT ativa AND inclui_saldo_total FROM contas WHERE id = @old", new { old })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "002-22")]
    public async Task Create_SameNameInAnotherUser_IsIndependent()
    {
        var a = await _data.InsertUserAsync("5511900000001");
        var b = await _data.InsertUserAsync("5511900000002");
        var inactiveOfB = await _data.InsertAccountAsync(b, "Nubank", ativa: false);
        await _data.InsertAccountAsync(a, "Nubank");

        // O usuário A tem "Nubank" ativa; o B tem "Nubank" inativa: criar para o B reativa a do B e não toca a do A.
        var result = await _repository.CreateAsync(b, New("Nubank"), CancellationToken.None);

        result.ShouldBeOfType<CreateAccountResult.Created>().Account.Id.ShouldBe(inactiveOfB);
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM contas WHERE usuario_id = @a", new { a })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "002-23")]
    public async Task Create_TwoSimultaneousRequestsWithTheSameNewName_OneCreatesAndTheOtherIsNameTaken()
    {
        var user = await _data.InsertUserAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            Task.Run(() => _repository.CreateAsync(user, New("Concorrente"), CancellationToken.None))));

        results.OfType<CreateAccountResult.Created>().Count().ShouldBe(1);
        results.OfType<CreateAccountResult.NameTaken>().Count().ShouldBe(1);
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM contas")).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "002-19")]
    public async Task Create_ViolatingTheCardCheck_PropagatesAs23514()
    {
        var user = await _data.InsertUserAsync();

        // O Domain já recusa isso (desvio 034); o CHECK do banco é a rede de segurança e propaga como exceção (500).
        var ex = await Should.ThrowAsync<PostgresException>(() =>
            _repository.CreateAsync(user, New(type: "Dinheiro", closing: 5, due: 10), CancellationToken.None));

        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        ex.ConstraintName.ShouldBe("chk_cartao_credito");
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM contas")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "002-23")]
    public async Task UniqueConstraint_HasTheNameTheSpecAssumes()
    {
        var user = await _data.InsertUserAsync();
        await _data.InsertAccountAsync(user, "Nubank");

        var ex = await Should.ThrowAsync<PostgresException>(() => _data.InsertAccountAsync(user, "Nubank"));

        ex.ConstraintName.ShouldBe("contas_usuario_id_nome_conta_key");
    }

    // ============================== atualizar ==============================

    [Fact]
    [Trait("AC", "002-42")]
    public async Task Update_ChangesOnlyTheFieldsThatAreSet()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Velha", "Dinheiro", 10m, cor: "#112233", icone: "x", limite: 20m);
        var before = await _data.ScalarAsync<DateTime>("SELECT updated_at FROM contas WHERE id = @id", new { id });
        await Task.Delay(20);

        var result = await _repository.UpdateAsync(user, id, Changes(nome: Change.Set("Nova")), CancellationToken.None);

        result.ShouldBeOfType<UpdateAccountResult.Updated>().Account.ShouldBe(
            new AccountDetails(id, "Nova", "Dinheiro", 10m, "#112233", "x", 20m, null, null));
        (await _data.ScalarAsync<DateTime>("SELECT updated_at FROM contas WHERE id = @id", new { id })).ShouldBeGreaterThan(before);
    }

    [Fact]
    [Trait("AC", "002-47")]
    public async Task Update_NullClearsNullableColumns()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Nubank", AccountTypes.Card, fechamento: 5, vencimento: 10, limite: 20m);

        var result = await _repository.UpdateAsync(
            user, id,
            Changes(cor: Change.Set<string?>(null), icone: Change.Set<string?>(null), limite: Change.Set<decimal?>(null),
                    fechamento: Change.Set<int?>(null), vencimento: Change.Set<int?>(null), tipo: Change.Set("Dinheiro")),
            CancellationToken.None);

        result.ShouldBeOfType<UpdateAccountResult.Updated>().Account.ShouldBe(
            new AccountDetails(id, "Nubank", "Dinheiro", 0m, null, null, null, null, null));
    }

    [Fact]
    [Trait("AC", "002-41")]
    public async Task Update_OfInactiveOrForeignAccount_IsNotFound_AndNothingChanges()
    {
        var a = await _data.InsertUserAsync("5511900000001");
        var b = await _data.InsertUserAsync("5511900000002");
        var foreign = await _data.InsertAccountAsync(a, "DoA");
        var inactive = await _data.InsertAccountAsync(b, "Inativa", ativa: false);

        (await _repository.UpdateAsync(b, foreign, Changes(nome: Change.Set("Roubada")), CancellationToken.None)).ShouldBeOfType<UpdateAccountResult.NotFound>();
        (await _repository.UpdateAsync(b, inactive, Changes(nome: Change.Set("Viva")), CancellationToken.None)).ShouldBeOfType<UpdateAccountResult.NotFound>();

        (await _data.ScalarAsync<string>("SELECT nome_conta FROM contas WHERE id = @foreign", new { foreign })).ShouldBe("DoA");
        (await _data.ScalarAsync<string>("SELECT nome_conta FROM contas WHERE id = @inactive", new { inactive })).ShouldBe("Inativa");
    }

    [Fact]
    [Trait("AC", "002-44")]
    public async Task Update_RenamingToAnExistingName_IsNameTaken_ThroughTheUniqueConstraint()
    {
        var user = await _data.InsertUserAsync();
        await _data.InsertAccountAsync(user, "Existente");
        var id = await _data.InsertAccountAsync(user, "Outra");

        var result = await _repository.UpdateAsync(user, id, Changes(nome: Change.Set("Existente")), CancellationToken.None);

        result.ShouldBeOfType<UpdateAccountResult.NameTaken>();
        (await _data.ScalarAsync<string>("SELECT nome_conta FROM contas WHERE id = @id", new { id })).ShouldBe("Outra");
    }

    [Fact]
    [Trait("AC", "002-52")]
    public async Task Update_ViolatingTheCardCheck_PropagatesAndChangesNothing()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Carteira");

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            _repository.UpdateAsync(user, id, Changes(nome: Change.Set("Mudou"), fechamento: Change.Set<int?>(5)), CancellationToken.None));

        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        (await _data.ScalarAsync<string>("SELECT nome_conta FROM contas WHERE id = @id", new { id })).ShouldBe("Carteira");
    }

    // ============================== remover ==============================

    [Fact]
    [Trait("AC", "002-70")]
    public async Task Delete_DeactivatesTheAccount_AndKeepsTheRow()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Carteira");

        (await _repository.SoftDeleteAsync(user, id, CancellationToken.None)).ShouldBeOfType<DeleteAccountResult.Deleted>();

        (await _data.ScalarAsync<bool>("SELECT ativa FROM contas WHERE id = @id", new { id })).ShouldBeFalse();
        (await _repository.SoftDeleteAsync(user, id, CancellationToken.None)).ShouldBeOfType<DeleteAccountResult.NotFound>();
    }

    [Fact]
    [Trait("AC", "002-71")]
    public async Task Delete_WithTransactions_IsBlockedWithTheExactCount()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Carteira");
        await _data.InsertTransactionAsync(user, id, -1m);
        await _data.InsertTransactionAsync(user, id, 2m);
        await _data.InsertTransactionAsync(user, id, -3m, consolidada: false);

        (await _repository.SoftDeleteAsync(user, id, CancellationToken.None)).ShouldBe(new DeleteAccountResult.HasTransactions(3));

        (await _data.ScalarAsync<bool>("SELECT ativa FROM contas WHERE id = @id", new { id })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "002-72")]
    public async Task Delete_OfForeignOrUnknownAccount_IsNotFound_AndTheOtherStaysActive()
    {
        var a = await _data.InsertUserAsync("5511900000001");
        var b = await _data.InsertUserAsync("5511900000002");
        var id = await _data.InsertAccountAsync(a, "DoA");

        (await _repository.SoftDeleteAsync(b, id, CancellationToken.None)).ShouldBeOfType<DeleteAccountResult.NotFound>();
        (await _repository.SoftDeleteAsync(a, 99999, CancellationToken.None)).ShouldBeOfType<DeleteAccountResult.NotFound>();

        (await _data.ScalarAsync<bool>("SELECT ativa FROM contas WHERE id = @id", new { id })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "002-76")]
    public async Task Delete_WithActiveSchedules_IsBlocked_ButInactiveSchedulesDoNotCount()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Carteira");
        await _data.InsertScheduleAsync(user, id);
        await _data.InsertScheduleAsync(user, id);
        await _data.InsertScheduleAsync(user, id, ativo: false);

        (await _repository.SoftDeleteAsync(user, id, CancellationToken.None)).ShouldBe(new DeleteAccountResult.HasActiveSchedules(2));

        await _data.QueryAsync<int>("UPDATE agendamentos SET ativo = false WHERE conta_id = @id RETURNING id", new { id });
        (await _repository.SoftDeleteAsync(user, id, CancellationToken.None)).ShouldBeOfType<DeleteAccountResult.Deleted>();
    }

    [Fact]
    [Trait("AC", "002-76")]
    public async Task Delete_WithTransactionsAndSchedules_ReportsTheTransactionsFirst()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Carteira");
        await _data.InsertTransactionAsync(user, id, -1m);
        await _data.InsertScheduleAsync(user, id);

        (await _repository.SoftDeleteAsync(user, id, CancellationToken.None)).ShouldBe(new DeleteAccountResult.HasTransactions(1));
    }

    [Fact]
    [Trait("AC", "002-77")]
    public async Task Delete_ClearsOnlyTheDefaultAccountColumnsThatPointToIt()
    {
        var user = await _data.InsertUserAsync();
        var removed = await _data.InsertAccountAsync(user, "Removida");
        var kept = await _data.InsertAccountAsync(user, "Mantida");
        await _data.SetDefaultAccountsAsync(user, padrao: removed, renda: removed, despesa: kept);

        (await _repository.SoftDeleteAsync(user, removed, CancellationToken.None)).ShouldBeOfType<DeleteAccountResult.Deleted>();

        var defaults = (await _data.QueryAsync<(int? Padrao, int? Renda, int? Despesa)>(
            "SELECT conta_padrao_id, conta_padrao_renda_id, conta_padrao_despesa_id FROM usuarios WHERE id = @user", new { user })).Single();
        defaults.ShouldBe((null, null, kept));
    }

    [Fact]
    [Trait("AC", "002-77")]
    public async Task Delete_BlockedByTransactions_LeavesTheDefaultAccountsAlone()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Padrao");
        await _data.InsertTransactionAsync(user, id, -1m);
        await _data.SetDefaultAccountsAsync(user, id, id, id);

        await _repository.SoftDeleteAsync(user, id, CancellationToken.None);

        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM usuarios WHERE conta_padrao_id = @id AND conta_padrao_renda_id = @id AND conta_padrao_despesa_id = @id", new { id })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "002-75")]
    public async Task Delete_WaitsForAnInFlightTransactionInsert_ThenSeesItAndRefuses()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Carteira");

        await using var writer = await postgres.DataSource.OpenConnectionAsync();
        await using var open = await writer.BeginTransactionAsync();
        await AccountsTestData.InsertTransactionAsync(writer, open, user, id, -10m);

        var delete = Task.Run(() => _repository.SoftDeleteAsync(user, id, CancellationToken.None));
        await WaitUntilBlockedOnALockAsync(delete);
        delete.IsCompleted.ShouldBeFalse();

        await open.CommitAsync();

        (await delete).ShouldBe(new DeleteAccountResult.HasTransactions(1));
        (await _data.ScalarAsync<bool>("SELECT ativa FROM contas WHERE id = @id", new { id })).ShouldBeTrue();
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
}
