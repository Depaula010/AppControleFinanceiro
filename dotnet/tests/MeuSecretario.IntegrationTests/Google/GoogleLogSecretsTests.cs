using System.Net;
using MeuSecretario.Application.Google;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Google;

/// <summary>AC-010-89: tokens, <c>code</c>, <c>state</c>, <c>client_secret</c> e número completo nunca aparecem em log.</summary>
[Trait("Category", "Docker")]
public class GoogleLogSecretsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private static readonly string[] Secrets =
    [
        "acc-segredo", "ref-segredo", "cod-segredo", "client-secret-teste", "segredo-do-google", "5511999991072",
    ];

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "010-89")]
    public async Task FailingFlows_LogOnlyIdsAndExceptionTypes()
    {
        var uid = await GoogleDb.SeedUser(postgres, "Maria", "5511999991072");
        await using var host = new GoogleTestHost(postgres);
        var state = host.Services.GetRequiredService<OAuthStateCodec>().Create(uid);

        // 1) o Google recusa o código e devolve texto com "segredo"
        host.Google.Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_client","error_description":"segredo-do-google client-secret-teste"}""");
        (await host.Get($"/oauth2callback?code=cod-segredo&state={Uri.EscapeDataString(state)}")).Response
            .StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        // 2) state inválido
        (await host.Get("/oauth2callback?code=cod-segredo&state=lixo")).Response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        // 3) revogação com falha do Google
        await GoogleDb.SeedTokens(postgres, uid, access: "acc-segredo", refresh: "ref-segredo");
        host.Google.Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_token","error_description":"segredo-do-google"}""");
        using (var client = host.CreateClient())
        using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/disconnect-calendar/{uid}", UriKind.Relative)))
        {
            request.Headers.Add("x-api-key", AuthFixtures.JwtSecret);
            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // 4) token expirado com refresh revogado: marca, notifica (o bot recebe o número) e segue para o consentimento
        await GoogleDb.SeedTokens(postgres, uid, access: "acc-segredo", refresh: "ref-segredo", expiresIn: TimeSpan.FromMinutes(-10));
        host.Google.Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"segredo-do-google"}""");
        var link = host.Services.GetRequiredService<ConnectLinkTokenCodec>().Create(uid);
        (await host.Get($"/connect-calendar/{uid}?t={link}")).Response.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var all = string.Join('\n', host.Logs.Entries);
        all.ShouldContain("10400"); // o log de "marcado para reconexão" existe: o teste olha logs de verdade
        foreach (var secret in Secrets.Append(state).Append(link))
        {
            all.ShouldNotContain(secret);
        }

        host.Bot.Sent.ShouldHaveSingleItem().Number.ShouldBe("5511999991072");
    }
}
