using Dapper;
using MeuSecretario.Domain.CoachAgenda;
using MeuSecretario.Infrastructure.CoachAgenda;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MeuSecretario.Infrastructure.Database;
using Npgsql;

namespace MeuSecretario.IntegrationTests.CoachAgenda;

/// <summary>Spec 015 (T4): a configuração da agenda vem da tabela <c>configuracoes</c>, com cache de 60 s e falha segura (AC-015-05 e 06).</summary>
[Trait("Category", "Docker")]
public sealed class DbCoachAgendaConfigTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly FakeTimeProvider _clock = new();

    public async Task InitializeAsync()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("DELETE FROM configuracoes WHERE starts_with(chave, 'coach_agenda.')");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DbCoachAgendaConfig Config() => new(postgres.Connections, _clock, NullLogger<DbCoachAgendaConfig>.Instance);

    private async Task SetAsync(string chave, string valor)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO configuracoes (chave, valor) VALUES (@chave, @valor) ON CONFLICT (chave) DO UPDATE SET valor = EXCLUDED.valor", new { chave, valor });
    }

    [Fact]
    public async Task AC_015_05_Sem_nenhuma_linha_vale_completo_e_o_mascaramento_fica_ligado()
    {
        var c = Config();

        (await c.NivelDeAsync("qualquer", default)).ShouldBe(NivelDeDetalhe.Completo);
        (await c.MascararAsync(default)).ShouldBeTrue();
    }

    [Fact]
    public async Task AC_015_05_O_calendario_com_linha_propria_sobrepoe_o_padrao()
    {
        await SetAsync(DbCoachAgendaConfig.NivelPadrao, "titulos");
        await SetAsync(DbCoachAgendaConfig.PrefixoDoNivel + "trabalho@empresa.com", "ocupado");

        var c = Config();

        (await c.NivelDeAsync("trabalho@empresa.com", default)).ShouldBe(NivelDeDetalhe.Ocupado);
        (await c.NivelDeAsync("pessoal@gmail.com", default)).ShouldBe(NivelDeDetalhe.Titulos);
    }

    [Fact]
    public async Task AC_015_06_Valor_desconhecido_vale_ocupado()
    {
        await SetAsync(DbCoachAgendaConfig.PrefixoDoNivel + "cal", "tudo liberado");

        (await Config().NivelDeAsync("cal", default)).ShouldBe(NivelDeDetalhe.Ocupado);
    }

    [Fact]
    public async Task O_mascaramento_so_desliga_com_false()
    {
        await SetAsync(DbCoachAgendaConfig.Mascarar, "false");
        (await Config().MascararAsync(default)).ShouldBeFalse();

        await SetAsync(DbCoachAgendaConfig.Mascarar, "talvez");
        _clock.Advance(DbCoachAgendaConfig.CacheTtl + TimeSpan.FromSeconds(1));
        (await Config().MascararAsync(default)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_troca_por_SQL_vale_depois_de_60_segundos_e_nao_antes()
    {
        var c = Config();
        (await c.NivelDeAsync("cal", default)).ShouldBe(NivelDeDetalhe.Completo);
        await SetAsync(DbCoachAgendaConfig.NivelPadrao, "ocupado");

        (await c.NivelDeAsync("cal", default)).ShouldBe(NivelDeDetalhe.Completo);                          // ainda no cache
        _clock.Advance(DbCoachAgendaConfig.CacheTtl + TimeSpan.FromSeconds(1));
        (await c.NivelDeAsync("cal", default)).ShouldBe(NivelDeDetalhe.Ocupado);
    }

    [Fact]
    public async Task AC_015_06_Banco_fora_do_ar_sem_copia_boa_vale_ocupado_e_mascara()
    {
        var c = new DbCoachAgendaConfig(new FabricaIntermitente(postgres.Connections) { Falhar = true }, _clock, NullLogger<DbCoachAgendaConfig>.Instance);

        (await c.NivelDeAsync("cal", default)).ShouldBe(NivelDeDetalhe.Ocupado);
        (await c.MascararAsync(default)).ShouldBeTrue();
    }

    [Fact]
    public async Task Banco_fora_do_ar_depois_de_uma_leitura_boa_mantem_a_ultima_copia()
    {
        await SetAsync(DbCoachAgendaConfig.NivelPadrao, "titulos");
        var fabrica = new FabricaIntermitente(postgres.Connections);
        var c = new DbCoachAgendaConfig(fabrica, _clock, NullLogger<DbCoachAgendaConfig>.Instance);
        (await c.NivelDeAsync("cal", default)).ShouldBe(NivelDeDetalhe.Titulos);

        fabrica.Falhar = true;
        _clock.Advance(DbCoachAgendaConfig.CacheTtl + TimeSpan.FromSeconds(1));

        (await c.NivelDeAsync("cal", default)).ShouldBe(NivelDeDetalhe.Titulos);
    }

    private sealed class FabricaIntermitente(IDbConnectionFactory real) : IDbConnectionFactory
    {
        public bool Falhar { get; set; }

        public Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default) =>
            Falhar ? throw new InvalidOperationException("banco fora") : real.OpenAsync(cancellationToken);
    }
}
