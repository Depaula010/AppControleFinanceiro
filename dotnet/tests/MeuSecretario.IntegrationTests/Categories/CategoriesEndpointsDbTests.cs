using System.Net;
using System.Text.Json;
using Dapper;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;

namespace MeuSecretario.IntegrationTests.Categories;

/// <summary>Spec 003 ponta a ponta: HTTP real, PostgreSQL 15 em container, seed da migration 0020. Corpos comparados como texto.</summary>
[Trait("Category", "Docker")]
public class CategoriesEndpointsDbTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpStatusCode Status, string Body)> Get(string path, int userId = 1)
    {
        using var client = postgres.Api.CreateClient();
        var token = new JwtTokenService(AuthFixtures.JwtSecret, TimeProvider.System).Issue(userId);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + token);
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task Sql(string sql)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql);
    }

    private static JsonElement Data(string body) => JsonDocument.Parse(body).RootElement.GetProperty("data");

    private const string ReceitaBody =
        """{"status":"success","data":[""" +
        """{"grupo":"Geral","macro_id":18,"macro_categoria":"Receitas Gerais","subcategorias":[{"id":60,"nome":"Outros"}]},""" +
        """{"grupo":"Renda","macro_id":1,"macro_categoria":"Renda Principal","subcategorias":[{"id":43,"nome":"Outros"},{"id":2,"nome":"Remuneração Variável / Comissões"},{"id":1,"nome":"Salário Fixo / Pró-labore"}]},""" +
        """{"grupo":"Renda","macro_id":2,"macro_categoria":"Renda Extra e Passiva","subcategorias":[{"id":3,"nome":"Aluguéis Recebidos"},{"id":4,"nome":"Freelances / Projetos Avulsos"},{"id":44,"nome":"Outros"}]},""" +
        """{"grupo":"Renda","macro_id":3,"macro_categoria":"Investimentos e Reembolsos","subcategorias":[{"id":5,"nome":"Dividendos / Juros (Rendimentos)"},{"id":45,"nome":"Outros"},{"id":6,"nome":"Restituições / Reembolsos"}]},""" +
        """{"grupo":"Renda","macro_id":4,"macro_categoria":"Outras Entradas","subcategorias":[{"id":46,"nome":"Outros"},{"id":7,"nome":"Presentes em Dinheiro / Doações"},{"id":8,"nome":"Venda de Bens (Pontual)"}]}""" +
        """]}""";

    [Fact]
    [Trait("AC", "003-03")]
    public async Task Receita_ReturnsTheExactBody()
    {
        var (status, body) = await Get("/api/categories?tipo=Receita");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(ReceitaBody);
    }

    [Fact]
    [Trait("AC", "003-02")]
    public async Task Alias_ReturnsTheSameBodyAsTheMainRoute()
    {
        var main = await Get("/api/categories?tipo=Receita");
        var alias = await Get("/api/categorias?tipo=Receita");

        alias.ShouldBe(main);

        var full = await Get("/api/categories");
        (await Get("/api/categorias")).ShouldBe(full);
    }

    [Fact]
    [Trait("AC", "003-01")]
    public async Task NoFilter_Returns18MacrosAnd60Subcategories_GroupsInTheSpecOrder()
    {
        var (status, body) = await Get("/api/categories");

        status.ShouldBe(HttpStatusCode.OK);
        var data = Data(body);
        data.GetArrayLength().ShouldBe(18);
        data.EnumerateArray().Sum(m => m.GetProperty("subcategorias").GetArrayLength()).ShouldBe(60);
        data.EnumerateArray().Select(m => m.GetProperty("grupo").GetString()).Distinct().ShouldBe(
            ["Despesa Discricionária", "Despesa Essencial", "Geral", "Meta Financeira", "Renda"]);
        body.ShouldStartWith(
            """{"status":"success","data":[{"grupo":"Despesa Discricionária","macro_id":13,"macro_categoria":"Lazer e Entretenimento","subcategorias":[{"id":55,"nome":"Outros"},""");
    }

    [Fact]
    [Trait("AC", "003-04")]
    public async Task Despesa_Returns13MacrosAnd47Subcategories_WithoutRendaOrReceitasGerais()
    {
        var (_, body) = await Get("/api/categories?tipo=Despesa");

        var data = Data(body);
        data.GetArrayLength().ShouldBe(13);
        data.EnumerateArray().Sum(m => m.GetProperty("subcategorias").GetArrayLength()).ShouldBe(47);
        data.EnumerateArray().ShouldNotContain(m => m.GetProperty("grupo").GetString() == "Renda");
        data.EnumerateArray().ShouldNotContain(m => m.GetProperty("macro_categoria").GetString() == "Receitas Gerais");
    }

    [Theory]
    [Trait("AC", "003-05")]
    [InlineData("?tipo=receita")]
    [InlineData("?tipo=RECEITA")]
    [InlineData("?tipo=Renda")]
    [InlineData("?tipo=")]
    [InlineData("?tipo=%20Receita")]
    [InlineData("?tipo=xyz")]
    [InlineData("?tipo=%27%20OR%201%3D1%20--")]
    public async Task UnknownTipo_IsIgnored_AndReturnsTheFullListing(string query)
    {
        var (status, body) = await Get("/api/categories" + query);

        status.ShouldBe(HttpStatusCode.OK);
        Data(body).GetArrayLength().ShouldBe(18);
    }

    [Fact]
    [Trait("AC", "003-05")]
    public async Task RepeatedTipo_TheFirstValueWins()
    {
        var (_, body) = await Get("/api/categories?tipo=Despesa&tipo=Receita");

        Data(body).GetArrayLength().ShouldBe(13);
    }

    [Fact]
    [Trait("AC", "003-09")]
    public async Task EmptyDatabase_Returns200WithEmptyData()
    {
        await Sql("DELETE FROM subcategoria");

        var (status, body) = await Get("/api/categories");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","data":[]}""");
    }

    [Fact]
    [Trait("AC", "003-06")]
    public async Task EveryItem_HasExactlyTheContractFieldsAndTypes()
    {
        var (_, body) = await Get("/api/categories");

        JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["status", "data"]);
        foreach (var macro in Data(body).EnumerateArray())
        {
            macro.EnumerateObject().Select(p => p.Name).ShouldBe(["grupo", "macro_id", "macro_categoria", "subcategorias"]);
            macro.GetProperty("macro_id").ValueKind.ShouldBe(JsonValueKind.Number);
            foreach (var sub in macro.GetProperty("subcategorias").EnumerateArray())
            {
                sub.EnumerateObject().Select(p => p.Name).ShouldBe(["id", "nome"]);
                sub.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Number);
            }
        }
    }

    [Fact]
    [Trait("AC", "003-32")]
    public async Task DuplicatedGlobalOutros_BothAppear_LowestIdFirst()
    {
        await Sql("INSERT INTO subcategoria (macro_id, nome_sub) VALUES (17, 'Outros')");

        var (_, body) = await Get("/api/categories?tipo=Despesa");

        body.ShouldContain(
            """{"grupo":"Geral","macro_id":17,"macro_categoria":"Despesas Gerais","subcategorias":[{"id":59,"nome":"Outros"},{"id":61,"nome":"Outros"}]}""");
    }

    [Fact]
    [Trait("AC", "003-14")]
    public async Task UserCategories_AppearOnlyForTheirOwner_EndToEnd()
    {
        await Sql("INSERT INTO usuarios (nome, numero_whatsapp) VALUES ('Ana', '5511111111111'), ('Bia', '5522222222222')");
        await Sql("INSERT INTO macrocategoria (grupo_id, usuario_id, nome_macro, ordem_macro) VALUES (4, 1, 'Pets da Ana', 9)");
        await Sql("INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) VALUES (19, 1, 'Petisco do Rex'), (13, 2, 'Cinema da Bia')");

        var ana = (await Get("/api/categories", userId: 1)).Body;
        var bia = (await Get("/api/categories", userId: 2)).Body;
        var anaDespesa = (await Get("/api/categories?tipo=Despesa", userId: 1)).Body;

        ana.ShouldContain("""{"grupo":"Despesa Discricionária","macro_id":19,"macro_categoria":"Pets da Ana","subcategorias":[{"id":61,"nome":"Petisco do Rex"}]}""");
        anaDespesa.ShouldContain("Pets da Ana");
        ana.ShouldNotContain("Cinema da Bia");
        bia.ShouldNotContain("Pets da Ana");
        bia.ShouldNotContain("Petisco do Rex");
        bia.ShouldContain("Cinema da Bia");
    }

    [Fact]
    [Trait("AC", "003-15")]
    public async Task TokenOfAnUnknownUser_GetsOnlyTheGlobals()
    {
        var (status, body) = await Get("/api/categories", userId: 999);

        status.ShouldBe(HttpStatusCode.OK);
        Data(body).GetArrayLength().ShouldBe(18);
    }
}
