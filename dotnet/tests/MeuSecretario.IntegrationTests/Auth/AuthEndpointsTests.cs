using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MeuSecretario.IntegrationTests.Auth;

/// <summary>
/// Spec 001 ponta a ponta: HTTP real (WebApplicationFactory) → casos de uso → PostgreSQL 15 em container.
/// Os corpos são comparados como texto exato: nomes de campo, ordem e acentuação são o contrato com o Angular.
/// </summary>
[Trait("Category", "Docker")]
public class AuthEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Whatsapp = "5511999999999";
    private const string Password = "SenhaSegura123";

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client() => postgres.Api.CreateClient();

    private static async Task<(HttpStatusCode Status, string Body)> Post(HttpClient client, string path, string? json, string contentType = "application/json")
    {
        using var content = json is null ? null : new StringContent(json, Encoding.UTF8, contentType);
        var response = await client.PostAsync(new Uri(path, UriKind.Relative), content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string RegisterJson(
        string nome = "Ana Souza", string whatsapp = Whatsapp, string password = Password, string venc = "10", string fech = "5") =>
        $$"""{"nome":"{{nome}}","whatsapp":"{{whatsapp}}","password":"{{password}}","dia_vencimento":{{venc}},"dia_fechamento":{{fech}}}""";

    private static string Error(string message) => $$"""{"status":"error","message":"{{message}}"}""";

    private async Task<int> Count(string sql)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    // ============================== POST /auth/register ==============================

    [Fact]
    [Trait("AC", "001-01")]
    public async Task Register_ValidBody_Returns201WithTheExactFlaskBody_AndPersistsEverything()
    {
        var (status, body) = await Post(Client(), "/auth/register", RegisterJson(whatsapp: "+55 (11) 99999-9999"));

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldBe("""{"status":"success","message":"Usuário cadastrado com sucesso","user_id":1}""");

        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        var user = await connection.QuerySingleAsync<(string Nome, string Whatsapp, string Hash, string ApiKey)>(
            "SELECT nome, numero_whatsapp, senha_hash, api_key_automate FROM usuarios WHERE id = 1");

        user.Nome.ShouldBe("Ana Souza");
        user.Whatsapp.ShouldBe(Whatsapp);                       // AC-001-02: só dígitos
        user.Hash.ShouldStartWith("pbkdf2:sha256:1000$");       // AC-001-13
        new WerkzeugPasswordHasher(1000).Verify(user.Hash, Password).ShouldBeTrue();

        // A chave é um token Fernet que cabe na coluna (desvio 017) e decifra para 43 caracteres url-safe.
        user.ApiKey.ShouldStartWith("gAAAAA");
        user.ApiKey.Length.ShouldBe(140);
        new FernetEncryptionService(AuthFixtures.FernetKey, TimeProvider.System).Decrypt(user.ApiKey).Length.ShouldBe(43);

        (await Count("SELECT count(*) FROM contas WHERE usuario_id = 1")).ShouldBe(3);
        (await Count("SELECT count(*) FROM contas WHERE usuario_id = 1 AND tipo_conta = 'Cartão de Crédito' AND dia_vencimento = 10 AND dia_fechamento = 5")).ShouldBe(1);
    }

    [Theory]
    [Trait("AC", "001-03")]
    [InlineData("""{"whatsapp":"5511999999999","password":"SenhaSegura123","dia_vencimento":10,"dia_fechamento":5}""")]            // sem nome
    [InlineData("""{"nome":"   ","whatsapp":"5511999999999","password":"SenhaSegura123","dia_vencimento":10,"dia_fechamento":5}""")] // nome em branco
    [InlineData("""{"nome":"Ana","password":"SenhaSegura123","dia_vencimento":10,"dia_fechamento":5}""")]                              // sem whatsapp
    [InlineData("""{"nome":"Ana","whatsapp":"5511999999999","dia_vencimento":10,"dia_fechamento":5}""")]                               // sem senha
    [InlineData("""{"nome":123,"whatsapp":"5511999999999","password":"SenhaSegura123","dia_vencimento":10,"dia_fechamento":5}""")]    // nome que não é texto
    public async Task Register_MissingRequiredText_Returns400(string json)
    {
        var (status, body) = await Post(Client(), "/auth/register", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Campos obrigatórios: nome, whatsapp, password"));
        (await Count("SELECT count(*) FROM usuarios")).ShouldBe(0);
    }

    [Theory]
    [Trait("AC", "001-10")]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("\"texto\"")]
    public async Task Register_InvalidOrEmptyBody_Returns400NotAnInternalError(string json)
    {
        // Desvio 024: o Flask devolvia 500.
        var (status, body) = await Post(Client(), "/auth/register", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Campos obrigatórios: nome, whatsapp, password"));
    }

    [Fact]
    [Trait("AC", "001-10")]
    public async Task Register_NoBodyAndNoContentType_Returns400()
    {
        var (status, body) = await Post(Client(), "/auth/register", json: null);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Campos obrigatórios: nome, whatsapp, password"));
    }

    [Fact]
    [Trait("AC", "001-10")]
    public async Task Register_JsonSentWithAnotherContentType_StillWorks()
    {
        var (status, _) = await Post(Client(), "/auth/register", RegisterJson(), contentType: "text/plain");

        status.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    [Trait("AC", "001-04")]
    public async Task Register_ShortPassword_Returns400()
    {
        var (status, body) = await Post(Client(), "/auth/register", RegisterJson(password: "12345"));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Senha deve ter no mínimo 6 caracteres"));
    }

    [Theory]
    [Trait("AC", "001-05")]
    [InlineData("null", "5")]
    [InlineData("10", "null")]
    public async Task Register_MissingDays_Returns400(string venc, string fech)
    {
        var (status, body) = await Post(Client(), "/auth/register", RegisterJson(venc: venc, fech: fech));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Campos obrigatórios: dia_vencimento, dia_fechamento"));
    }

    [Theory]
    [Trait("AC", "001-06")]
    [InlineData("0", "5", "dia_vencimento")]
    [InlineData("32", "5", "dia_vencimento")]
    [InlineData("10", "0", "dia_fechamento")]
    [InlineData("10", "32", "dia_fechamento")]
    [InlineData("0", "40", "dia_vencimento")]
    public async Task Register_DayOutOfRange_Returns400WithTheFirstInvalidField(string venc, string fech, string field)
    {
        var (status, body) = await Post(Client(), "/auth/register", RegisterJson(venc: venc, fech: fech));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error($"Dias inválidos: {field} deve estar entre 1 e 31"));
    }

    [Theory]
    [Trait("AC", "001-07")]
    [InlineData("\"abc\"", "5")]
    [InlineData("true", "5")]
    [InlineData("10", "[1]")]
    [InlineData("10", "{}")]
    public async Task Register_NonIntegerDays_Returns400(string venc, string fech)
    {
        var (status, body) = await Post(Client(), "/auth/register", RegisterJson(venc: venc, fech: fech));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Dias inválidos: dia_vencimento e dia_fechamento devem ser números inteiros"));
    }

    [Theory]
    [Trait("AC", "001-07")]
    [InlineData("\"10\"", "\"5\"")]
    [InlineData("10.9", "5.1")]
    [InlineData("\" 10 \"", "5")]
    public async Task Register_NumericStringsAndDecimals_AreAcceptedAndTruncated(string venc, string fech)
    {
        var (status, _) = await Post(Client(), "/auth/register", RegisterJson(venc: venc, fech: fech));

        status.ShouldBe(HttpStatusCode.Created);
        (await Count("SELECT count(*) FROM contas WHERE dia_vencimento = 10 AND dia_fechamento = 5")).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "001-08")]
    public async Task Register_ExistingNumber_Returns409_EvenWithDifferentFormatting()
    {
        var client = Client();
        await Post(client, "/auth/register", RegisterJson());

        var (status, body) = await Post(client, "/auth/register", RegisterJson(nome: "Outra", whatsapp: "+55 11 99999-9999"));

        status.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldBe(Error("WhatsApp já cadastrado"));
        (await Count("SELECT count(*) FROM usuarios")).ShouldBe(1);
        (await Count("SELECT count(*) FROM contas")).ShouldBe(3);
    }

    [Fact]
    [Trait("AC", "001-09")]
    public async Task Register_WhatsappWithoutDigits_Returns400_AndStoresNothing()
    {
        var (status, body) = await Post(Client(), "/auth/register", RegisterJson(whatsapp: "abc"));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Campos obrigatórios: nome, whatsapp, password"));
        (await Count("SELECT count(*) FROM usuarios")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "001-12")]
    public async Task Register_SimultaneousRequestsForTheSameNumber_OneCreatedTheRestConflict()
    {
        var client = Client();

        var results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(i => Post(client, "/auth/register", RegisterJson(nome: $"Pessoa {i}"))));

        results.Count(r => r.Status == HttpStatusCode.Created).ShouldBe(1);
        results.Count(r => r.Status == HttpStatusCode.Conflict).ShouldBe(5);
        results.ShouldNotContain(r => r.Status == HttpStatusCode.InternalServerError);
        (await Count("SELECT count(*) FROM usuarios")).ShouldBe(1);
        (await Count("SELECT count(*) FROM contas")).ShouldBe(3);
    }

    // ============================== POST /auth/login ==============================

    private static async Task RegisterAna(HttpClient client) =>
        (await Post(client, "/auth/register", RegisterJson())).Status.ShouldBe(HttpStatusCode.Created);

    private static string LoginJson(string whatsapp = Whatsapp, string password = Password) =>
        $$"""{"whatsapp":"{{whatsapp}}","password":"{{password}}"}""";

    [Fact]
    [Trait("AC", "001-20")]
    public async Task Login_AfterRegister_ReturnsTheFlaskShapeWithAValidToken()
    {
        var client = Client();
        await RegisterAna(client);

        var (status, body) = await Post(client, "/auth/login", LoginJson());

        status.ShouldBe(HttpStatusCode.OK);
        var root = JsonDocument.Parse(body).RootElement;
        root.EnumerateObject().Select(p => p.Name).ShouldBe(["status", "token", "user"]); // campos na raiz, nesta ordem
        root.GetProperty("status").GetString().ShouldBe("success");
        root.GetProperty("user").GetRawText().ShouldBe("""{"id":1,"nome":"Ana Souza","whatsapp":"5511999999999"}""");

        // O token é aceito pelo mesmo serviço que o filtro RequireUser usa.
        var validation = new JwtTokenService(AuthFixtures.JwtSecret, TimeProvider.System).Validate(root.GetProperty("token").GetString()!);
        validation.IsValid.ShouldBeTrue();
        validation.UserId.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "001-21")]
    public async Task Login_WithDifferentlyFormattedNumber_Works()
    {
        var client = Client();
        await RegisterAna(client);

        var (status, _) = await Post(client, "/auth/login", LoginJson(whatsapp: "+55 (11) 99999-9999"));

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("AC", "001-26")]
    public async Task Login_UserWithTheRealProductionHashFormat_Scrypt32768_Works()
    {
        // O usuário real de produção tem senha_hash scrypt:32768:8:1 (gerado pelo Werkzeug).
        var scrypt = AuthFixtures.Passwords.First(p => p.Kind == "scrypt-default");
        await using (var connection = await postgres.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                "INSERT INTO usuarios (nome, numero_whatsapp, senha_hash) VALUES ('Real', '5511888888888', @hash)", new { hash = scrypt.Hash });
        }

        var (status, body) = await Post(Client(), "/auth/login", LoginJson("5511888888888", scrypt.Password));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"token\"");
    }

    [Fact]
    [Trait("AC", "001-27")]
    public async Task Login_UserWithAPbkdf2HashFromWerkzeug_Works()
    {
        var pbkdf2 = AuthFixtures.Passwords.First(p => p.Kind == "pbkdf2-1000");
        await using (var connection = await postgres.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                "INSERT INTO usuarios (nome, numero_whatsapp, senha_hash) VALUES ('Legado', '5511777777777', @hash)", new { hash = pbkdf2.Hash });
        }

        (await Post(Client(), "/auth/login", LoginJson("5511777777777", pbkdf2.Password))).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [Trait("AC", "001-22")]
    [InlineData("""{"password":"x"}""")]
    [InlineData("""{"whatsapp":"5511999999999"}""")]
    [InlineData("""{"whatsapp":"  ","password":"x"}""")]
    [InlineData("não é json")]
    [InlineData("")]
    public async Task Login_MissingFieldsOrInvalidBody_Returns400(string json)
    {
        var (status, body) = await Post(Client(), "/auth/login", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Campos obrigatórios: whatsapp, password"));
    }

    [Fact]
    [Trait("AC", "001-23")]
    public async Task Login_UnknownNumber_Returns404()
    {
        var (status, body) = await Post(Client(), "/auth/login", LoginJson(whatsapp: "5500000000000"));

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(Error("WhatsApp não cadastrado"));
    }

    [Fact]
    [Trait("AC", "001-24")]
    public async Task Login_UserWithoutPasswordHash_Returns401()
    {
        await using (var connection = await postgres.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("INSERT INTO usuarios (nome, numero_whatsapp) VALUES ('Sem senha', '5511666666666')");
        }

        var (status, body) = await Post(Client(), "/auth/login", LoginJson("5511666666666"));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Error("Usuário sem senha cadastrada. Cadastre-se pelo site."));
    }

    [Fact]
    [Trait("AC", "001-25")]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = Client();
        await RegisterAna(client);

        var (status, body) = await Post(client, "/auth/login", LoginJson(password: "errada-errada"));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Error("Senha incorreta"));
    }

    [Fact]
    [Trait("AC", "001-25")]
    public async Task Login_UnrecognizedHashFormat_Returns401WrongPassword()
    {
        await using (var connection = await postgres.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("INSERT INTO usuarios (nome, numero_whatsapp, senha_hash) VALUES ('Antigo', '5511555555555', 'senha-em-texto-puro')");
        }

        var (status, body) = await Post(Client(), "/auth/login", LoginJson("5511555555555", "senha-em-texto-puro"));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Error("Senha incorreta"));
    }

    // ============================== POST /auth/verify ==============================

    [Fact]
    [Trait("AC", "001-30")]
    public async Task Verify_TokenFromLogin_Returns200WithTheExactBody()
    {
        var client = Client();
        await RegisterAna(client);
        var login = JsonDocument.Parse((await Post(client, "/auth/login", LoginJson())).Body).RootElement;

        var (status, body) = await Post(client, "/auth/verify", $$"""{"token":"{{login.GetProperty("token").GetString()}}"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","valid":true,"user_id":1}""");
    }

    [Fact]
    [Trait("AC", "001-34")]
    public async Task Verify_TokenIssuedByThePyJwtOfTheFlask_Returns200()
    {
        var (status, body) = await Post(Client(), "/auth/verify", $$"""{"token":"{{AuthFixtures.JwtToken("valid")}}"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","valid":true,"user_id":42}""");
    }

    [Theory]
    [Trait("AC", "001-31")]
    [InlineData("""{"token":""}""")]
    [InlineData("""{"token":"   "}""")]
    [InlineData("""{}""")]
    [InlineData("""{"token":123}""")]
    [InlineData("não é json")]
    [InlineData("")]
    public async Task Verify_MissingTokenOrInvalidBody_Returns400(string json)
    {
        var (status, body) = await Post(Client(), "/auth/verify", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Token não fornecido"));
    }

    [Theory]
    [Trait("AC", "001-32")]
    [InlineData("expired")]
    [InlineData("alg_none")]
    [InlineData("hs512")]
    [InlineData("wrong_secret")]
    [InlineData("tampered_payload")]
    [InlineData("no_exp")]
    public async Task Verify_BadTokens_Return401(string fixtureName)
    {
        var (status, body) = await Post(Client(), "/auth/verify", $$"""{"token":"{{AuthFixtures.JwtToken(fixtureName)}}"}""");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Error("Token inválido ou expirado"));
    }

    [Fact]
    [Trait("AC", "001-32")]
    public async Task Verify_TokenIssuedByTheDotnetButAlreadyExpired_Returns401()
    {
        var past = new JwtTokenService(AuthFixtures.JwtSecret, new FakeTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var (status, body) = await Post(Client(), "/auth/verify", $$"""{"token":"{{past.Issue(7)}}"}""");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Error("Token inválido ou expirado"));
    }

    [Fact]
    [Trait("AC", "001-33")]
    public async Task Verify_ValidTokenWithoutUserId_Returns500LikeTheFlask()
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["foo"] = "bar" },
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthFixtures.JwtSecret)), SecurityAlgorithms.HmacSha256),
        });

        var (status, body) = await Post(Client(), "/auth/verify", $$"""{"token":"{{token}}"}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(Error("Erro ao verificar token"));
    }

    // ============================== Roteamento ==============================

    [Theory]
    [InlineData("/auth/register/")]
    [InlineData("/auth/login/")]
    [InlineData("/auth/verify/")]
    public async Task TrailingSlash_Returns404LikeFlask(string path)
    {
        var (status, _) = await Post(Client(), path, "{}");

        status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/auth/register")]
    [InlineData("/auth/login")]
    [InlineData("/auth/verify")]
    public async Task Get_IsNotAllowed(string path)
    {
        var response = await Client().GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Responses_AreJsonWithUtf8()
    {
        var response = await Client().PostAsync(
            new Uri("/auth/login", UriKind.Relative), new StringContent("{}", Encoding.UTF8, new MediaTypeHeaderValue("application/json")));

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        response.Content.Headers.ContentType?.CharSet.ShouldBe("utf-8");
    }
}
