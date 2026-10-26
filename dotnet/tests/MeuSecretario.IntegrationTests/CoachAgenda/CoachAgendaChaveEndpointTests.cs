using System.Net;
using System.Text.Json;
using MeuSecretario.IntegrationTests.Accounts;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.CoachAgenda;

/// <summary>Spec 016, pela API real e o Postgres: gerar, consultar, revogar e usar a chave (AC-016-02, 03, 05, 06, 09).</summary>
[Trait("Category", "Docker")]
public sealed class CoachAgendaChaveEndpointTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Agenda = "/internal/coach/agenda?de=2026-10-26&ate=2026-11-08";
    private const string Rota = "/coach/agenda/chave";

    private readonly AccountsHttp _http = new(postgres);
    private readonly AccountsTestData _data = new(postgres.DataSource);
    private int _ana;
    private int _beto;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _ana = await _data.InsertUserAsync("5511900000001", "Ana");
        _beto = await _data.InsertUserAsync("5511900000002", "Beto");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpStatusCode> UsarAsync(string? chave)
    {
        using var client = postgres.Api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Agenda, UriKind.Relative));
        if (chave is not null)
        {
            request.Headers.Add("X-API-KEY", chave);
        }

        using var resposta = await client.SendAsync(request);
        return resposta.StatusCode;
    }

    private static string Chave(string corpo) => JsonDocument.Parse(corpo).RootElement.GetProperty("chave").GetString()!;

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task AC_016_09_Gerar_consultar_e_revogar_exigem_login(string metodo)
    {
        (await _http.SendAsync(new HttpMethod(metodo), Rota)).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC_016_06_Sem_nenhuma_chave_a_rota_da_agenda_responde_401_e_nao_404()
    {
        (await UsarAsync("qualquer")).ShouldBe(HttpStatusCode.Unauthorized);
        (await UsarAsync(null)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC_016_02_A_chave_aparece_uma_vez_e_a_consulta_so_traz_o_final()
    {
        var (status, corpo) = await _http.SendAsync(HttpMethod.Post, Rota, userId: _ana);
        status.ShouldBe(HttpStatusCode.Created);
        var chave = Chave(corpo);

        var (_, consulta) = await _http.SendAsync(HttpMethod.Get, Rota, userId: _ana);

        consulta.ShouldNotContain(chave);
        var json = JsonDocument.Parse(consulta).RootElement;
        json.GetProperty("existe").GetBoolean().ShouldBeTrue();
        json.GetProperty("final").GetString().ShouldBe(chave[^4..]);
        JsonDocument.Parse((await _http.SendAsync(HttpMethod.Get, Rota, userId: _beto)).Body).RootElement.GetProperty("existe").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_chave_gerada_passa_na_autenticacao_da_agenda_e_o_Google_nao_conectado_responde_503()
    {
        var chave = Chave((await _http.SendAsync(HttpMethod.Post, Rota, userId: _ana)).Body);

        (await UsarAsync(chave)).ShouldBe(HttpStatusCode.ServiceUnavailable);       // autenticou; o Google da Ana não está conectado
        (await UsarAsync("outra-coisa")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC_016_03_Gerar_de_novo_e_revogar_derrubam_a_chave_antiga()
    {
        var velha = Chave((await _http.SendAsync(HttpMethod.Post, Rota, userId: _ana)).Body);
        var nova = Chave((await _http.SendAsync(HttpMethod.Post, Rota, userId: _ana)).Body);

        (await UsarAsync(velha)).ShouldBe(HttpStatusCode.Unauthorized);
        (await UsarAsync(nova)).ShouldBe(HttpStatusCode.ServiceUnavailable);

        (await _http.SendAsync(HttpMethod.Delete, Rota, userId: _ana)).Status.ShouldBe(HttpStatusCode.OK);
        (await UsarAsync(nova)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AC_016_09_A_api_key_pessoal_gravada_no_banco_autentica_a_agenda_sem_gerar_chave_nenhuma()
    {
        await using (var connection = await postgres.DataSource.OpenConnectionAsync())
        {
            await Dapper.SqlMapper.ExecuteAsync(connection, "UPDATE usuarios SET api_key_automate = @k WHERE id = @id", new { k = "api-key-pessoal-em-texto-puro-legado", id = _ana });
        }

        (await UsarAsync("api-key-pessoal-em-texto-puro-legado")).ShouldBe(HttpStatusCode.ServiceUnavailable);   // autenticou; o Google da Ana não está conectado
        (await UsarAsync("api-key-pessoal-errada")).ShouldBe(HttpStatusCode.Unauthorized);
    }
}
