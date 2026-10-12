using System.Text.Json;

namespace MeuSecretario.UnitTests.Domain.Automation;

/// <summary>Referência gerada pelo Python (tests/Fixtures/generate_automation_fixtures.py): Flask e rapidfuzz 3.14.3.</summary>
internal static class AutomationFixture
{
    private static readonly JsonElement Root = Load();

    public static IReadOnlyList<JsonElement> Cases(string section) => [.. Root.GetProperty(section).EnumerateArray()];

    public static JsonElement Section(string name) => Root.GetProperty(name);

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "automation-fixtures.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
