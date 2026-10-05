using Dapper;
using MeuSecretario.Application.Settings;
using MeuSecretario.Domain.Settings;
using MeuSecretario.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace MeuSecretario.Infrastructure.Settings;

/// <summary>
/// <c>usuarios</c> (perfil, spec 007 Â§3.1 e Â§3.2). O <c>UPDATE</c> monta o <c>SET</c> sÃ³ com fragmentos fixos; todo valor
/// vai em parÃ¢metro. A violaÃ§Ã£o de UNIQUE do e-mail vira <see cref="ProfileUpdateOutcome.EmailTaken"/> (desvio 085);
/// qualquer outro erro do banco sobe como exceÃ§Ã£o.
/// </summary>
public sealed class ProfileRepository(IDbConnectionFactory connections) : IProfileRepository
{
    private const string Columns =
        """
        id AS Id, nome AS Nome, email AS Email, numero_whatsapp AS NumeroWhatsapp, cidade AS Cidade, estado AS Estado,
        fuso_horario AS FusoHorario, meses_reserva_emergencia AS MesesReservaEmergencia
        """;

    public async Task<ProfileRow?> GetAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ProfileRow>(new CommandDefinition(
            $"SELECT {Columns} FROM usuarios WHERE id = @userId AND ativo = true",
            new { userId },
            cancellationToken: cancellationToken));
    }

    public async Task<ProfileUpdateOutcome> UpdateAsync(int userId, ProfileChanges changes, CancellationToken cancellationToken)
    {
        var assignments = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("userId", userId);
        Add(assignments, parameters, "nome", changes.Name);
        Add(assignments, parameters, "email", changes.Email);
        Add(assignments, parameters, "cidade", changes.City);
        Add(assignments, parameters, "estado", changes.State);
        Add(assignments, parameters, "fuso_horario", changes.TimeZone);
        if (changes.ReserveMonths is { } months)
        {
            assignments.Add("meses_reserva_emergencia = @meses_reserva_emergencia");
            parameters.Add("meses_reserva_emergencia", months.Value, System.Data.DbType.Int32);
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            var row = await connection.QuerySingleOrDefaultAsync<ProfileRow>(new CommandDefinition(
                $"UPDATE usuarios SET {string.Join(", ", assignments)} WHERE id = @userId AND ativo = true RETURNING {Columns}",
                parameters,
                cancellationToken: cancellationToken));
            return row is null ? new ProfileUpdateOutcome.NotFound() : new ProfileUpdateOutcome.Updated(row);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new ProfileUpdateOutcome.EmailTaken();
        }
    }

    private static void Add(List<string> assignments, DynamicParameters parameters, string column, FieldChange<string?>? change)
    {
        if (change is { } value)
        {
            assignments.Add($"{column} = @{column}");
            parameters.Add(column, value.Value, System.Data.DbType.String);
        }
    }
}

/// <summary><c>preferenciaschaveapi</c> e <c>chavesapiusuario</c> (spec 007 Â§3.3 e Â§3.4).</summary>
public sealed class ApiKeySettingsRepository(IDbConnectionFactory connections) : IApiKeySettingsRepository
{
    public async Task<IReadOnlyList<ApiKeyPreferenceRow>> ListAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Desvio 088: parte da lista fixa dos trÃªs provedores; has_key reflete a chave ativa mesmo sem preferÃªncia.
        var rows = await connection.QueryAsync<ApiKeyPreferenceRow>(new CommandDefinition(
            """
            SELECT v.provedor AS Provider,
                   COALESCE(p.usar_chave_propria, false) AS UseOwnKey,
                   (c.id IS NOT NULL) AS HasKey
            FROM (VALUES ('gemini', 1), ('weather', 2), ('openroute', 3)) AS v(provedor, ordem)
            LEFT JOIN preferenciaschaveapi p ON p.usuario_id = @userId AND p.provedor = v.provedor
            LEFT JOIN chavesapiusuario c ON c.usuario_id = @userId AND c.provedor = v.provedor AND c.ativo = true
            ORDER BY v.ordem
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task SaveOwnKeyAsync(int userId, string provider, bool useOwnKey, string? encryptedKey, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria)
            VALUES (@userId, @provider, @useOwnKey)
            ON CONFLICT (usuario_id, provedor)
            DO UPDATE SET usar_chave_propria = EXCLUDED.usar_chave_propria, atualizado_em = now()
            """,
            new { userId, provider, useOwnKey },
            transaction,
            cancellationToken: cancellationToken));

