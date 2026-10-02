using Dapper;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Domain.ApiKeys;
using MeuSecretario.Infrastructure.Database;
using Npgsql;

namespace MeuSecretario.Infrastructure.ApiKeys;

/// <summary>
/// <c>chavesapiusuario</c> (spec 008 §4.1 a 4.4 e porta de chaves). SQL parametrizado, nomes minúsculos.
/// Violação de FK (<c>23503</c>) vira "usuário não encontrado" e de UNIQUE (<c>23505</c>) "já existe" (desvios 042 e 043).
/// </summary>
public sealed class ApiKeyRepository(IDbConnectionFactory connections) : IApiKeyRepository
{
    public async Task<InsertKeyResult> InsertAsync(int userId, string provider, string encryptedKey, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo, criado_em, atualizado_em)
                VALUES (@userId, @provider, @encryptedKey, TRUE, now(), now())
                RETURNING id
                """,
                new { userId, provider, encryptedKey },
                cancellationToken: cancellationToken));
            return new InsertKeyResult.Created(id);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return new InsertKeyResult.UserNotFound();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new InsertKeyResult.AlreadyExists();
        }
    }

    public async Task<IReadOnlyList<ApiKeyRow>> ListByUserAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ApiKeyRow>(new CommandDefinition(
            """
            SELECT id AS Id, provedor AS Provider, ativo AS Active, ultimo_uso_em AS LastUsedAt,
                   criado_em AS CreatedAt, atualizado_em AS UpdatedAt
            FROM chavesapiusuario
            WHERE usuario_id = @userId
            ORDER BY provedor, criado_em DESC
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<KeyRef?> UpdateAsync(int keyId, int ownerUserId, string? encryptedKey, bool? active, CancellationToken cancellationToken)
    {
        // O SET é montado só com fragmentos fixos; a entrada do usuário vai sempre em parâmetros.
        var set = (encryptedKey is null ? string.Empty : "chave_api_criptografada = @encryptedKey, ")
                  + (active is null ? string.Empty : "ativo = @active, ");

        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<KeyRef>(new CommandDefinition(
            $"""
            UPDATE chavesapiusuario SET {set}atualizado_em = now()
            WHERE id = @keyId AND usuario_id = @ownerUserId
            RETURNING provedor AS Provider, usuario_id AS UserId
            """,
            new { keyId, ownerUserId, encryptedKey, active },
            cancellationToken: cancellationToken));
    }

    public async Task<KeyRef?> DeactivateAsync(int keyId, int ownerUserId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<KeyRef>(new CommandDefinition(
            """
            UPDATE chavesapiusuario SET ativo = FALSE, atualizado_em = now()
            WHERE id = @keyId AND usuario_id = @ownerUserId
            RETURNING provedor AS Provider, usuario_id AS UserId
            """,
            new { keyId, ownerUserId },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> FindActiveEncryptedAsync(int userId, string provider, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT chave_api_criptografada FROM chavesapiusuario
            WHERE usuario_id = @userId AND provedor = @provider AND ativo = TRUE
            """,
            new { userId, provider },
            cancellationToken: cancellationToken));
    }

    public async Task TouchLastUseAsync(int userId, string provider, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE chavesapiusuario SET ultimo_uso_em = now(), atualizado_em = now()
            WHERE usuario_id = @userId AND provedor = @provider
            """,
            new { userId, provider },
            cancellationToken: cancellationToken));
    }
}

/// <summary><c>preferenciaschaveapi</c> (spec 008 §4.5 a 4.7).</summary>
public sealed class PreferenceRepository(IDbConnectionFactory connections) : IPreferenceRepository
{
    public async Task<UpsertPreferenceResult> UpsertAsync(int userId, string provider, bool useOwnKey, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria, atualizado_em)
                VALUES (@userId, @provider, @useOwnKey, now())
                ON CONFLICT (usuario_id, provedor)
                DO UPDATE SET usar_chave_propria = EXCLUDED.usar_chave_propria, atualizado_em = now()
                RETURNING id
                """,
                new { userId, provider, useOwnKey },
                cancellationToken: cancellationToken));
            return new UpsertPreferenceResult.Saved(id);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return new UpsertPreferenceResult.UserNotFound();
        }
    }

    public async Task<IReadOnlyList<PreferenceRow>> ListAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PreferenceRow>(new CommandDefinition(
            """
            SELECT id AS Id, provedor AS Provider, usar_chave_propria AS UseOwnKey, atualizado_em AS UpdatedAt
            FROM preferenciaschaveapi
            WHERE usuario_id = @userId
            ORDER BY provedor
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool> DeleteAsync(int userId, string provider, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var id = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            "DELETE FROM preferenciaschaveapi WHERE usuario_id = @userId AND provedor = @provider RETURNING id",
            new { userId, provider },
            cancellationToken: cancellationToken));
        return id is not null;
    }

    public async Task<bool?> FindAsync(int userId, string provider, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            "SELECT usar_chave_propria FROM preferenciaschaveapi WHERE usuario_id = @userId AND provedor = @provider",
            new { userId, provider },
            cancellationToken: cancellationToken));
    }
}

