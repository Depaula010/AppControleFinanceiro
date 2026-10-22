namespace MeuSecretario.Domain.ApiKeys;

/// <summary>Um tipo de consentimento LGPD do catálogo (RN-008-08).</summary>
public sealed record ConsentType(string Type, string Description, bool Required);

/// <summary>
/// Catálogo dos 5 tipos de consentimento, na ordem do Flask (<c>servico_consentimento_lgpd.py</c>).
/// Descrições literais; versão padrão <c>1.0</c>.
/// </summary>
public static class ConsentCatalog
{
    public const string CurrentVersion = "1.0";
    public const string UnknownDescription = "Descrição não disponível";

    public static IReadOnlyList<ConsentType> All { get; } =
    [
        new("uso_dados_pessoais", "Uso de dados pessoais para funcionamento do sistema", true),
        new("armazenamento_chaves_api", "Armazenamento de chaves de API criptografadas", true),
        new("rastreamento_uso", "Rastreamento de uso para billing e analytics", false),
        new("comunicacao_whatsapp", "Envio de notificações via WhatsApp", false),
        new("compartilhamento_terceiros", "Compartilhamento com APIs de terceiros (Gemini, Weather, etc)", false),
    ];

    public static bool IsValid(string? type) => type is not null && All.Any(c => string.Equals(c.Type, type, StringComparison.Ordinal));

    /// <summary>Descrição do catálogo, ou "Descrição não disponível" para tipo fora dele.</summary>
    public static string Description(string type) =>
        All.FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.Ordinal))?.Description ?? UnknownDescription;

    /// <summary>Mensagem do <c>ValueError</c>: a lista sai no formato <c>repr</c> do Python (aspas simples).</summary>
    public static string InvalidTypeMessage(string type) =>
        $"Tipo de consentimento inválido: {type}. Válidos: [{string.Join(", ", All.Select(c => $"'{c.Type}'"))}]";
}
