using Dapper;
using MeuSecretario.Application.Admin;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Admin;

/// <summary>Leituras das rotas de teste da 009c (<c>usuarios</c> e <c>agendamentos</c>). Só <c>SELECT</c>, parametrizado.</summary>
public sealed class AdminTestDataRepository(IDbConnectionFactory connections) : IAdminTestDataRepository
{
    public async Task<TestUser?> FindUserAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            "SELECT nome AS Name, numero_whatsapp AS WhatsappNumber FROM usuarios WHERE id = @userId",
            new { userId },
            cancellationToken: cancellationToken));
        return row is null ? null : new TestUser(row.Name ?? string.Empty, row.WhatsappNumber);
    }

    public async Task<IReadOnlyList<TestBill>> ListBillsForDayAsync(int userId, int dayOfMonth, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<BillRow>(new CommandDefinition(
            """
            SELECT descricao AS Description, valor_previsto AS ExpectedValue
            FROM agendamentos
            WHERE usuario_id = @userId AND ativo = TRUE AND dia_execucao = @dayOfMonth
            LIMIT @limit
            """,
            new { userId, dayOfMonth, limit },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new TestBill(r.Description ?? string.Empty, r.ExpectedValue))];
    }

    private sealed class UserRow
    {
        public string? Name { get; set; }

        public string? WhatsappNumber { get; set; }
    }

    private sealed class BillRow
    {
        public string? Description { get; set; }

        public decimal? ExpectedValue { get; set; }
    }
}
