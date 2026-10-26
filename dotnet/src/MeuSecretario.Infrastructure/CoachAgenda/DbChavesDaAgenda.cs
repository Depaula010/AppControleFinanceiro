using Dapper;
using MeuSecretario.Application.CoachAgenda;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.CoachAgenda;

/// <summary>Spec 016: tabela <c>coach_agenda_chaves</c>. Nada daqui guarda nem registra a chave em texto, só o hash.</summary>
public sealed class DbChavesDaAgenda(IDbConnectionFactory connections) : IChavesDaAgenda
{
    public async Task<int?> ResolverAsync(string chave, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "UPDATE coach_agenda_chaves SET ultimo_uso_em = NOW() WHERE chave_hash = @hash AND revogada_em IS NULL RETURNING usuario_id",
            new { hash = FormaDaChave.Hash(chave) }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<ChaveGerada> GerarAsync(int userId, CancellationToken cancellationToken)
    {
        var chave = FormaDaChave.Nova();
        await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE coach_agenda_chaves SET revogada_em = NOW() WHERE usuario_id = @userId AND revogada_em IS NULL", new { userId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var criadaEm = await connection.QuerySingleAsync<DateTime>(new CommandDefinition(
            "INSERT INTO coach_agenda_chaves (usuario_id, chave_hash, final) VALUES (@userId, @hash, @final) RETURNING criada_em",
            new { userId, hash = FormaDaChave.Hash(chave), final = FormaDaChave.Final(chave) }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ChaveGerada(chave, FormaDaChave.Final(chave), criadaEm);
    }

    public async Task<ChaveExistente?> ConsultarAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<ChaveExistente>(new CommandDefinition(
            "SELECT final AS Final, criada_em AS CriadaEm, ultimo_uso_em AS UltimoUsoEm FROM coach_agenda_chaves WHERE usuario_id = @userId AND revogada_em IS NULL",
            new { userId }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<bool> RevogarAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE coach_agenda_chaves SET revogada_em = NOW() WHERE usuario_id = @userId AND revogada_em IS NULL", new { userId }, cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
    }
}
