using MeuSecretario.Api.Auth;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.UnitTests.Fixtures;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Auth;

/// <summary>Spec 001 §4.4 / AC-001-40 a 44: o filtro equivale ao <c>token_required</c> do Flask.</summary>
public class RequireUserFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed record Outcome(int? Status, string? Json, bool NextCalled, ICurrentUser CurrentUser);

    private static async Task<Outcome> Invoke(string? authorization)
    {
        var jwt = new JwtTokenService(AuthFixtures.JwtSecret, new FakeTimeProvider(Now));
        var services = new ServiceCollection();
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (authorization is not null)
        {
            http.Request.Headers.Authorization = authorization;
        }

        var nextCalled = false;
        var result = await new RequireUserFilter(jwt).InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>("ok");
            });

        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        if (result is IResult httpResult)
        {
            var (status, json) = await ResultRunner.Run(httpResult);
            return new Outcome(status, json, nextCalled, currentUser);
        }

        return new Outcome(null, null, nextCalled, currentUser);
    }

    private static string Error(string message) => $$"""{"status":"error","message":"{{message}}"}""";

    [Theory]
    [Trait("AC", "001-40")]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingHeader_Returns401TokenNotProvided(string? header)
    {
        var outcome = await Invoke(header);

        outcome.Status.ShouldBe(401);
        outcome.Json.ShouldBe(Error("Token de autenticação não fornecido"));
        outcome.NextCalled.ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "001-41")]
    [InlineData("Token abc")]
    [InlineData("Bearer")]
    [InlineData("Bearer a b")]
    [InlineData("abc")]
    [InlineData("   ")] // Python: header truthy, split() vazio => erro de formato (não "não fornecido")
    public async Task WrongFormat_Returns401InvalidFormat(string header)
    {
        var outcome = await Invoke(header);

        outcome.Status.ShouldBe(401);
        outcome.Json.ShouldBe(Error("Formato de autenticação inválido. Use: Bearer <token>"));
        outcome.NextCalled.ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "001-41")]
    [InlineData("Bearer {0}")]
    [InlineData("bearer {0}")]
    [InlineData("BEARER {0}")]
    [InlineData("Bearer   {0}")] // vários espaços: split() do Python aceita
    public async Task BearerScheme_IsCaseInsensitive_AndToleratesExtraSpaces(string headerTemplate)
    {
        var outcome = await Invoke(string.Format(System.Globalization.CultureInfo.InvariantCulture, headerTemplate, AuthFixtures.JwtToken("valid")));

        outcome.NextCalled.ShouldBeTrue();
        outcome.CurrentUser.UserId.ShouldBe(AuthFixtures.JwtUserId);
    }

    [Theory]
    [Trait("AC", "001-42")]
    [InlineData("Bearer lixo")]
    [InlineData("expired")]
    [InlineData("alg_none")]
    [InlineData("wrong_secret")]
    public async Task InvalidToken_Returns401InvalidOrExpired(string tokenOrFixture)
    {
        var token = tokenOrFixture.StartsWith("Bearer ", StringComparison.Ordinal)
            ? tokenOrFixture["Bearer ".Length..]
            : AuthFixtures.JwtToken(tokenOrFixture);

        var outcome = await Invoke($"Bearer {token}");

        outcome.Status.ShouldBe(401);
        outcome.Json.ShouldBe(Error("Token inválido ou expirado"));
        outcome.NextCalled.ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "001-43")]
    [InlineData("no_user_id")]
    [InlineData("user_id_zero")]
    [InlineData("sub_only")]
    public async Task ValidTokenWithoutUsableUserId_Returns401MissingUserId(string fixtureName)
    {
        var outcome = await Invoke($"Bearer {AuthFixtures.JwtToken(fixtureName)}");

        outcome.Status.ShouldBe(401);
        outcome.Json.ShouldBe(Error("Token não contém user_id"));
        outcome.NextCalled.ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "001-44")]
    public async Task ValidToken_CallsTheEndpointAndExposesTheUserId()
    {
        var outcome = await Invoke($"Bearer {AuthFixtures.JwtToken("valid")}");

        outcome.NextCalled.ShouldBeTrue();
        outcome.Status.ShouldBeNull();
        outcome.CurrentUser.UserId.ShouldBe(42);
    }

    [Fact]
    public void CurrentUser_BeforeAuthentication_Throws()
    {
        Should.Throw<InvalidOperationException>(() => new CurrentUserAccessor().UserId);
    }
}
