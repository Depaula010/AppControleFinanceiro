using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;

namespace MeuSecretario.UnitTests.Http;

/// <summary>Spec 008 §4.0 / AC-008-01: o filtro devolve o 401 no envelope da rota e não chama o endpoint.</summary>
public class RequireApiKeyFilterTests
{
    private const string Secret = "chave-de-teste-com-mais-de-32-caracteres-0123456789";

    private sealed record Outcome(int? Status, string? Json, bool NextCalled);

    private static async Task<Outcome> Invoke(Func<HttpContext, IResult> unauthorized, params string[] headerValues)
    {
        var http = new DefaultHttpContext();
        if (headerValues.Length > 0)
        {
            http.Request.Headers["x-api-key"] = headerValues;
        }

        var nextCalled = false;
        var filter = new RequireApiKeyFilter(new ApiSecretKeyValidator(Secret), unauthorized);
        var result = await filter.InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>("ok");
            });

        if (result is IResult httpResult)
        {
            var (status, json) = await ResultRunner.Run(httpResult);
            return new Outcome(status, json, nextCalled);
        }

        return new Outcome(null, null, nextCalled);
    }

    private const string BodyC = """{"erro":"Não autorizado","mensagem":"API key inválida ou ausente"}""";

    [Fact]
    [Trait("AC", "008-01")]
    public async Task MissingHeader_Returns401InEnvelopeC_AndSkipsTheEndpoint()
    {
        var outcome = await Invoke(ApiKeyUnauthorized.EnvelopeC);

        outcome.Status.ShouldBe(401);
        outcome.Json.ShouldBe(BodyC);
        outcome.NextCalled.ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "008-01")]
    [InlineData("")]
    [InlineData("errada")]
    [InlineData("chave-de-teste-com-mais-de-32-caracteres-012345678")]
    public async Task EmptyOrWrongHeader_Returns401(string value)
    {
        var outcome = await Invoke(ApiKeyUnauthorized.EnvelopeC, value);

        outcome.Status.ShouldBe(401);
        outcome.Json.ShouldBe(BodyC);
        outcome.NextCalled.ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "008-02")]
    public async Task CorrectHeader_CallsTheEndpoint()
    {
        var outcome = await Invoke(ApiKeyUnauthorized.EnvelopeC, Secret);

        outcome.NextCalled.ShouldBeTrue();
        outcome.Status.ShouldBeNull();
    }

    [Fact]
    public async Task OnlyTheFirstHeaderValueIsConsidered()
    {
        (await Invoke(ApiKeyUnauthorized.EnvelopeC, Secret, "outra")).NextCalled.ShouldBeTrue();
        (await Invoke(ApiKeyUnauthorized.EnvelopeC, "outra", Secret)).NextCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task Envelope_IsConfigurable_B()
    {
        var outcome = await Invoke(_ => Envelopes.B.Error("API key inválida", StatusCodes.Status401Unauthorized));

        outcome.Status.ShouldBe(401);
        outcome.Json.ShouldBe("""{"status":"erro","mensagem":"API key inválida"}""");
        outcome.NextCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task Envelope_IsConfigurable_E()
    {
        var outcome = await Invoke(_ => Envelopes.E.Error("Não autorizado", StatusCodes.Status401Unauthorized), "errada");

        outcome.Status.ShouldBe(401);
        outcome.Json.ShouldBe("""{"status":"erro","resposta":"Não autorizado"}""");
        outcome.NextCalled.ShouldBeFalse();
    }
}
