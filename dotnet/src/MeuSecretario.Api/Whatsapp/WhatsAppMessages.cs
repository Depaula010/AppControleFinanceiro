namespace MeuSecretario.Api.Whatsapp;

/// <summary>Textos de erro do <c>/webhook-whatsapp</c> (spec 012 §3), literais de <c>whatsapp_handler.py, 1556-1575</c>.</summary>
public static class WhatsAppMessages
{
    public const string DatabaseUnavailable = "Banco de dados temporariamente indisponível";
    public const string NotConfigured = "Serviço não configurado";
    public const string InvalidSignature = "Assinatura inválida";
    public const string Unauthorized = "Não autorizado";
    public const string MissingData = "Dados faltando";
    public const string ProcessingFailed = "Erro ao processar.";
}
