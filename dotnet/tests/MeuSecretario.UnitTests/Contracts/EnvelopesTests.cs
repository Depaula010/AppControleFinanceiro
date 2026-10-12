using System.Text.Json;
using MeuSecretario.Api.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.UnitTests.Contracts;

public class EnvelopesTests
{
    private static async Task<(int Status, string Json)> Run(IResult result)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task A_Success_UsesStatusSuccessMessageAndData()
    {
        var (status, json) = await Run(Envelopes.A.Success(new { id = 1 }, "Conta criada com sucesso", StatusCodes.Status201Created));

        status.ShouldBe(201);
        json.ShouldBe("""{"status":"success","message":"Conta criada com sucesso","data":{"id":1}}""");
    }

    [Fact]
    public async Task A_Success_OmitsMessageAndDataWhenAbsent()
    {
        var (_, json) = await Run(Envelopes.A.Success());

        json.ShouldBe("""{"status":"success"}""");
    }

    [Fact]
    public async Task A_SuccessWithFields_PutsFieldsAtTheRootInOrder()
    {
        var fields = new Dictionary<string, object?>
        {
            ["token"] = "abc",
            ["user"] = new Dictionary<string, object?> { ["id"] = 1, ["nome"] = "Ana", ["whatsapp"] = "5511999999999" },
        };

        var (status, json) = await Run(Envelopes.A.SuccessWithFields(fields));

        status.ShouldBe(200);
        json.ShouldBe("""{"status":"success","token":"abc","user":{"id":1,"nome":"Ana","whatsapp":"5511999999999"}}""");
    }

    [Fact]
    public async Task A_SuccessWithFields_PutsMessageBeforeFields_LikeRegister()
    {
        var (status, json) = await Run(Envelopes.A.SuccessWithFields(
            new Dictionary<string, object?> { ["user_id"] = 123 }, "Usuário cadastrado com sucesso", StatusCodes.Status201Created));

        status.ShouldBe(201);
        json.ShouldBe("""{"status":"success","message":"Usuário cadastrado com sucesso","user_id":123}""");
    }

    [Fact]
    public async Task A_SuccessWithFields_SupportsBooleanLikeVerify()
    {
        var (_, json) = await Run(Envelopes.A.SuccessWithFields(
            new Dictionary<string, object?> { ["valid"] = true, ["user_id"] = 42 }));

        json.ShouldBe("""{"status":"success","valid":true,"user_id":42}""");
    }

    [Fact]
    public async Task A_Error_UsesStatusErrorAndMessage()
    {
        var (status, json) = await Run(Envelopes.A.Error("Token inválido ou expirado", 401));

        status.ShouldBe(401);
        json.ShouldBe("""{"status":"error","message":"Token inválido ou expirado"}""");
    }

    [Fact]
    public async Task B_Success_PutsExtrasAtTheRoot()
    {
        var extras = new Dictionary<string, object?> { ["keys_deleted"] = 3 };

        var (_, json) = await Run(Envelopes.B.Success("Cache limpo", extras));

        json.ShouldBe("""{"status":"sucesso","mensagem":"Cache limpo","keys_deleted":3}""");
    }

    [Fact]
    public async Task B_Error_UsesStatusErroAndMensagem()
    {
        var (status, json) = await Run(Envelopes.B.Error("Não autorizado", 401));

        status.ShouldBe(401);
        json.ShouldBe("""{"status":"erro","mensagem":"Não autorizado"}""");
    }

    [Fact]
    public async Task C_Error_HasNoStatusKey()
    {
        var (_, json) = await Run(Envelopes.C.Error("Não autorizado", "API key inválida ou ausente", 401));

        json.ShouldBe("""{"erro":"Não autorizado","mensagem":"API key inválida ou ausente"}""");
    }

    [Fact]
    public async Task D_Error_UsesEnglishKeys()
    {
        var (status, json) = await Run(Envelopes.D.Error("Not found", "x", 404));

        status.ShouldBe(404);
        json.ShouldBe("""{"error":"Not found","message":"x"}""");
    }

    [Fact]
    public async Task E_Success_UsesRespostaAndBadRequestUsesMensagem()
    {
        (await Run(Envelopes.E.Success("Olá"))).Json.ShouldBe("""{"status":"sucesso","resposta":"Olá"}""");

        var (status, json) = await Run(Envelopes.E.BadRequest("Dados faltando"));
        status.ShouldBe(400);
        json.ShouldBe("""{"status":"erro","mensagem":"Dados faltando"}""");
    }

    [Fact]
    public async Task Json_KeepsEmojisAndAccentsUnescapedAndNullsPresent()
    {
        var body = new Dictionary<string, object?> { ["database"] = "✅ Conectado", ["x"] = null };
        var (_, json) = await Run(Envelopes.C.Success(body));

        json.ShouldBe("""{"database":"✅ Conectado","x":null}""");
        JsonDocument.Parse(json).RootElement.GetProperty("x").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
