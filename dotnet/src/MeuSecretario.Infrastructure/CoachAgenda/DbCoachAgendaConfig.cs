using Dapper;
using MeuSecretario.Application.CoachAgenda;
using MeuSecretario.Domain.CoachAgenda;
using MeuSecretario.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.CoachAgenda;

/// <summary>
/// Spec 015 (RN-015-03 e 04): lê as linhas <c>coach_agenda.*</c> da tabela <c>configuracoes</c> e guarda por 60 s (como a <c>gemini.modelo</c>), então uma troca
/// por SQL vale em até 1 minuto, sem deploy. Linha <c>coach_agenda.nivel.&lt;id-do-calendario&gt;</c> sobrepõe <c>coach_agenda.nivel_padrao</c>; sem nenhuma linha vale
/// <c>completo</c> (a escolha do Rafael). <b>Falha segura:</b> valor desconhecido = <c>ocupado</c>; se o banco falhar e não houver cópia boa, tudo vale <c>ocupado</c>
/// e o mascaramento fica ligado.
/// </summary>
public sealed partial class DbCoachAgendaConfig(IDbConnectionFactory connections, TimeProvider clock, ILogger<DbCoachAgendaConfig> logger) : ICoachAgendaConfig
{
    public const string PrefixoDoNivel = "coach_agenda.nivel.";
    public const string NivelPadrao = "coach_agenda.nivel_padrao";
    public const string Mascarar = "coach_agenda.mascarar";
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private sealed record Linhas(IReadOnlyDictionary<string, string> Valores, bool Falhou);

    private (Linhas Valor, DateTimeOffset ExpiraEm)? _cache;

    public async Task<NivelDeDetalhe> NivelDeAsync(string? calendarId, CancellationToken cancellationToken)
    {
        var linhas = await LerAsync(cancellationToken).ConfigureAwait(false);
        if (linhas.Falhou)
        {
            return NivelDeDetalhe.Ocupado;
        }

        if (calendarId is not null && linhas.Valores.TryGetValue(PrefixoDoNivel + calendarId, out var proprio))
        {
            return NivelDeDetalheTexto.Ler(proprio);
        }

        return linhas.Valores.TryGetValue(NivelPadrao, out var padrao) ? NivelDeDetalheTexto.Ler(padrao) : NivelDeDetalhe.Completo;
    }

    public async Task<bool> MascararAsync(CancellationToken cancellationToken)
    {
        var linhas = await LerAsync(cancellationToken).ConfigureAwait(false);
        return linhas.Falhou || !linhas.Valores.TryGetValue(Mascarar, out var valor) || !string.Equals(valor.Trim(), "false", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Linhas> LerAsync(CancellationToken cancellationToken)
    {
        var agora = clock.GetUtcNow();
        if (_cache is { } hit && hit.ExpiraEm > agora)
        {
            return hit.Valor;
        }

        Linhas lidas;
        try
        {
            await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            var rows = await connection.QueryAsync<(string Chave, string Valor)>(
                new CommandDefinition("SELECT chave, valor FROM configuracoes WHERE starts_with(chave, 'coach_agenda.')", cancellationToken: cancellationToken)).ConfigureAwait(false);
            lidas = new Linhas(rows.ToDictionary(r => r.Chave, r => r.Valor, StringComparer.Ordinal), false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            FalhaAoLer(logger, e.GetType().Name);
            // mantém a última cópia boa; sem ela, tudo fica no mais privado (falha segura)
            lidas = _cache is { } anterior && !anterior.Valor.Falhou ? anterior.Valor : new Linhas(new Dictionary<string, string>(), true);
        }

        _cache = (lidas, clock.GetUtcNow().Add(CacheTtl));
        return lidas;
    }

    [LoggerMessage(EventId = 15010, Level = LogLevel.Warning, Message = "Configuração da agenda do Coach indisponível ({Error}): tudo vale ocupado até a próxima leitura")]
    private static partial void FalhaAoLer(ILogger logger, string error);
}
