using System.Net;
using MeuSecretario.Application.Categories;
using MeuSecretario.Domain.Categories;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.Categories;

/// <summary>
/// Spec 003 §3 sem banco: HTTP real (WebApplicationFactory) com um repositório falso no lugar do PostgreSQL.
/// Não precisa de Docker.
/// </summary>
public sealed class CategoriesEndpointTests : IDisposable
{
    private sealed class FakeRepository : ICategoryRepository
    {
        public List<(int UserId, CategoryTypeFilter Filter)> Calls { get; } = [];

        public Func<IReadOnlyList<CategoryRow>> Rows { get; set; } = () => [];

        public Task<IReadOnlyList<CategoryRow>> ListVisibleAsync(int userId, CategoryTypeFilter filter, CancellationToken cancellationToken)
        {
            Calls.Add((userId, filter));
            return Task.FromResult(Rows());
        }
    }

    private readonly FakeRepository _fake = new();
    private readonly WebApplicationFactory<Program> _factory;

    public CategoriesEndpointTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DATABASE_URL", "postgresql://u:p@localhost:5432/inexistente");
            builder.UseSetting("API_SECRET_KEY", AuthFixtures.JwtSecret);
            builder.UseSetting("ENCRYPTION_KEY", AuthFixtures.FernetKey);
            builder.UseSetting("GEMINI_API_KEY", "gemini-de-teste");
            builder.UseSetting("REDIS_URL", "redis://localhost:6379");
            builder.UseSetting("LOG_TO_FILE", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICategoryRepository>();
                services.AddSingleton<ICategoryRepository>(_fake);
            });
        });
    }

    public void Dispose() => _factory.Dispose();

    private static string ValidToken(int userId) =>
        new JwtTokenService(AuthFixtures.JwtSecret, TimeProvider.System).Issue(userId);

    private async Task<(HttpStatusCode Status, string Body)> Get(string path, string? authorization)
    {
        using var client = _factory.CreateClient();
        if (authorization is not null)
        {
            client.DefaultRequestHeaders.Authorization = null;
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
        }
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private Task<(HttpStatusCode Status, string Body)> GetAs(string path, int userId = 7) =>
        Get(path, "Bearer " + ValidToken(userId));

    private static string Error(string message) => $$"""{"status":"error","message":"{{message}}"}""";

    [Theory]
    [Trait("AC", "003-02")]
    [InlineData("/api/categories")]
    [InlineData("/api/categorias")]
    public async Task BothRoutes_ReturnTheSameBody_WithTheFieldsInTheContractOrder(string path)
    {
        _fake.Rows = () =>
        [
            new CategoryRow("Despesa Discricionária", 13, "Lazer e Entretenimento", 55, "Outros"),
            new CategoryRow("Despesa Discricionária", 13, "Lazer e Entretenimento", 33, "Passeios e Eventos Culturais"),
        ];

        var (status, body) = await GetAs(path);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"success","data":[{"grupo":"Despesa Discricionária","macro_id":13,"macro_categoria":"Lazer e Entretenimento","subcategorias":[{"id":55,"nome":"Outros"},{"id":33,"nome":"Passeios e Eventos Culturais"}]}]}""");
    }

    [Fact]
    [Trait("AC", "003-09")]
    public async Task NoCategories_Returns200WithAnEmptyDataArray()
    {
        var (status, body) = await GetAs("/api/categories");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","data":[]}""");
    }

    [Fact]
    [Trait("AC", "003-01")]
    public async Task TheUserIdComesFromTheToken()
    {
        await GetAs("/api/categories", userId: 42);

        _fake.Calls.ShouldBe([(42, CategoryTypeFilter.None)]);
    }

    [Theory]
    [Trait("AC", "003-05")]
    [InlineData("?tipo=Receita", CategoryTypeFilter.Receita)]
    [InlineData("?tipo=Despesa", CategoryTypeFilter.Despesa)]
    [InlineData("?tipo=receita", CategoryTypeFilter.None)]
    [InlineData("?tipo=RECEITA", CategoryTypeFilter.None)]
    [InlineData("?tipo=Renda", CategoryTypeFilter.None)]
    [InlineData("?tipo=", CategoryTypeFilter.None)]
    [InlineData("?tipo=%20Receita", CategoryTypeFilter.None)]
    [InlineData("?tipo=xyz", CategoryTypeFilter.None)]
    [InlineData("?tipo=Despesa&tipo=Receita", CategoryTypeFilter.Despesa)]
    [InlineData("?tipo=Receita&tipo=Despesa", CategoryTypeFilter.Receita)]
    [InlineData("?outro=Receita", CategoryTypeFilter.None)]
    public async Task OnlyTheExactValues_Filter_AndTheFirstOneWins(string query, CategoryTypeFilter expected)
    {
        var (status, _) = await GetAs("/api/categories" + query);

        status.ShouldBe(HttpStatusCode.OK);
        _fake.Calls.Single().Filter.ShouldBe(expected);
    }

    [Theory]
    [Trait("AC", "003-31")]
    [InlineData("?tipo=%27%20OR%201%3D1%20--")]
    [InlineData("?tipo=%E2%82%AC%F0%9F%98%80%C3%A7")]
    [InlineData("?tipo=%00%01%1F")]
    [InlineData("?tipo=%FF%FE")]
    public async Task HostileTipo_NeverCauses500(string query)
    {
        var (status, body) = await GetAs("/api/categories" + query);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","data":[]}""");
        _fake.Calls.Single().Filter.ShouldBe(CategoryTypeFilter.None);
    }

    [Fact]
    [Trait("AC", "003-31")]
    public async Task TenThousandCharTipo_NeverCauses500()
    {
        var (status, _) = await GetAs("/api/categories?tipo=" + new string('a', 10_000));

        status.ShouldBe(HttpStatusCode.OK);
        _fake.Calls.Single().Filter.ShouldBe(CategoryTypeFilter.None);
    }

    [Fact]
    [Trait("AC", "003-30")]
    public async Task RepositoryFailure_Returns500WithAFixedMessage_NeverTheExceptionText()
    {
        _fake.Rows = () => throw new InvalidOperationException("SEGREDO senha=hunter2 5511999999999");

        var (status, body) = await GetAs("/api/categories");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(Error("Erro ao carregar categorias"));
    }

    [Theory]
    [Trait("AC", "003-20")]
    [InlineData("/api/categories")]
    [InlineData("/api/categorias")]
    public async Task WithoutHeader_Returns401(string path)
    {
        var (status, body) = await Get(path, authorization: null);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Error("Token de autenticação não fornecido"));
        _fake.Calls.ShouldBeEmpty();
    }

    [Theory]
    [Trait("AC", "003-20")]
    [InlineData("/api/categories")]
    [InlineData("/api/categorias")]
    public async Task MalformedHeader_Returns401(string path)
    {
        var (status, body) = await Get(path, "Token abc");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Error("Formato de autenticação inválido. Use: Bearer <token>"));
    }

    [Theory]
    [Trait("AC", "003-20")]
    [InlineData("expired")]
    [InlineData("tampered_payload")]
    [InlineData("wrong_secret")]
    public async Task BadToken_Returns401(string fixtureName)
    {
        var (status, body) = await Get("/api/categories", "Bearer " + AuthFixtures.JwtToken(fixtureName));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Error("Token inválido ou expirado"));
    }

    [Theory]
    [Trait("AC", "003-20")]
    [InlineData("no_user_id")]
    [InlineData("user_id_zero")]
    public async Task TokenWithoutUserId_Returns401(string fixtureName)
    {
        var (status, body) = await Get("/api/categorias", "Bearer " + AuthFixtures.JwtToken(fixtureName));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Error("Token não contém user_id"));
    }

    [Theory]
    [Trait("AC", "003-21")]
    [InlineData("/api/categories/")]
    [InlineData("/api/categorias/")]
    public async Task TrailingSlash_Returns404WithTheFlaskHtml(string path)
    {
        var (status, body) = await GetAs(path);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldStartWith("<!doctype html>\n<html lang=en>\n<title>404 Not Found</title>");
        _fake.Calls.ShouldBeEmpty();
    }
}
