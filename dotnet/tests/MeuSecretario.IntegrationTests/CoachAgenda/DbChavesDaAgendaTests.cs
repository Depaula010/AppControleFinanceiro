using Dapper;
using MeuSecretario.Application.CoachAgenda;
using MeuSecretario.Infrastructure.CoachAgenda;
using MeuSecretario.IntegrationTests.Accounts;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.CoachAgenda;

/// <summary>Spec 016: as chaves da agenda por usuário no banco real (AC-016-01, 03, 04, 05).</summary>
[Trait("Category", "Docker")]
public sealed class DbChavesDaAgendaTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
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

    private DbChavesDaAgenda Chaves() => new(postgres.Connections);

    [Fact]
    public async Task AC_016_01_O_banco_so_tem_o_hash_e_o_final()
    {
        var nova = await Chaves().GerarAsync(_ana, default);

        nova.Chave.Length.ShouldBe(64);
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        var linha = await connection.QuerySingleAsync<(string Hash, string Final)>("SELECT chave_hash, final FROM coach_agenda_chaves");
        linha.Hash.ShouldBe(FormaDaChave.Hash(nova.Chave));
        linha.Hash.ShouldNotBe(nova.Chave);
        linha.Final.ShouldBe(nova.Chave[^4..]);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM coach_agenda_chaves WHERE chave_hash = @c OR final = @c", new { c = nova.Chave })).ShouldBe(0);
    }

    [Fact]
    public async Task AC_016_04_Cada_chave_resolve_para_o_seu_dono()
    {
        var chaves = Chaves();
        var daAna = await chaves.GerarAsync(_ana, default);
        var doBeto = await chaves.GerarAsync(_beto, default);

        (await chaves.ResolverAsync(daAna.Chave, default)).ShouldBe(_ana);
        (await chaves.ResolverAsync(doBeto.Chave, default)).ShouldBe(_beto);
    }

    [Fact]
    public async Task AC_016_03_Gerar_de_novo_revoga_a_anterior_e_so_uma_fica_ativa()
    {
        var chaves = Chaves();
        var velha = await chaves.GerarAsync(_ana, default);
        var nova = await chaves.GerarAsync(_ana, default);

        (await chaves.ResolverAsync(velha.Chave, default)).ShouldBeNull();
        (await chaves.ResolverAsync(nova.Chave, default)).ShouldBe(_ana);
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM coach_agenda_chaves WHERE usuario_id = @u AND revogada_em IS NULL", new { u = _ana })).ShouldBe(1);
    }

    [Fact]
    public async Task AC_016_05_Chave_desconhecida_ou_revogada_nao_resolve_e_revogar_diz_se_havia()
    {
        var chaves = Chaves();
        var nova = await chaves.GerarAsync(_ana, default);

        (await chaves.ResolverAsync("desconhecida", default)).ShouldBeNull();
        (await chaves.RevogarAsync(_ana, default)).ShouldBeTrue();
        (await chaves.ResolverAsync(nova.Chave, default)).ShouldBeNull();
        (await chaves.RevogarAsync(_ana, default)).ShouldBeFalse();
    }

    [Fact]
    public async Task Consultar_traz_o_final_e_marca_o_ultimo_uso_sem_nunca_trazer_a_chave()
    {
        var chaves = Chaves();
        (await chaves.ConsultarAsync(_ana, default)).ShouldBeNull();
        var nova = await chaves.GerarAsync(_ana, default);
        (await chaves.ConsultarAsync(_ana, default))!.UltimoUsoEm.ShouldBeNull();

        await chaves.ResolverAsync(nova.Chave, default);

        var atual = (await chaves.ConsultarAsync(_ana, default))!;
        atual.Final.ShouldBe(nova.Final);
        atual.UltimoUsoEm.ShouldNotBeNull();
    }
}