        if (encryptedKey is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo)
                VALUES (@userId, @provider, @encryptedKey, true)
                ON CONFLICT (usuario_id, provedor)
                DO UPDATE SET chave_api_criptografada = EXCLUDED.chave_api_criptografada, ativo = true, atualizado_em = now()
                """,
                new { userId, provider, encryptedKey },
                transaction,
                cancellationToken: cancellationToken));
        }
        else if (!useOwnKey)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE chavesapiusuario SET ativo = false, atualizado_em = now() WHERE usuario_id = @userId AND provedor = @provider",
                new { userId, provider },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary><c>notificationconfigs</c> (spec 007 Â§3.6 e Â§3.7). As horas vÃ£o como texto e o banco as converte.</summary>
public sealed class UserNotificationSettingsRepository(IDbConnectionFactory connections) : IUserNotificationSettingsRepository
{
    public async Task<NotificationRow?> GetAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<NotificationRow>(new CommandDefinition(
            """
            SELECT resumo_matinal_ativo AS MorningEnabled, resumo_matinal_hora::text AS MorningTime,
                   checkin_noturno_ativo AS EveningEnabled, checkin_noturno_hora::text AS EveningTime,
                   alertas_financeiros_ativos AS FinancialEnabled, alerta_potes_threshold AS Threshold
            FROM notificationconfigs WHERE usuario_id = @userId
            """,
            new { userId },
            cancellationToken: cancellationToken));
    }

    public async Task UpsertAsync(int userId, NotificationInput input, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // P-007-1 rejeitada: daysBeforeDue continua gravado em alerta_potes_threshold (paridade).
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO notificationconfigs
                   (usuario_id, resumo_matinal_ativo, resumo_matinal_hora, checkin_noturno_ativo, checkin_noturno_hora,
                    alertas_financeiros_ativos, alerta_potes_threshold)
            VALUES (@userId, @morningEnabled, CAST(@morningTime AS time), @eveningEnabled, CAST(@eveningTime AS time),
                    @financialEnabled, @daysBeforeDue)
            ON CONFLICT (usuario_id) DO UPDATE SET
                resumo_matinal_ativo = EXCLUDED.resumo_matinal_ativo,
                resumo_matinal_hora = EXCLUDED.resumo_matinal_hora,
                checkin_noturno_ativo = EXCLUDED.checkin_noturno_ativo,
                checkin_noturno_hora = EXCLUDED.checkin_noturno_hora,
                alertas_financeiros_ativos = EXCLUDED.alertas_financeiros_ativos,
                alerta_potes_threshold = EXCLUDED.alerta_potes_threshold,
                updated_at = now()
            """,
            new
            {
                userId,
                morningEnabled = input.MorningEnabled,
                morningTime = input.MorningTime,
                eveningEnabled = input.EveningEnabled,
                eveningTime = input.EveningTime,
                financialEnabled = input.FinancialEnabled,
                daysBeforeDue = input.DaysBeforeDue,
            },
            cancellationToken: cancellationToken));
    }
}

/// <summary><c>enderecosfavoritos</c> (spec 007 Â§3.8 a Â§3.10); tambÃ©m usada pela 012.</summary>
public sealed class FavoriteAddressRepository(IDbConnectionFactory connections) : IFavoriteAddressRepository
{
    private const string UpsertWithCoordinates =
        """
        INSERT INTO enderecosfavoritos (usuario_id, label, endereco_completo, latitude, longitude)
        VALUES (@userId, @label, @address, @latitude, @longitude)
        ON CONFLICT (usuario_id, label) DO UPDATE SET
            endereco_completo = EXCLUDED.endereco_completo, latitude = EXCLUDED.latitude,
            longitude = EXCLUDED.longitude, updated_at = now()
        """;

    // Desvio 086: sem coordenadas na entrada, mantÃ©m as existentes quando o texto nÃ£o mudou e zera quando mudou.
    private const string UpsertPreservingCoordinates =
        """
        INSERT INTO enderecosfavoritos (usuario_id, label, endereco_completo, latitude, longitude)
        VALUES (@userId, @label, @address, NULL, NULL)
        ON CONFLICT (usuario_id, label) DO UPDATE SET
            endereco_completo = EXCLUDED.endereco_completo,
            latitude = CASE WHEN enderecosfavoritos.endereco_completo = EXCLUDED.endereco_completo
                            THEN enderecosfavoritos.latitude END,
            longitude = CASE WHEN enderecosfavoritos.endereco_completo = EXCLUDED.endereco_completo
                             THEN enderecosfavoritos.longitude END,
            updated_at = now()
        """;

    public async Task<IReadOnlyList<AddressRow>> ListAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AddressRow>(new CommandDefinition(
            """
            SELECT id AS Id, label AS Label, endereco_completo AS Address, latitude AS Latitude, longitude AS Longitude
            FROM enderecosfavoritos WHERE usuario_id = @userId ORDER BY label
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task UpsertAsync(int userId, string label, string address, AddressCoordinates? coordinates, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            coordinates is null ? UpsertPreservingCoordinates : UpsertWithCoordinates,
            new { userId, label, address, latitude = coordinates?.Latitude, longitude = coordinates?.Longitude },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(int userId, string label, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM enderecosfavoritos WHERE usuario_id = @userId AND label = @label",
            new { userId, label },
            cancellationToken: cancellationToken));
        return deleted > 0;
    }
}

public static class SettingsServiceCollectionExtensions
{
    /// <summary>RepositÃ³rios da 007; chamado pelo <c>SettingsModule.AddServices</c>.</summary>
    public static IServiceCollection AddSettingsInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<IProfileRepository, ProfileRepository>();
        services.TryAddSingleton<IApiKeySettingsRepository, ApiKeySettingsRepository>();
        services.TryAddSingleton<IUserNotificationSettingsRepository, UserNotificationSettingsRepository>();
        services.TryAddSingleton<IFavoriteAddressRepository, FavoriteAddressRepository>();
        return services;
    }
}
