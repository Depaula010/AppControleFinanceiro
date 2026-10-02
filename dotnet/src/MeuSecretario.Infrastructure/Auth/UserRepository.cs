using Dapper;
using MeuSecretario.Application.Auth;
using MeuSecretario.Domain.Auth;
using MeuSecretario.Infrastructure.Database;
using Npgsql;

namespace MeuSecretario.Infrastructure.Auth;

/// <summary>
/// Acesso a <c>usuarios</c> e <c>contas</c> com Dapper e SQL equivalente ao do Flask. Dapper (e não EF) porque
/// as duas tabelas são compartilhadas com a feature 002 e um mapeamento EF único bloquearia o trabalho em paralelo.
/// </summary>
public sealed class UserRepository(IDbConnectionFactory connections) : IUserRepository
{
    private const string WhatsappUniqueConstraint = "usuarios_numero_whatsapp_key";

    public async Task<UserRecord?> FindByWhatsappAsync(string digits, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserRecord>(new CommandDefinition(
            """
            SELECT id AS Id, nome AS Nome, numero_whatsapp AS NumeroWhatsapp, senha_hash AS SenhaHash
            FROM usuarios
            WHERE numero_whatsapp = @whatsapp
            """,
            new { whatsapp = digits },
            cancellationToken: cancellationToken));
    }

    public async Task<CreateUserResult> CreateWithDefaultAccountsAsync(
        NewUser user, IReadOnlyList<NewAccount> accounts, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var userId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO usuarios (nome, numero_whatsapp, senha_hash, api_key_automate)
                VALUES (@Nome, @NumeroWhatsapp, @SenhaHash, @ApiKeyAutomate)
                RETURNING id
                """,
                user,
                transaction,
                cancellationToken: cancellationToken));

            foreach (var account in accounts)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO contas (usuario_id, nome_conta, tipo_conta, dia_vencimento, dia_fechamento)
                    VALUES (@UserId, @NomeConta, @TipoConta, @DiaVencimento, @DiaFechamento)
                    """,
                    new { UserId = userId, account.NomeConta, account.TipoConta, account.DiaVencimento, account.DiaFechamento },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return CreateUserResult.Created(userId);
        }
        catch (PostgresException ex) when (
            ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == WhatsappUniqueConstraint)
        {
            // Cadastro simultâneo do mesmo número (desvio 031). A transação é desfeita ao sair do escopo.
            return CreateUserResult.Taken;
        }
    }
}
