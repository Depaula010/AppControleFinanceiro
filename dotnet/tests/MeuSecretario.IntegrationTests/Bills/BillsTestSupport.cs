using System.Net;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.IntegrationTests.Accounts;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Bills;

/// <summary>Linhas de apoio dos testes da feature 005 (usa o seed de categorias da migration 0020 e os helpers da 002 sem editá-los).</summary>
internal sealed class BillsTestData(NpgsqlDataSource dataSource)
{
    /// <summary>Subcategorias globais do seed: 59 = Outros (Despesas Gerais), 60 = Outros (Receitas Gerais).</summary>
    public const int ExpenseSubcategory = 59;

    public const int GeneralIncomeSubcategory = 60;

    public AccountsTestData Accounts { get; } = new(dataSource);

    public async Task<int> InsertUserAsync(string whatsapp = "5511999999999", string nome = "Ana Souza", int? mesesReserva = null, bool nullMonths = false)
    {
        var id = await Accounts.InsertUserAsync(whatsapp, nome);
        if (mesesReserva is not null || nullMonths)
        {
            await ExecuteAsync("UPDATE usuarios SET meses_reserva_emergencia = @mesesReserva WHERE id = @id", new { mesesReserva, id });
        }
        return id;
    }

    public Task<int> InsertAccountAsync(int userId, string nome = "Mercado pago", bool ativa = true) =>
        Accounts.InsertAccountAsync(userId, nome, "Conta Corrente", ativa: ativa);

    /// <summary>Primeira subcategoria global do grupo informado (ex.: <c>Renda</c>, <c>Despesa Essencial</c>).</summary>
    public Task<int> GlobalSubcategoryOfGroupAsync(string group) =>
        ScalarAsync<int>(
            """
            SELECT s.id FROM subcategoria s
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE g.nome_grupo = @group AND s.usuario_id IS NULL
            ORDER BY s.id LIMIT 1
            """,
            new { group });

    /// <summary>Subcategoria própria de um usuário (na macro de <see cref="ExpenseSubcategory"/>).</summary>
    public Task<int> InsertUserSubcategoryAsync(int userId, string nome) =>
        ScalarAsync<int>(
            "INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) SELECT macro_id, @userId, @nome FROM subcategoria WHERE id = @baseId RETURNING id",
            new { userId, nome, baseId = ExpenseSubcategory });

    public async Task<int> InsertScheduleAsync(
        int userId,
        int accountId,
        string descricao = "Conta",
        string tipo = "FIXO",
        string periodicidade = "MENSAL",
        int dia = 5,
        int? mes = null,
        decimal? valor = 100m,
        bool ativo = true,
        bool? incluirNaReserva = null,
        int? totalParcelas = null,
        int parcelasExecutadas = 0,
        int subcategoryId = ExpenseSubcategory,
        string dataInicio = "2026-01-05")
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento,
                                      periodicidade, data_inicio, dia_execucao, mes_execucao, ativo, incluir_na_reserva,
                                      total_parcelas, parcelas_executadas)
            VALUES (@userId, @accountId, @subcategoryId, @descricao, @valor, @tipo, @periodicidade, @dataInicio::date, @dia, @mes,
                    @ativo, @incluirNaReserva, @totalParcelas, @parcelasExecutadas)
            RETURNING id
            """,
            new
            {
                userId, accountId, subcategoryId, descricao, valor, tipo, periodicidade, dataInicio, dia, mes, ativo,
                incluirNaReserva, totalParcelas, parcelasExecutadas,
            });
    }

    public async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    public Task<T> ScalarAsync<T>(string sql, object? parameters = null) => Accounts.ScalarAsync<T>(sql, parameters);

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null) => Accounts.QueryAsync<T>(sql, parameters);
}

/// <summary>Cliente HTTP dos testes de contas mensais (reaproveita o da 002) e fábrica com relógio fixo.</summary>
internal sealed class BillsHttp(PostgresFixture postgres)
{
    public const string Base = "/api/bills";

    public const string Alias = "/api/contas-mensais";

    private readonly AccountsHttp _http = new(postgres);

    public static string Error(string message) => AccountsHttp.Error(message);

    public Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpMethod method, string path, string? json = null, int? userId = null, string? authorization = null) =>
        _http.SendAsync(method, path, json, userId, authorization);

    public Task<(HttpStatusCode Status, string Body)> Get(int userId, string path = Base) => _http.Get(userId, path);

    public Task<(HttpStatusCode Status, string Body)> Post(int userId, string? json, string path = Base) => _http.Post(userId, json, path);

    public Task<(HttpStatusCode Status, string Body)> Put(int userId, long id, string? json, string basePath = Base) =>
        _http.SendAsync(HttpMethod.Put, $"{basePath}/{id}", json, userId);

    public Task<(HttpStatusCode Status, string Body)> Delete(int userId, long id, string basePath = Base) =>
        _http.SendAsync(HttpMethod.Delete, $"{basePath}/{id}", userId: userId);

    /// <summary>API real com o relógio em <paramref name="today"/> (America/Sao_Paulo).</summary>
    public WebApplicationFactory<Program> WithClock(DateOnly today) =>
        postgres.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IClock>(new FixedClock(today))));

    internal sealed class FixedClock(DateOnly today) : IClock
    {
        // 23:30 em São Paulo: em UTC já é o dia seguinte, e a data de negócio continua a de São Paulo.
        public DateTimeOffset UtcNow => new(today.AddDays(1).ToDateTime(new TimeOnly(2, 30)), TimeSpan.Zero);

        public DateTime Now => today.ToDateTime(new TimeOnly(23, 30));

        public DateOnly Today => today;

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
    }
}
