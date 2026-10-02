using Dapper;
using MeuSecretario.Application.Auth;
using MeuSecretario.Domain.Auth;
using MeuSecretario.Infrastructure.Auth;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Auth;

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class UserRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly UserRepository _repository = new(postgres.Connections);

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static NewUser Ana(string whatsapp = "5511999999999", string apiKey = "chave-cifrada") =>
        new("Ana Souza", whatsapp, "pbkdf2:sha256:1000$salt$hash", apiKey);

    private async Task<int> Count(string sql)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    [Fact]
    [Trait("AC", "001-01")]
    public async Task Create_InsertsTheUserAndTheThreeDefaultAccounts()
    {
        var result = await _repository.CreateWithDefaultAccountsAsync(Ana(), DefaultAccounts.For(10, 5), CancellationToken.None);

        result.UserId.ShouldNotBeNull();
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        var accounts = (await connection.QueryAsync<(string Nome, string Tipo, int? Venc, int? Fech, bool Ativa, decimal Saldo)>(
            "SELECT nome_conta, tipo_conta, dia_vencimento, dia_fechamento, ativa, saldo_inicial FROM contas WHERE usuario_id = @id ORDER BY id",
            new { id = result.UserId })).ToList();

        accounts.ShouldBe(
        [
            ("Carteira", "Dinheiro", null, null, true, 0m),
            ("Conta Corrente", "Conta Corrente", null, null, true, 0m),
            ("Cartão de Crédito", "Cartão de Crédito", 10, 5, true, 0m),
        ]);
    }

    [Fact]
    public async Task FindByWhatsapp_ReturnsTheStoredUser_AndNullForUnknown()
    {
        var created = await _repository.CreateWithDefaultAccountsAsync(Ana(), DefaultAccounts.For(10, 5), CancellationToken.None);

        var found = await _repository.FindByWhatsappAsync("5511999999999", CancellationToken.None);

        found.ShouldBe(new UserRecord(created.UserId!.Value, "Ana Souza", "5511999999999", "pbkdf2:sha256:1000$salt$hash"));
        (await _repository.FindByWhatsappAsync("5500000000000", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "001-08")]
    public async Task Create_WithAnExistingNumber_ReturnsTaken_AndWritesNothingElse()
    {
        await _repository.CreateWithDefaultAccountsAsync(Ana(), DefaultAccounts.For(10, 5), CancellationToken.None);

        var second = await _repository.CreateWithDefaultAccountsAsync(
            Ana(apiKey: "outra-chave") with { Nome = "Outra Pessoa" }, DefaultAccounts.For(1, 2), CancellationToken.None);

        second.WhatsappTaken.ShouldBeTrue();
        (await Count("SELECT count(*) FROM usuarios")).ShouldBe(1);
        (await Count("SELECT count(*) FROM contas")).ShouldBe(3);
    }

    [Fact]
    [Trait("AC", "001-11")]
    public async Task Create_WhenAnAccountInsertFails_RollsBackTheUserToo()
    {
        // Tipo fora do CHECK contas_tipo_conta_check na 3ª conta: a transação inteira é desfeita.
        var accounts = new List<NewAccount>(DefaultAccounts.For(10, 5)) { new("Inválida", "Tipo Inexistente", null, null) };

        await Should.ThrowAsync<PostgresException>(() =>
            _repository.CreateWithDefaultAccountsAsync(Ana(), accounts, CancellationToken.None));

        (await Count("SELECT count(*) FROM usuarios")).ShouldBe(0);
        (await Count("SELECT count(*) FROM contas")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "001-12")]
    public async Task Create_ConcurrentRegistrationsOfTheSameNumber_OnlyOneWins()
    {
        var attempts = Enumerable.Range(0, 8)
            .Select(i => Task.Run(() => _repository.CreateWithDefaultAccountsAsync(
                Ana(apiKey: $"chave-{i}") with { Nome = $"Pessoa {i}" }, DefaultAccounts.For(10, 5), CancellationToken.None)))
            .ToList();

        var results = await Task.WhenAll(attempts);

        results.Count(r => !r.WhatsappTaken).ShouldBe(1);
        results.Count(r => r.WhatsappTaken).ShouldBe(7);
        (await Count("SELECT count(*) FROM usuarios")).ShouldBe(1);
        (await Count("SELECT count(*) FROM contas")).ShouldBe(3);
    }

    [Fact]
    [Trait("AC", "001-13")]
    public async Task Create_AcceptsAFernetSizedApiKey_AfterMigration0001()
    {
        // 140 caracteres: o tamanho de um token Fernet de uma chave de 43. Não cabia em varchar(100).
        var fernetSized = "gAAAAA" + new string('x', 134);

        var result = await _repository.CreateWithDefaultAccountsAsync(Ana(apiKey: fernetSized), DefaultAccounts.For(10, 5), CancellationToken.None);

        result.WhatsappTaken.ShouldBeFalse();
    }

    [Fact]
    public async Task Create_SetsUpdatedAtTriggerAndDefaults()
    {
        var result = await _repository.CreateWithDefaultAccountsAsync(Ana(), DefaultAccounts.For(10, 5), CancellationToken.None);

        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleAsync<(bool Ativo, string Fuso, int Meses, DateTime? Created)>(
            "SELECT ativo, fuso_horario, meses_reserva_emergencia, created_at FROM usuarios WHERE id = @id",
            new { id = result.UserId });

        row.Ativo.ShouldBeTrue();
        row.Fuso.ShouldBe("America/Sao_Paulo");
        row.Meses.ShouldBe(6);
        row.Created.ShouldNotBeNull();
    }
}
