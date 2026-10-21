using System.Globalization;
using Dapper;
using MeuSecretario.IntegrationTests.Accounts;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Jobs;

/// <summary>Semeadura e leitura direta do banco para os testes da feature 013.</summary>
internal sealed class JobsEnv(PostgresFixture postgres)
{
    private readonly AccountsTestData _accounts = new(postgres.DataSource);

    public static DateTime D(string iso) => DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly Day(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public Task<int> UserAsync(string whatsapp = "5511900000001", string name = "Ana") => _accounts.InsertUserAsync(whatsapp, name);

    public Task<int> AccountAsync(int userId, string name = "Conta", string type = "Conta Corrente") =>
        _accounts.InsertAccountAsync(userId, name, type);

    public Task<int> CardAsync(int userId, string name = "Nubank", int closing = 10, int due = 15) =>
        _accounts.InsertAccountAsync(userId, name, "Cartão de Crédito", fechamento: closing, vencimento: due);

    /// <summary>Subcategoria do grupo (cria o grupo, a macro e a subcategoria se faltarem; o grupo <c>Despesa</c> não existe no seed).</summary>
    public async Task<int> SubcategoryAsync(string group)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO grupocategoria (nome_grupo) SELECT @group WHERE NOT EXISTS (SELECT 1 FROM grupocategoria WHERE nome_grupo = @group)",
            new { group });
        await connection.ExecuteAsync(
            """
            INSERT INTO macrocategoria (grupo_id, nome_macro)
            SELECT (SELECT id FROM grupocategoria WHERE nome_grupo = @group LIMIT 1), 'Macro ' || @group
            WHERE NOT EXISTS (SELECT 1 FROM macrocategoria WHERE nome_macro = 'Macro ' || @group)
            """,
            new { group });
        await connection.ExecuteAsync(
            """
            INSERT INTO subcategoria (macro_id, nome_sub)
            SELECT (SELECT id FROM macrocategoria WHERE nome_macro = 'Macro ' || @group LIMIT 1), 'Sub ' || @group
            WHERE NOT EXISTS (SELECT 1 FROM subcategoria WHERE nome_sub = 'Sub ' || @group)
            """,
            new { group });
        return await connection.ExecuteScalarAsync<int>("SELECT id FROM subcategoria WHERE nome_sub = 'Sub ' || @group", new { group });
    }

    public async Task<int> ScheduleAsync(
        int userId, int accountId, string group, string description = "Aluguel", decimal? amount = 1500m, string kind = "FIXO",
        string periodicity = "MENSAL", int day = 10, int? month = null, int? total = null, int executed = 0, bool active = true,
        int notify = 3, string startDate = "2026-01-01")
    {
        var sub = await SubcategoryAsync(group);
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento,
                                      periodicidade, data_inicio, dia_execucao, mes_execucao, total_parcelas, parcelas_executadas,
                                      notificar_antes_dias, ativo)
            VALUES (@userId, @accountId, @sub, @description, @amount, @kind, @periodicity, @startDate, @day, @month, @total,
                    @executed, @notify, @active)
            RETURNING id
            """,
            new { userId, accountId, sub, description, amount, kind, periodicity, startDate = D(startDate), day, month, total, executed, notify, active });
    }

    public async Task<int> TransactionAsync(
        int userId, int accountId, string group, string date, string description = "Aluguel", decimal amount = -1500m,
        int? scheduleId = null, int? invoiceId = null, string? competence = null)
    {
        var sub = await SubcategoryAsync(group);
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, descricao, valor, tipo_transacao, data_transacao,
                                    agendamento_id, fatura_id, competencia)
            VALUES (@userId, @accountId, @sub, @description, @amount, CASE WHEN @amount < 0 THEN 'Despesa' ELSE 'Renda' END,
                    @date, @scheduleId, @invoiceId, @competence)
            RETURNING id
            """,
            new { userId, accountId, sub, description, amount, date = D(date), scheduleId, invoiceId, competence = competence is null ? (DateTime?)null : D(competence) });
    }

    public async Task<int> InvoiceAsync(int cardId, string due, string closing, string status = "Aberta")
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO faturas (conta_id, data_vencimento, data_fechamento, status) VALUES (@cardId, @due, @closing, @status) RETURNING id",
            new { cardId, due = D(due), closing = D(closing), status });
    }

    public async Task NotificationConfigAsync(
        int userId, bool summary = true, string summaryHour = "07:00", bool alerts = true, bool checkin = true, string checkinHour = "20:00")
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO notificationconfigs (usuario_id, resumo_matinal_ativo, resumo_matinal_hora, alertas_financeiros_ativos,
                                             checkin_noturno_ativo, checkin_noturno_hora)
            VALUES (@userId, @summary, @summaryHour::time, @alerts, @checkin, @checkinHour::time)
            """,
            new { userId, summary, summaryHour, alerts, checkin, checkinHour });
    }

    public async Task MonthlyConfigAsync(int userId, string moment, string hour = "08:00", bool active = true)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO monthlyreportconfigs (usuario_id, ativo, momento_envio, hora_envio) VALUES (@userId, @active, @moment, @hour::time)",
            new { userId, active, moment, hour });
    }

    public async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<T>(sql, args)).AsList();
    }

    public async Task ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }
}
