using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>
/// Aviso ao dono quando o webhook do WhatsApp falha por causa do Gemini (desvio 266). Cada causa tem a sua origem, então o limite de um aviso
/// a cada 30 minutos vale por causa. Só texto fixo e o tipo da exceção: nunca a mensagem do usuário, a chave nem o corpo da resposta.
/// </summary>
public static class WebhookFailureAlert
{
    public const string QuotaSource = "WHATSAPP-GEMINI-COTA";
    public const string PermissionSource = "WHATSAPP-GEMINI-CHAVE";
    public const string ModelSource = "WHATSAPP-GEMINI-FALHA";
    public const string OtherSource = "WHATSAPP-WEBHOOK";

    public static (string Source, string Message) Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            LlmQuotaExceededException => (QuotaSource,
                "O Gemini está recusando as chamadas (cota ou saldo esgotados). O bot não entende mensagens livres até resolver; 'menu' e os comandos fixos seguem funcionando. Confira o saldo no Google AI Studio e a chave pessoal em preferenciaschaveapi."),
            LlmPermissionDeniedException => (PermissionSource,
                "O Gemini recusou a chave (permissão negada). Confira o GEMINI_API_KEY do servidor e a chave pessoal do usuário (usar_chave_propria)."),
            LlmException llm when WhatsAppTexts.IsQuotaMessage(llm.Message) => (QuotaSource,
                "O Gemini está recusando as chamadas (cota ou saldo esgotados). Confira o saldo no Google AI Studio."),
            LlmException llm => (ModelSource,
                $"Falha ao chamar o Gemini: {llm.Message} Se for modelo indisponível, troque em: UPDATE configuracoes SET valor = '<modelo>' WHERE chave = 'gemini.modelo'."),
            _ when WhatsAppTexts.IsQuotaMessage(exception.Message) => (QuotaSource,
                "O Gemini está recusando as chamadas (cota ou saldo esgotados). Confira o saldo no Google AI Studio."),
            _ => (OtherSource, $"O webhook do WhatsApp está falhando ({exception.GetType().Name}). Veja o log da API."),
        };
    }
}
