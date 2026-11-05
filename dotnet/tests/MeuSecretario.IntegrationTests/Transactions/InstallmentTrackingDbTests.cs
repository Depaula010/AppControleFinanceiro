using System.Globalization;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Infrastructure.Categories;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.Infrastructure.Transactions;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary>Desvio 271: compra parcelada que chega como despesa com <c>(n/N)</c> vira conta parcelada; carga inicial da migration 0120.</summary>
[Trait("Category", "Docker")]
public class InstallmentTrackingDbTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const int ExpenseSubcategory = TransactionsTestData.ExpenseSubcategory;

    private readonly TransactionsTestData _data = new(postgres.DataSource);
    private TransactionPosting _posting = null!;
    private int _user;
    private int _checking;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync("5511900000071", "Ana");
        _checking = await _data.InsertAccountAsync(_user, "Conta Corrente");
        _posting = new TransactionPosting(new TransactionsUnitOfWork(postgres.Connections), new CategoryRepository(postgres.Connections));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<PostingResult<PostedTransaction>> PostAsync(string description, string date) =>
        _posting.PostSimpleAsync(
            new PostSimpleTransaction(_user, _checking, ExpenseSubcategory, TransactionKind.Expense, 511m, description, Day(date), Origin: "automate"),
            CancellationToken.None);

    private static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<(int Id, string Desc, decimal Valor, int Dia, int Total, int Exec, bool Ativo, DateTime Inicio)> ScheduleAsync() =>
        (await _data.QueryAsync<(int, string, decimal, int, int, int, bool, DateTime)>(
            """
            SELECT id, descricao, valor_previsto, dia_execucao, total_parcelas, parcelas_executadas, ativo, data_inicio
            FROM agendamentos WHERE usuario_id = @user AND tipo_agendamento = 'PARCELADO'
            """,
            new { user = _user })).Single();

    private async Task<int?> LinkOf(int transactionId) =>
        (await _data.QueryAsync<int?>("SELECT agendamento_id FROM transacoes WHERE id = @transactionId", new { transactionId })).Single();

    [Fact]
    [Trait("Desvio", "271")]
    public async Task ExpenseWithSuffix_CreatesTheSchedule_ThenTheNextInstallmentAdvancesIt()
    {
        var first = ((PostingResult<PostedTransaction>.Ok)await PostAsync("ZP ROGGI 62899 (1/6)", "2026-08-28")).Value;

        var schedule = await ScheduleAsync();
        (schedule.Desc, schedule.Valor, schedule.Dia, schedule.Total, schedule.Exec, schedule.Ativo, DateOnly.FromDateTime(schedule.Inicio))
            .ShouldBe(("ZP ROGGI 62899", 511m, 28, 6, 1, true, Day("2026-09-28")));
        (await LinkOf(first.TransactionId)).ShouldBe(schedule.Id);

        var second = ((PostingResult<PostedTransaction>.Ok)await PostAsync("zp roggi 62899 (2/6)", "2026-09-28")).Value;

        (await ScheduleAsync()).Exec.ShouldBe(2);
        (await LinkOf(second.TransactionId)).ShouldBe(schedule.Id);
    }

    [Fact]
    [Trait("Desvio", "271")]
    public async Task TheLastInstallment_DeactivatesTheSchedule()
    {
        await PostAsync("Curso (1/2)", "2026-08-10");
        await PostAsync("Curso (2/2)", "2026-09-10");

        var schedule = await ScheduleAsync();
        (schedule.Exec, schedule.Ativo).ShouldBe((2, false));
    }

    [Fact]
    [Trait("Desvio", "271")]
    public async Task Migration0120_CreatesTheOpenPurchases_SkipsTheFinishedAndTheEnded_AndIsIdempotent()
    {
        var today = DateOnly.FromDateTime((await _data.ScalarAsync<DateTime>("SELECT CURRENT_DATE::timestamp")));
        string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Aberta: 1ª de 6 há dois meses e um dia ⇒ as parcelas 2 e 3 já passaram; faltam 3.
        var purchase = today.AddMonths(-2).AddDays(-1);
        var open = await _data.InsertTransactionAsync(_user, _checking, valor: -511m, data: Text(purchase), descricao: "ZP ROGGI 62899 (1/6)");
        // Terminada: a última parcela já foi lançada.
        await _data.InsertTransactionAsync(_user, _checking, valor: -50m, data: Text(today.AddMonths(-1)), descricao: "KABUM (5/5)");
        // Acabou antes: o mesmo extrato trouxe lançamentos depois da data da parcela seguinte, e ela não veio.
        var ended = await _data.InsertTransactionAsync(_user, _checking, valor: -45m, data: Text(today.AddMonths(-6)), descricao: "DUO GOURMET (3/12)");
        var later = await _data.InsertTransactionAsync(_user, _checking, valor: -1m, data: Text(today.AddMonths(-1)), descricao: "Padaria");
        await _data.ExecuteAsync("UPDATE transacoes SET origem = 'import_inter' WHERE id IN (@ended, @later)", new { ended, later });

        await _data.ExecuteAsync(MigrationRunner.ReadScript("0120_parcelados_em_aberto"));
        await _data.ExecuteAsync(MigrationRunner.ReadScript("0120_parcelados_em_aberto"));

        var schedule = await ScheduleAsync();
        (schedule.Desc, schedule.Valor, schedule.Total, schedule.Exec, schedule.Ativo, schedule.Dia)
            .ShouldBe(("ZP ROGGI 62899", 511m, 6, 3, true, purchase.Day));
        DateOnly.FromDateTime(schedule.Inicio).ShouldBe(purchase.AddMonths(3));
        (await LinkOf(open)).ShouldBe(schedule.Id);
    }
}
