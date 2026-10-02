using System.Text.Json;

namespace MeuSecretario.UnitTests.Fixtures;

/// <summary>
/// Fixtures geradas pelo Python (tests/Fixtures/generate_auth_fixtures.py): hashes do Werkzeug,
/// tokens Fernet e JWTs do PyJWT. Valores fictícios.
/// </summary>
public static class AuthFixtures
{
    private static readonly JsonElement Root = Load();

    public sealed record PasswordHash(string Password, string Hash, string Kind);

    public sealed record FernetCase(string Plaintext, string Token, long Timestamp);

    public static IReadOnlyList<PasswordHash> Passwords { get; } =
        Root.GetProperty("passwords").EnumerateArray()
            .Select(e => new PasswordHash(
                e.GetProperty("password").GetString()!,
                e.GetProperty("hash").GetString()!,
                e.GetProperty("kind").GetString()!))
            .ToList();

    public static string FernetKey => Root.GetProperty("fernet").GetProperty("key").GetString()!;

    public static string FernetTamperedToken => Root.GetProperty("fernet").GetProperty("tampered_token").GetString()!;

    public static IReadOnlyList<FernetCase> FernetCases { get; } =
        Root.GetProperty("fernet").GetProperty("cases").EnumerateArray()
            .Select(e => new FernetCase(
                e.GetProperty("plaintext").GetString()!,
                e.GetProperty("token").GetString()!,
                e.GetProperty("timestamp").GetInt64()))
            .ToList();

    public static string JwtSecret => Root.GetProperty("jwt").GetProperty("secret").GetString()!;

    public static int JwtUserId => Root.GetProperty("jwt").GetProperty("user_id").GetInt32();

    public static string JwtToken(string name) => Root.GetProperty("jwt").GetProperty("tokens").GetProperty(name).GetString()!;

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "auth-fixtures.json");
        using var stream = File.OpenRead(path);
        return JsonDocument.Parse(stream).RootElement.Clone();
    }
}
