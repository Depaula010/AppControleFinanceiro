using System.Text.Json;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>Referência gerada pelo Python (tests/Fixtures/generate_whatsapp_fixtures.py): o Flask carregado por texto.</summary>
internal static class WhatsAppFixture
{
    private static readonly JsonElement Root = Load();

    public static IReadOnlyList<JsonElement> Cases(string section) => [.. Root.GetProperty(section).EnumerateArray()];

    public static JsonElement Section(string name) => Root.GetProperty(name);

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whatsapp-fixtures.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
