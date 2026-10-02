using Npgsql;

namespace MeuSecretario.Infrastructure.Database;

/// <summary>Abre conexões com o PostgreSQL (usado pelos repositórios Dapper).</summary>
public interface IDbConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default);
}

public sealed class NpgsqlConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default) =>
        await dataSource.OpenConnectionAsync(cancellationToken);
}
