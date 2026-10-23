using Dapper;
using MeuSecretario.Infrastructure.Categories;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Categories;

/// <summary>Buscas por nome (G-3, desvio 030): sempre <c>ORDER BY id</c>, nunca devolvem categoria de outro usuário.</summary>
[Trait("Category", "Docker")]
public class CategoryLookupTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly CategoryRepository _lookup = new(postgres.Connections);

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        await Sql("INSERT INTO usuarios (nome, numero_whatsapp) VALUES ('Ana', '5511111111111'), ('Bia', '5522222222222')");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task Sql(string sql)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql);
    }

    [Fact]
    [Trait("AC", "003-50")]
    public async Task SeededDatabase_FindsTheThreeNames()
    {
        (await _lookup.FindShortTermInvestmentAsync(1, CancellationToken.None)).ShouldBe(28);
        (await _lookup.FindLoanPrincipalAsync(1, CancellationToken.None)).ShouldBe(29);
        (await _lookup.FindFallbackAsync(income: false, CancellationToken.None)).ShouldBe(59); // Despesas Gerais
        (await _lookup.FindFallbackAsync(income: true, CancellationToken.None)).ShouldBe(60);  // Receitas Gerais
    }

    [Fact]
    [Trait("AC", "003-51")]
    public async Task DuplicatedOutrosInDespesasGerais_ReturnsTheLowestId()
    {
        await Sql("INSERT INTO subcategoria (macro_id, nome_sub) VALUES (17, 'Outros')");

        (await _lookup.FindFallbackAsync(income: false, CancellationToken.None)).ShouldBe(59);
    }

    [Fact]
    [Trait("AC", "003-51")]
    public async Task DuplicatedShortTermInvestment_ReturnsTheLowestId()
    {
        await Sql("INSERT INTO subcategoria (macro_id, nome_sub) VALUES (10, 'Investimentos de Curto Prazo')");

        (await _lookup.FindShortTermInvestmentAsync(1, CancellationToken.None)).ShouldBe(28);
    }

    [Fact]
    [Trait("AC", "003-52")]
    public async Task MissingNames_ReturnNull()
    {
        await Sql("DELETE FROM subcategoria WHERE nome_sub IN ('Investimentos de Curto Prazo', 'Quitação de Empréstimos (Principal)')");
        await Sql("DELETE FROM subcategoria WHERE nome_sub = 'Outros' AND macro_id IN (17, 18)");

        (await _lookup.FindShortTermInvestmentAsync(1, CancellationToken.None)).ShouldBeNull();
        (await _lookup.FindLoanPrincipalAsync(1, CancellationToken.None)).ShouldBeNull();
        (await _lookup.FindFallbackAsync(income: false, CancellationToken.None)).ShouldBeNull();
        (await _lookup.FindFallbackAsync(income: true, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "003-53")]
    public async Task SubcategoryOfAnotherUser_IsNeverReturned()
    {
        await Sql("DELETE FROM subcategoria WHERE nome_sub = 'Investimentos de Curto Prazo'");
        await Sql("INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) VALUES (10, 2, 'Investimentos de Curto Prazo')");

        (await _lookup.FindShortTermInvestmentAsync(1, CancellationToken.None)).ShouldBeNull();
        (await _lookup.FindShortTermInvestmentAsync(2, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    [Trait("AC", "003-53")]
    public async Task UserOutros_IsNotAFallback()
    {
        await Sql("DELETE FROM subcategoria WHERE nome_sub = 'Outros' AND macro_id = 17");
        await Sql("INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) VALUES (17, 2, 'Outros')");

        (await _lookup.FindFallbackAsync(income: false, CancellationToken.None)).ShouldBeNull();
    }
}
