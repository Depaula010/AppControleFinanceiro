using System.Data;
using Dapper;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Infrastructure.Database;
using Npgsql;

namespace MeuSecretario.Infrastructure.ApiKeys;

/// <summary><c>consentimentousuario</c> com as colunas reais do banco (desvio 028; spec 008 §4.10, 4.11, 4.15).</summary>
public sealed class ConsentRepository(IDbConnectionFactory connections) : IConsentRepository
{
    public async Task<UpsertConsentResult> UpsertAsync(int userId, string type, bool given, string version, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            // Desvio 041: o mesmo (usuário, tipo, versão) atualiza a linha; revogar grava revogado_em e reconceder o zera.
            var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO consentimentousuario
                    (usuario_id, tipo_consentimento, consentimento_dado, versao_consentimento, data_consentimento, ip_consentimento)
                VALUES (@userId, @type, @given, @version, now(), NULL)
                ON CONFLICT (usuario_id, tipo_consentimento, versao_consentimento)
                DO UPDATE SET consentimento_dado = EXCLUDED.consentimento_dado,
                              data_consentimento = now(),
                              revogado_em = CASE WHEN EXCLUDED.consentimento_dado THEN NULL ELSE now() END
                RETURNING id
                """,
                new { userId, type, given, version },
                cancellationToken: cancellationToken));
            return new UpsertConsentResult.Saved(id);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return new UpsertConsentResult.UserNotFound();
        }
    }

    public async Task<IReadOnlyList<ConsentRow>> ListAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ConsentRow>(new CommandDefinition(
            """
            SELECT id AS Id, tipo_consentimento AS Type, consentimento_dado AS Given,
                   versao_consentimento AS Version, data_consentimento AS ConsentedAt
            FROM consentimentousuario
            WHERE usuario_id = @userId
            ORDER BY data_consentimento DESC, id DESC
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool?> LatestGivenAsync(int userId, string type, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            """
            SELECT consentimento_dado FROM consentimentousuario
            WHERE usuario_id = @userId AND tipo_consentimento = @type
            ORDER BY data_consentimento DESC, id DESC
            LIMIT 1
            """,
            new { userId, type },
            cancellationToken: cancellationToken));
    }
}

/// <summary>Exportação (§4.13) e deleção da conta (§4.14) do usuário.</summary>
public sealed class AccountDataRepository(IDbConnectionFactory connections) : IAccountDataRepository
{
    public async Task<ExportData> ExportAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        // Um snapshot só para as cinco leituras.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var keys = await connection.QueryAsync<ExportKeyRow>(new CommandDefinition(
            """
            SELECT id AS Id, provedor AS Provider, chave_api_criptografada AS StoredKey, ativo AS Active,
                   ultimo_uso_em AS LastUsedAt, criado_em AS CreatedAt, atualizado_em AS UpdatedAt
            FROM chavesapiusuario WHERE usuario_id = @userId ORDER BY provedor, id
            """,
            new { userId }, transaction, cancellationToken: cancellationToken));

        var preferences = await connection.QueryAsync<PreferenceRow>(new CommandDefinition(
            """
            SELECT id AS Id, provedor AS Provider, usar_chave_propria AS UseOwnKey, atualizado_em AS UpdatedAt
            FROM preferenciaschaveapi WHERE usuario_id = @userId ORDER BY provedor
            """,
            new { userId }, transaction, cancellationToken: cancellationToken));

        var usage = await connection.QueryAsync<ExportUsageRow>(new CommandDefinition(
            """
            SELECT provedor AS Provider, tipo_chave AS KeyType, quantidade_chamadas AS Calls, mes_ano AS Month
            FROM rastreamentousoapi WHERE usuario_id = @userId ORDER BY mes_ano DESC, provedor, tipo_chave
            """,
            new { userId }, transaction, cancellationToken: cancellationToken));

        var logs = await connection.QueryAsync<ExportLogRow>(new CommandDefinition(
            """
            SELECT provedor AS Provider, tipo_chave AS KeyType, sucesso AS Success, mensagem_erro AS ErrorMessage,
                   timestamp_acesso AS AccessedAt
            FROM logacessochaveapi WHERE usuario_id = @userId ORDER BY timestamp_acesso DESC, id DESC LIMIT 1000
            """,
            new { userId }, transaction, cancellationToken: cancellationToken));

        var subscription = await connection.QuerySingleOrDefaultAsync<SubscriptionRow>(new CommandDefinition(
            """
            SELECT a.id AS Id, p.nome_plano AS PlanName, p.preco_mensal AS MonthlyPrice,
                   p.limite_gemini AS GeminiLimit, p.limite_weather AS WeatherLimit, p.limite_openroute AS OpenRouteLimit,
                   a.data_inicio AS StartDate, a.data_fim AS EndDate, a.status AS Status
            FROM assinaturasusuario a JOIN planos p ON a.plano_id = p.id
            WHERE a.usuario_id = @userId
            ORDER BY a.data_inicio DESC, a.id DESC
            LIMIT 1
            """,
            new { userId }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new ExportData(keys.AsList(), preferences.AsList(), usage.AsList(), logs.AsList(), subscription);
    }

    public async Task<AccountDeletionCounts?> DeleteAccountCompletelyAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var exists = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT id FROM usuarios WHERE id = @userId FOR UPDATE",
            new { userId }, transaction, cancellationToken: cancellationToken));
        if (exists is null)
        {
            return null;
        }

        // Cada DELETE usa uma tabela fixa (nunca entrada do usuário). A ordem importa: transacoes.conta_id,
        // transacoes.subcategoria_id e agendamentos.subcategoria_id são RESTRICT.
        async Task<int> DeleteFromAsync(string table, string column = "usuario_id") =>
            await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {table} WHERE {column} = @userId",
                new { userId }, transaction, cancellationToken: cancellationToken));

        var apiKeys = await DeleteFromAsync("chavesapiusuario");
        var preferences = await DeleteFromAsync("preferenciaschaveapi");
        var logs = await DeleteFromAsync("logacessochaveapi");
        var usage = await DeleteFromAsync("rastreamentousoapi");
        var consents = await DeleteFromAsync("consentimentousuario");
        var subscriptions = await DeleteFromAsync("assinaturasusuario");

        var transactions = await DeleteFromAsync("transacoes");
        var schedules = await DeleteFromAsync("agendamentos");
        var accounts = await DeleteFromAsync("contas");
        var pots = await DeleteFromAsync("potesdegastos");
        var subcategories = await DeleteFromAsync("subcategoria");
        var macrocategories = await DeleteFromAsync("macrocategoria");
        var users = await DeleteFromAsync("usuarios", "id");

        await transaction.CommitAsync(cancellationToken);
        return new AccountDeletionCounts(
            apiKeys, preferences, logs, usage, consents, subscriptions,
            transactions, schedules, accounts, pots, subcategories, macrocategories, users);
    }
}
