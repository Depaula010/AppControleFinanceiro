using System.Net;
using System.Text;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;

namespace MeuSecretario.IntegrationTests.Auth;

/// <summary>Desvio 278: <c>REGISTRATION_ENABLED</c> (padrão false) e <c>TASK_ALERTS_ENABLED</c> (padrão true).</summary>
[Trait("Category", "Docker")]
public class RegistrationFlagTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Body = """{"nome":"Ana","whatsapp":"5511900000099","password":"SenhaSegura123","dia_vencimento":10,"dia_fechamento":3}""";

    [Fact]
    public async Task RegisterDisabled_Answers403_AndCreatesNoUser()
    {
        await postgres.ResetAsync();
        using var client = postgres.Api.WithWebHostBuilder(b => b.UseSetting("REGISTRATION_ENABLED", "false")).CreateClient();

        var response = await client.PostAsync("/auth/register", new StringContent(Body, Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Cadastro desativado");
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        (await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection, "SELECT count(*) FROM usuarios")).ShouldBe(0);
    }
}
