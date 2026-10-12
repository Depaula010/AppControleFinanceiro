using System.Data;
using Dapper;
using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Bills;
using MeuSecretario.Infrastructure.Database;
using Npgsql;

namespace MeuSecretario.Infrastructure.Bills;

/// <summary>
/// Acesso a <c>agendamentos</c> com Dapper (spec 005). O SQL monta-se só com fragmentos fixos; todo valor vai parametrizado.
/// A coluna de ativo é <c>ativo</c> (em <c>contas</c> é <c>ativa</c>).
/// </summary>
public sealed class BillRepository(IDbConnectionFactory connections) : IBillRepository
{
    // Desvio 075/076: receita = grupo Renda ou macro Receitas Gerais (grupo Geral); o mesmo predicado da 003.
    private const string IncomePredicate = "(g.nome_grupo = 'Renda' OR (g.nome_grupo = 'Geral' AND m.nome_macro = 'Receitas Gerais'))";

    private const string DetailSelect =
        """
        SELECT a.id AS Id, a.descricao AS Description, a.valor_previsto AS Amount, a.tipo_agendamento AS Type,
               a.periodicidade AS Periodicity, a.dia_execucao AS Day, a.mes_execucao AS Month,
               a.notificar_antes_dias AS NotifyDaysBefore, a.subcategoria_id AS SubcategoryId,
               s.nome_sub AS SubcategoryName, a.conta_id AS AccountId, c.nome_conta AS AccountName,
               a.data_inicio AS StartDate, a.incluir_na_reserva AS IncludeInReserve,
               a.total_parcelas AS TotalInstallments, a.parcelas_executadas AS ExecutedInstallments
        FROM agendamentos a
        LEFT JOIN subcategoria s ON a.subcategoria_id = s.id
        LEFT JOIN contas c ON a.conta_id = c.id
        WHERE a.id = @billId AND a.usuario_id = @userId
        """;

    public async Task<IReadOnlyList<BillListItem>> ListActiveAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Sem COLLATE: a ordem é a do banco, como no Flask; o id só desempata.
        var rows = await connection.QueryAsync<ListRow>(new CommandDefinition(
            """
            SELECT a.id AS Id, a.descricao AS Description, a.valor_previsto AS Amount, a.tipo_agendamento AS Type,
                   a.periodicidade AS Periodicity, a.dia_execucao AS Day, a.mes_execucao AS Month,
                   a.notificar_antes_dias AS NotifyDaysBefore, a.subcategoria_id AS SubcategoryId,
                   s.nome_sub AS SubcategoryName, m.nome_macro AS MacroName, g.nome_grupo AS "Group",
                   a.conta_id AS AccountId, c.nome_conta AS AccountName, a.data_inicio AS StartDate,
                   a.incluir_na_reserva AS IncludeInReserve, a.total_parcelas AS TotalInstallments,
                   a.parcelas_executadas AS ExecutedInstallments
            FROM agendamentos a
            LEFT JOIN subcategoria s ON a.subcategoria_id = s.id
            LEFT JOIN macrocategoria m ON s.macro_id = m.id
            LEFT JOIN grupocategoria g ON m.grupo_id = g.id
            LEFT JOIN contas c ON a.conta_id = c.id
            WHERE a.usuario_id = @userId AND a.ativo = true
            ORDER BY a.periodicidade, a.dia_execucao, a.descricao, a.id
            """,
            new { userId },
            cancellationToken: cancellationToken));

