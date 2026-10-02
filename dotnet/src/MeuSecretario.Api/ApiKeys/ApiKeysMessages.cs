using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.Api.ApiKeys;

/// <summary>Textos em português do envelope C (spec 008 §4), idênticos ao Flask; os 500 usam o texto fixo da rota (desvio 008).</summary>
internal static class ApiKeysMessages
{
    // ---- erros comuns
    public const string Unauthorized = "Não autorizado";
    public const string IncompleteTitle = "Dados incompletos";
    public const string InvalidProviderTitle = "Provedor inválido";
    public const string InvalidProviderMessage = "Provedor deve ser um de: " + ApiProviders.ValidListText;
    public const string UserRequired = "usuario_id é obrigatório";

    public const string UserNotFoundTitle = "Usuário não encontrado";

    public static string UserNotFound(int userId) => $"Nenhum usuário com ID {userId}";

    /// <summary>Texto fixo dos 500: o título seguido de ". Tente novamente." (decisão G-7).</summary>
    public static string Fixed500(string title) => title + ". Tente novamente.";

    // ---- chaves
    public const string RegisterKeyFailed = "Erro ao cadastrar chave";
    public const string RegisterKeyMissing = "Provedor e chave_api são obrigatórios";
    public const string KeyRegistered = "Chave cadastrada com sucesso";
    public const string KeyDuplicateTitle = "Chave já cadastrada";
    public const string KeyDuplicateMessage = "Já existe uma chave para este provedor. Use PUT /api-keys/chave/<id>.";
    public const string ListKeysFailed = "Erro ao listar chaves";
    public const string UpdateKeyFailed = "Erro ao atualizar chave";
    public const string UpdateKeyMissing = "Forneça pelo menos 'chave_api' ou 'ativo'";
    public const string KeyUpdated = "Chave atualizada com sucesso";
    public const string KeyNotFoundTitle = "Chave não encontrada";

    public static string KeyNotFound(int keyId) => $"Nenhuma chave com ID {keyId}";

    public const string DeleteKeyFailed = "Erro ao deletar chave";
    public const string KeyDeactivated = "Chave desativada com sucesso";

    // ---- preferências
    public const string SetPreferenceFailed = "Erro ao configurar preferência";
    public const string SetPreferenceMissing = "Provedor e usar_chave_propria são obrigatórios";
    public const string PreferenceSaved = "Preferência configurada com sucesso";
    public const string ListPreferencesFailed = "Erro ao listar preferências";
    public const string DeletePreferenceFailed = "Erro ao deletar preferência";
    public const string PreferenceNotFoundTitle = "Preferência não encontrada";

    public static string PreferenceNotFound(string provider) => $"Nenhuma preferência para {provider}";

    public const string PreferenceRemoved = "Preferência removida com sucesso";

    // ---- uso e logs
    public const string UsageFailed = "Erro ao consultar uso";
    public const string LogsFailed = "Erro ao consultar logs";

    // ---- LGPD
    public const string RegisterConsentFailed = "Erro ao registrar consentimento";
    public const string RegisterConsentMissing = "usuario_id, tipo_consentimento e consentimento_dado são obrigatórios";
    public const string ValidationTitle = "Validação";
    public const string ConsentRegistered = "Consentimento registrado com sucesso";
    public const string ListConsentsFailed = "Erro ao listar consentimentos";
    public const string InitialConsentsFailed = "Erro ao solicitar consentimentos";
    public const string InitialConsentsMessage = "Para continuar, você precisa consentir com os termos abaixo";
    public const string ExportFailed = "Erro ao exportar dados";
    public const string DeleteAccountFailed = "Erro ao deletar conta";
    public const string DeleteConfirmationTitle = "Confirmação inválida";

    public const string DeleteConfirmationMessage =
        "Confirmação inválida. Para deletar a conta, você deve passar confirmacao='" + AccountDeletion.Confirmation + "' explicitamente.";

    public static string AccountDeleted(int userId) => $"Conta e todos os dados do usuário {userId} foram deletados com sucesso";

    public const string CheckConsentFailed = "Erro ao verificar consentimento";

    // ---- health
    public const string ServiceName = "API Keys Management";
    public const string ServiceVersion = "1.0.0";
}