/// <summary><c>rastreamentousoapi</c> (spec 008 §4.8 e registro de uso da porta).</summary>
public sealed class UsageRepository(IDbConnectionFactory connections) : IUsageRepository
{
    public async Task<IReadOnlyList<UsageRow>> QueryMonthAsync(int userId, string month, string? provider, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<UsageRow>(new CommandDefinition(
            """
            SELECT provedor AS Provider, tipo_chave AS KeyType, SUM(quantidade_chamadas) AS TotalCalls,
                   mes_ano AS Month, MAX(atualizado_em) AS LastUpdate
            FROM rastreamentousoapi
            WHERE usuario_id = @userId AND mes_ano = @month AND (@provider::text IS NULL OR provedor = @provider)
            GROUP BY provedor, tipo_chave, mes_ano
            ORDER BY provedor, tipo_chave
            """,
            new { userId, month, provider },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task IncrementAsync(int userId, string provider, string keyType, string month, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO rastreamentousoapi (usuario_id, provedor, tipo_chave, quantidade_chamadas, mes_ano, atualizado_em)
            VALUES (@userId, @provider, @keyType, 1, @month, now())
            ON CONFLICT (usuario_id, provedor, tipo_chave, mes_ano)
            DO UPDATE SET quantidade_chamadas = rastreamentousoapi.quantidade_chamadas + 1, atualizado_em = now()
            """,
            new { userId, provider, keyType, month },
            cancellationToken: cancellationToken));
    }

    public async Task<long> SumSystemMonthAsync(int userId, string provider, string month, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(quantidade_chamadas), 0) FROM rastreamentousoapi
            WHERE usuario_id = @userId AND provedor = @provider AND mes_ano = @month AND tipo_chave = @systemType
            """,
            new { userId, provider, month, systemType = KeyTypes.System },
            cancellationToken: cancellationToken));
    }
}

/// <summary><c>logacessochaveapi</c> (spec 008 §4.9; escrita pela porta, desvio 047).</summary>
public sealed class AccessLogRepository(IDbConnectionFactory connections) : IAccessLogRepository
{
    public async Task<IReadOnlyList<AccessLogRow>> ListAsync(int userId, string? provider, bool? success, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AccessLogRow>(new CommandDefinition(
            """
            SELECT id AS Id, provedor AS Provider, tipo_chave AS KeyType, operacao AS Operation, sucesso AS Success,
                   mensagem_erro AS ErrorMessage, criado_em AS CreatedAt
            FROM logacessochaveapi
            WHERE usuario_id = @userId
              AND (@provider::text IS NULL OR provedor = @provider)
              AND (@success::boolean IS NULL OR sucesso = @success)
            ORDER BY criado_em DESC
            LIMIT @limit
            """,
            new { userId, provider, success, limit },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task InsertAsync(
        int userId, string provider, string keyType, string operation, bool success, string? errorMessage, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO logacessochaveapi (usuario_id, provedor, tipo_chave, operacao, sucesso, mensagem_erro, endereco_ip)
            VALUES (@userId, @provider, @keyType, @operation, @success, @errorMessage, NULL)
            """,
            new { userId, provider, keyType, operation, success, errorMessage },
            cancellationToken: cancellationToken));
    }
}

/// <summary>Plano ativo para a verificação de limite (spec 008 §5).</summary>
public sealed class PlanRepository(IDbConnectionFactory connections) : IPlanRepository
{
    public async Task<ActivePlan?> FindActivePlanAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ActivePlan>(new CommandDefinition(
            """
            SELECT p.nome_plano AS PlanName, p.limite_gemini AS GeminiLimit,
                   p.limite_weather AS WeatherLimit, p.limite_openroute AS OpenRouteLimit
            FROM assinaturasusuario a JOIN planos p ON a.plano_id = p.id
            WHERE a.usuario_id = @userId AND a.status = 'ativo' AND (a.data_fim IS NULL OR a.data_fim > now())
            ORDER BY a.data_inicio DESC
            LIMIT 1
            """,
            new { userId },
            cancellationToken: cancellationToken));
    }
}