        return rows.Select(r => r.ToItem()).ToList();
    }

    public async Task<CurrentBill?> GetCurrentAsync(int userId, int billId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CurrentRow>(new CommandDefinition(
            """
            SELECT tipo_agendamento AS Type, periodicidade AS Periodicity, mes_execucao AS Month,
                   valor_previsto AS Amount, total_parcelas AS TotalInstallments,
                   COALESCE(parcelas_executadas, 0) AS ExecutedInstallments
            FROM agendamentos
            WHERE id = @billId AND usuario_id = @userId AND ativo = true
            """,
            new { billId, userId },
            cancellationToken: cancellationToken));

        return row is null
            ? null
            : new CurrentBill(row.Type, row.Periodicity, row.Month, row.Amount, row.TotalInstallments, row.ExecutedInstallments);
    }

    public async Task<bool> ActiveAccountOwnedAsync(int userId, int accountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM contas WHERE id = @accountId AND usuario_id = @userId AND ativa = true)",
            new { accountId, userId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM subcategoria WHERE id = @subcategoryId AND (usuario_id IS NULL OR usuario_id = @userId))",
            new { subcategoryId, userId },
            cancellationToken: cancellationToken));
    }

    public async Task<BillDetails> CreateAsync(int userId, NewBillData data, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("userId", userId, DbType.Int32);
        parameters.Add("accountId", data.AccountId, DbType.Int32);
        parameters.Add("subcategoryId", data.SubcategoryId, DbType.Int32);
        parameters.Add("description", data.Description, DbType.String);
        parameters.Add("amount", data.Amount, DbType.Decimal);
        parameters.Add("type", data.Type, DbType.String);
        parameters.Add("periodicity", data.Periodicity, DbType.String);
        parameters.Add("startDate", Day(data.StartDate), DbType.Date);
        parameters.Add("day", data.Day, DbType.Int32);
        parameters.Add("month", data.Month, DbType.Int32);
        parameters.Add("notify", data.NotifyDaysBefore, DbType.Int32);
        parameters.Add("include", data.IncludeInReserve, DbType.Boolean);
        parameters.Add("installments", data.TotalInstallments, DbType.Int32);

        // Violações de CHECK ou FK propagam como exceção (o endpoint responde 500 fixo).
        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento,
                                      periodicidade, data_inicio, dia_execucao, mes_execucao, notificar_antes_dias, ativo,
                                      parcelas_executadas, incluir_na_reserva, total_parcelas)
            VALUES (@userId, @accountId, @subcategoryId, @description, @amount, @type, @periodicity, @startDate, @day,
                    @month, @notify, true, 0, @include, @installments)
            RETURNING id
            """,
            parameters,
            cancellationToken: cancellationToken));

        return (await ReadAsync(connection, userId, id, cancellationToken))!;
    }

    public async Task<BillDetails?> UpdateAsync(int userId, int billId, BillChanges changes, CancellationToken cancellationToken)
    {
        var sets = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("billId", billId, DbType.Int32);
        parameters.Add("userId", userId, DbType.Int32);

        if (changes.Description.IsSet)
        {
            sets.Add("descricao = @description");
            parameters.Add("description", changes.Description.Value, DbType.String);
        }
        if (changes.Type.IsSet)
        {
            sets.Add("tipo_agendamento = @type");
            parameters.Add("type", changes.Type.Value, DbType.String);
        }
        if (changes.Periodicity.IsSet)
        {
            sets.Add("periodicidade = @periodicity");
            parameters.Add("periodicity", changes.Periodicity.Value, DbType.String);
        }
        if (changes.Day.IsSet)
        {
            sets.Add("dia_execucao = @day");
            parameters.Add("day", changes.Day.Value, DbType.Int32);
        }
        if (changes.Month.IsSet)
        {
            sets.Add("mes_execucao = @month");
            parameters.Add("month", changes.Month.Value, DbType.Int32);
        }
        if (changes.Amount.IsSet)
        {
            sets.Add("valor_previsto = @amount");
            parameters.Add("amount", changes.Amount.Value, DbType.Decimal);
        }
        if (changes.SubcategoryId.IsSet)
        {
            sets.Add("subcategoria_id = @subcategoryId");
            parameters.Add("subcategoryId", changes.SubcategoryId.Value, DbType.Int32);
        }
        if (changes.AccountId.IsSet)
        {
            sets.Add("conta_id = @accountId");
            parameters.Add("accountId", changes.AccountId.Value, DbType.Int32);
        }
        if (changes.NotifyDaysBefore.IsSet)
        {
            sets.Add("notificar_antes_dias = @notify");
            parameters.Add("notify", changes.NotifyDaysBefore.Value, DbType.Int32);
        }
        if (changes.StartDate.IsSet)
        {
            sets.Add("data_inicio = @startDate");
            parameters.Add("startDate", Day(changes.StartDate.Value), DbType.Date);
        }
        if (changes.IncludeInReserve.IsSet)
        {
            sets.Add("incluir_na_reserva = @include");
            parameters.Add("include", changes.IncludeInReserve.Value, DbType.Boolean);
        }
        if (changes.TotalInstallments.IsSet)
        {
            sets.Add("total_parcelas = @installments");
            parameters.Add("installments", changes.TotalInstallments.Value, DbType.Int32);
        }
        if (changes.ExecutedInstallments.IsSet)
        {
            sets.Add("parcelas_executadas = @executed");
            parameters.Add("executed", changes.ExecutedInstallments.Value, DbType.Int32);
        }

        if (sets.Count == 0)
        {
            throw new ArgumentException("Nenhum campo para atualizar.", nameof(changes));
        }

        await using var connection = await connections.OpenAsync(cancellationToken);

        // O trigger atualiza updated_at. Violações de CHECK/FK propagam (500 fixo no endpoint).
        var updated = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            $"UPDATE agendamentos SET {string.Join(", ", sets)} WHERE id = @billId AND usuario_id = @userId AND ativo = true RETURNING id",
            parameters,
            cancellationToken: cancellationToken));

        return updated is null ? null : await ReadAsync(connection, userId, billId, cancellationToken);
    }

    public async Task<bool> DeactivateAsync(int userId, int billId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE agendamentos SET ativo = false WHERE id = @billId AND usuario_id = @userId AND ativo = true",
            new { billId, userId },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    public async Task<IReadOnlyList<SummaryRow>> ListSummaryRowsAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // RN-005-13: ordem fixa, para a soma de double ser determinística. O GROUP BY por grupo (e não por lado)
        // mantém as mesmas parcelas que o Flask somava.
        var rows = await connection.QueryAsync<SummaryRowDto>(new CommandDefinition(
            $"""
            SELECT a.periodicidade AS Periodicity,
                   CASE WHEN {IncomePredicate} THEN true ELSE false END AS IsIncome,
                   COALESCE(SUM(a.valor_previsto), 0) AS Total
            FROM agendamentos a
            LEFT JOIN subcategoria s ON a.subcategoria_id = s.id
            LEFT JOIN macrocategoria m ON s.macro_id = m.id
            LEFT JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = @userId AND a.ativo = TRUE AND a.valor_previsto IS NOT NULL AND a.valor_previsto > 0
            GROUP BY a.periodicidade, g.nome_grupo, CASE WHEN {IncomePredicate} THEN true ELSE false END
            ORDER BY a.periodicidade, g.nome_grupo, 2
            """,
            new { userId },
            cancellationToken: cancellationToken));

        return rows.Select(r => new SummaryRow(r.Periodicity, r.IsIncome, (double)r.Total)).ToList();
    }

    public async Task<IReadOnlyList<PeriodTotal>> ListReserveTotalsAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Desvio 014: só FIXO e LEMBRETE_VARIAVEL. Desvio 076: receita fora. Sem valor > 0: NULL some na soma, negativo entra.
        var rows = await connection.QueryAsync<PeriodTotalDto>(new CommandDefinition(
            $"""
            SELECT a.periodicidade AS Periodicity, COALESCE(SUM(a.valor_previsto), 0) AS Total
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = @userId AND a.ativo = TRUE AND a.incluir_na_reserva = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND NOT {IncomePredicate}
            GROUP BY a.periodicidade
            ORDER BY a.periodicidade
            """,
            new { userId },
            cancellationToken: cancellationToken));

        return rows.Select(r => new PeriodTotal(r.Periodicity, (double)r.Total)).ToList();
    }

    public async Task<IReadOnlyList<ReserveItemRow>> ListReserveItemsAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ReserveItemDto>(new CommandDefinition(
            $"""
            SELECT a.id AS ScheduleId, a.descricao AS Description, a.valor_previsto AS Amount,
                   a.periodicidade AS Periodicity, s.nome_sub AS Category
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = @userId AND a.ativo = TRUE AND a.incluir_na_reserva = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.valor_previsto IS NOT NULL
              AND NOT {IncomePredicate}
            ORDER BY a.id
            """,
            new { userId },
            cancellationToken: cancellationToken));

        return rows.Select(r => new ReserveItemRow(r.ScheduleId, r.Description, r.Amount, r.Periodicity, r.Category)).ToList();
    }

    public async Task<int> GetReserveMonthsAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT COALESCE(meses_reserva_emergencia, 6) FROM usuarios WHERE id = @userId",
            new { userId },
            cancellationToken: cancellationToken)) ?? 6;
    }

    private static async Task<BillDetails?> ReadAsync(NpgsqlConnection connection, int userId, int billId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<DetailRow>(new CommandDefinition(
            DetailSelect,
            new { billId, userId },
            cancellationToken: cancellationToken));
        return row?.ToDetails();
    }

    /// <summary>O Dapper não conhece <see cref="DateOnly"/>: a data viaja como <see cref="DateTime"/> à meia-noite.</summary>
    private static DateTime Day(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private sealed class ListRow
    {
        public int Id { get; set; }
        public string Description { get; set; } = "";
        public decimal? Amount { get; set; }
        public string Type { get; set; } = "";
        public string Periodicity { get; set; } = "";
        public int Day { get; set; }
        public int? Month { get; set; }
        public int? NotifyDaysBefore { get; set; }
        public int SubcategoryId { get; set; }
        public string? SubcategoryName { get; set; }
        public string? MacroName { get; set; }
        public string? Group { get; set; }
        public int AccountId { get; set; }
        public string? AccountName { get; set; }
        public DateTime StartDate { get; set; }
        public bool? IncludeInReserve { get; set; }
        public int? TotalInstallments { get; set; }
        public int? ExecutedInstallments { get; set; }

        public BillListItem ToItem() => new(
            Id, Description, Amount, Type, Periodicity, Day, Month, NotifyDaysBefore, SubcategoryId, SubcategoryName,
            MacroName, Group, AccountId, AccountName, DateOnly.FromDateTime(StartDate), IncludeInReserve,
            TotalInstallments, ExecutedInstallments);
    }

    private sealed class DetailRow
    {
        public int Id { get; set; }
        public string Description { get; set; } = "";
        public decimal? Amount { get; set; }
        public string Type { get; set; } = "";
        public string Periodicity { get; set; } = "";
        public int Day { get; set; }
        public int? Month { get; set; }
        public int? NotifyDaysBefore { get; set; }
        public int SubcategoryId { get; set; }
        public string? SubcategoryName { get; set; }
        public int AccountId { get; set; }
        public string? AccountName { get; set; }
        public DateTime StartDate { get; set; }
        public bool? IncludeInReserve { get; set; }
        public int? TotalInstallments { get; set; }
        public int? ExecutedInstallments { get; set; }

        public BillDetails ToDetails() => new(
            Id, Description, Amount, Type, Periodicity, Day, Month, NotifyDaysBefore, SubcategoryId, SubcategoryName,
            AccountId, AccountName, DateOnly.FromDateTime(StartDate), IncludeInReserve, TotalInstallments, ExecutedInstallments);
    }

    private sealed class CurrentRow
    {
        public string Type { get; set; } = "";
        public string Periodicity { get; set; } = "";
        public int? Month { get; set; }
        public decimal? Amount { get; set; }
        public int? TotalInstallments { get; set; }
        public int ExecutedInstallments { get; set; }
    }

    private sealed class SummaryRowDto
    {
        public string Periodicity { get; set; } = "";
        public bool IsIncome { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class PeriodTotalDto
    {
        public string Periodicity { get; set; } = "";
        public decimal Total { get; set; }
    }

    private sealed class ReserveItemDto
    {
        public int ScheduleId { get; set; }
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public string Periodicity { get; set; } = "";
        public string? Category { get; set; }
    }
}
