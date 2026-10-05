using System.Net;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Settings;

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class ProfileEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Path = "/api/user/profile";
    private readonly SettingsEnv _env = new(postgres);
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _env.InsertUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private const string DefaultProfile =
        """{"id":1,"nome":"Ana Souza","email":null,"numero_whatsapp":"5511900000001","cidade":"Sao Paulo","estado":"SP","fuso_horario":"America/Sao_Paulo","meses_reserva_emergencia":6}""";

    [Fact]
    [Trait("AC", "007-02")]
    public async Task Get_ReturnsTheEightFieldsInOrder()
    {
        var (status, body) = await _env.Get(_user, Path);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(SettingsEnv.Data(DefaultProfile));
    }

    [Fact]
    [Trait("AC", "007-03")]
    public async Task InactiveOrMissingUser_Gets404_OnGetPutAndSettings()
    {
        var inactive = await _env.InsertUserAsync("5511900000002", "Inativo", ativo: false);

        foreach (var user in new[] { inactive, 999 })
        {
            (await _env.Get(user, Path)).ShouldBe((HttpStatusCode.NotFound, SettingsEnv.Error("Usuário não encontrado")));
            (await _env.Put(user, Path, """{"nome":"Novo Nome"}""")).ShouldBe((HttpStatusCode.NotFound, SettingsEnv.Error("Usuário não encontrado")));
            (await _env.Get(user, "/api/user/settings")).ShouldBe((HttpStatusCode.NotFound, SettingsEnv.Error("Usuário não encontrado")));
        }
    }

    [Theory]
    [Trait("AC", "007-10")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("")]
    [InlineData("nao e json")]
    [InlineData("""{"id":5,"senha":"x","numero_whatsapp":"1"}""")]
    public async Task Put_WithNothingToUpdate_Returns400(string json)
    {
        var (status, body) = await _env.Put(_user, Path, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Nenhum campo válido para atualizar"));
    }

    [Theory]
    [Trait("AC", "007-11")]
    [InlineData("""{"nome":"A"}""")]
    [InlineData("""{"nome":""}""")]
    [InlineData("""{"nome":null}""")]
    [InlineData("""{"nome":"  A "}""")]
    public async Task Put_ShortName_Returns400(string json)
    {
        var (status, body) = await _env.Put(_user, Path, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Nome deve ter pelo menos 2 caracteres"));
        (await _env.ScalarAsync<string>("SELECT nome FROM usuarios WHERE id = @_user", new { _user })).ShouldBe("Ana Souza");
    }

    [Fact]
    [Trait("AC", "007-11")]
    public async Task Put_NameIsStoredTrimmed_Desvio085()
    {
        var (status, body) = await _env.Put(_user, Path, """{"nome":" Ana "}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"nome\":\"Ana\"");
        (await _env.ScalarAsync<string>("SELECT nome FROM usuarios WHERE id = @_user", new { _user })).ShouldBe("Ana");
    }

    [Fact]
    [Trait("AC", "007-12")]
    public async Task Put_State_Rules()
    {
        (await _env.Put(_user, Path, """{"estado":"SPX"}""")).ShouldBe((HttpStatusCode.BadRequest, SettingsEnv.Error("Estado deve ter 2 caracteres (ex: SP)")));
        (await _env.Put(_user, Path, """{"estado":"S"}""")).ShouldBe((HttpStatusCode.BadRequest, SettingsEnv.Error("Estado deve ter 2 caracteres (ex: SP)")));

        var (status, body) = await _env.Put(_user, Path, """{"estado":""}""");
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"estado\":null");

        (await _env.Put(_user, Path, """{"estado":"sp"}""")).Body.ShouldContain("\"estado\":\"sp\"");
    }

    [Theory]
    [Trait("AC", "007-13")]
    [InlineData("\"x\"", "Meses de reserva deve ser um número inteiro")]
    [InlineData("\"5.5\"", "Meses de reserva deve ser um número inteiro")]
    [InlineData("[]", "Meses de reserva deve ser um número inteiro")]
    [InlineData("0", "Meses de reserva deve ser entre 1 e 36")]
    [InlineData("37", "Meses de reserva deve ser entre 1 e 36")]
    [InlineData("\"40\"", "Meses de reserva deve ser entre 1 e 36")]
    public async Task Put_InvalidMonths_Returns400(string months, string message)
    {
        var (status, body) = await _env.Put(_user, Path, $$"""{"meses_reserva_emergencia":{{months}}}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error(message));
    }

    [Theory]
    [Trait("AC", "007-13")]
    [InlineData("\"12\"", "12")]
    [InlineData("\" 12 \"", "12")]
    [InlineData("12.9", "12")]
    [InlineData("true", "1")]
    [InlineData("null", "null")]
    public async Task Put_ValidMonths_AreConvertedLikePythonInt(string months, string expected)
    {
        var (status, body) = await _env.Put(_user, Path, $$"""{"meses_reserva_emergencia":{{months}}}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain($"\"meses_reserva_emergencia\":{expected}}}");
    }

    [Fact]
    [Trait("AC", "007-14")]
    public async Task Put_ValidationOrder()
    {
        (await _env.Put(_user, Path, """{"nome":"A","meses_reserva_emergencia":"x"}""")).Body
            .ShouldBe(SettingsEnv.Error("Meses de reserva deve ser um número inteiro"));
        (await _env.Put(_user, Path, """{"nome":"A","estado":"S"}""")).Body
            .ShouldBe(SettingsEnv.Error("Nome deve ter pelo menos 2 caracteres"));
    }

    [Fact]
    [Trait("AC", "007-15")]
    public async Task Put_EmptyStringsBecomeNull_ButABlankIsKept()
    {
        await _env.ExecuteAsync("UPDATE usuarios SET email = 'a@b.com' WHERE id = @_user", new { _user });

        var (_, body) = await _env.Put(_user, Path, """{"email":"","cidade":" "}""");

        body.ShouldContain("\"email\":null");
        body.ShouldContain("\"cidade\":\" \"");
    }

    [Fact]
    [Trait("AC", "007-16")]
    public async Task Put_FullAngularBody_KeepsIdAndWhatsapp()
    {
        var (status, body) = await _env.Put(_user, Path,
            """{"id":77,"nome":"Ana Maria","email":"ana@x.com","numero_whatsapp":"000","cidade":"Rio","estado":"RJ","fuso_horario":"America/Sao_Paulo","meses_reserva_emergencia":10,"senha":"x"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(SettingsEnv.DataMsg(
            "Perfil atualizado com sucesso",
            """{"id":1,"nome":"Ana Maria","email":"ana@x.com","numero_whatsapp":"5511900000001","cidade":"Rio","estado":"RJ","fuso_horario":"America/Sao_Paulo","meses_reserva_emergencia":10}"""));
    }

    [Fact]
    [Trait("AC", "007-17")]
    public async Task Put_EmailOfAnotherUser_Returns409_WithoutLeakingTheOtherEmail()
    {
        await _env.InsertUserAsync("5511900000003", "Beto", "beto@x.com");

        var (status, body) = await _env.Put(_user, Path, """{"email":"beto@x.com","nome":"Novo Nome"}""");

        status.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldBe(SettingsEnv.Error("E-mail já cadastrado"));
        (await _env.ScalarAsync<string>("SELECT nome FROM usuarios WHERE id = @_user", new { _user })).ShouldBe("Ana Souza");
    }

    [Fact]
    [Trait("AC", "007-17")]
    public async Task Put_KeepingTheOwnEmail_IsFine()
    {
        await _env.ExecuteAsync("UPDATE usuarios SET email = 'ana@x.com' WHERE id = @_user", new { _user });

        (await _env.Put(_user, Path, """{"email":"ana@x.com"}""")).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [Trait("AC", "007-17")]
    [InlineData("""{"fuso_horario":null}""", "Fuso horário inválido")]
    [InlineData("""{"fuso_horario":"Marte/Olympus"}""", "Fuso horário inválido")]
    [InlineData("""{"email":1}""", "Dados inválidos")]
    [InlineData("""{"cidade":["x"]}""", "Dados inválidos")]
    [InlineData("""{"estado":5}""", "Dados inválidos")]
    [InlineData("""{"nome":5}""", "Dados inválidos")]
    public async Task Put_InvalidTimeZoneOrTypes_Return400(string json, string message)
    {
        var (status, body) = await _env.Put(_user, Path, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error(message));
    }

    [Fact]
    [Trait("AC", "007-17")]
    public async Task Put_TooLongValues_Return400_AndNothingIsStored()
    {
        var (status, body) = await _env.Put(_user, Path, $$"""{"nome":"{{new string('a', 300)}}"}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Nome deve ter no máximo 255 caracteres"));
        (await _env.Put(_user, Path, $$"""{"cidade":"{{new string('a', 101)}}"}""")).Body
            .ShouldBe(SettingsEnv.Error("Cidade deve ter no máximo 100 caracteres"));
        (await _env.Put(_user, Path, $$"""{"email":"{{new string('a', 256)}}"}""")).Body
            .ShouldBe(SettingsEnv.Error("E-mail deve ter no máximo 255 caracteres"));
    }

    [Fact]
    [Trait("AC", "007-82")]
    public async Task TrailingSlash_Returns404Html()
    {
        var (status, body) = await _env.SendAsync(HttpMethod.Get, Path + "/", userId: _user);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain("<title>404 Not Found</title>");
    }
}
