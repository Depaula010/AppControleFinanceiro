using System.Reflection;
using Npgsql;

namespace MeuSecretario.Infrastructure.Database;

/// <summary>
/// Aplica os scripts de <c>dotnet/migrations/NNNN_descricao.sql</c> (embutidos no assembly) que ainda
/// não estão registrados em <c>dotnet_migrations</c>, em ordem, cada um em uma transação (ADR 0001).
/// Nunca roda no boot da aplicação: só no modo <c>--migrate</c>, na homologação e nos testes.
/// A <c>alembic_version</c> do Flask não é tocada.
/// </summary>
/// <remarks>
/// O <c>0000_baseline</c> cria o banco vazio. Se o banco já tem a tabela <c>usuarios</c> (produção),
/// ele só é registrado como aplicado, sem executar.
/// </remarks>
public sealed class MigrationRunner(NpgsqlDataSource dataSource)
{
    public const string BaselineVersion = "0000_baseline";

    private const long AdvisoryLockKey = 727_301; // evita duas execuções simultâneas
    private const string ResourcePrefix = "migrations/";

    /// <returns>As versões processadas nesta execução (aplicadas ou adotadas), em ordem.</returns>
    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await Execute(connection, "SELECT pg_advisory_lock(" + AdvisoryLockKey + ")", cancellationToken);
        try
        {
            await Execute(
                connection,
                "CREATE TABLE IF NOT EXISTS dotnet_migrations (versao text PRIMARY KEY, aplicada_em timestamptz NOT NULL DEFAULT now())",
                cancellationToken);

            var applied = await LoadApplied(connection, cancellationToken);
            var processed = new List<string>();

            foreach (var (version, script) in LoadEmbeddedScripts())
            {
                if (applied.Contains(version))
                {
                    continue;
                }

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

                var adopt = version == BaselineVersion && await TableExists(connection, "usuarios", cancellationToken);
                if (!adopt)
                {
                    await using var command = new NpgsqlCommand(script, connection, transaction);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var register = new NpgsqlCommand("INSERT INTO dotnet_migrations (versao) VALUES (@v)", connection, transaction))
                {
                    register.Parameters.AddWithValue("v", version);
                    await register.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                processed.Add(version);
            }

            return processed;
        }
        finally
        {
            await Execute(connection, "SELECT pg_advisory_unlock(" + AdvisoryLockKey + ")", CancellationToken.None);
        }
    }

    /// <summary>Texto de uma migration embutida (ex.: <c>0020_categorias_globais</c>); usado pelo reset dos testes.</summary>
    public static string ReadScript(string version) =>
        LoadEmbeddedScripts().Single(m => m.Version == version).Script;

    private static List<(string Version, string Script)> LoadEmbeddedScripts()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return (Path.GetFileNameWithoutExtension(name), reader.ReadToEnd());
            })
            .ToList();
    }

    private static async Task<HashSet<string>> LoadApplied(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT versao FROM dotnet_migrations", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private static async Task<bool> TableExists(NpgsqlConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
        command.Parameters.AddWithValue("name", "public." + table);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
