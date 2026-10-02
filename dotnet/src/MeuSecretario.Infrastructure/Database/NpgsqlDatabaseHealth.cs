using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Configuration;
using Npgsql;

namespace MeuSecretario.Infrastructure.Database;

public sealed class NpgsqlDatabaseHealth(AppSettings settings) : IDatabaseHealth
{
    public async Task<DatabaseStatus> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return new DatabaseStatus(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Só o tipo da exceção: a mensagem do Npgsql pode conter host e usuário (desvio 008).
            return new DatabaseStatus(false, ex.GetType().Name);
        }
    }
}
