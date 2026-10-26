using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// "menu" e "ajuda" (desvio 265): responde o menu fixo sem chamar o Gemini, então ele funciona mesmo com a cota ou o saldo do Gemini
/// esgotados. É o mesmo texto da intenção <c>Menu de Ajuda</c>, que o Gemini devolvia para essas palavras.
/// </summary>
public sealed class HelpCommandStep : IWhatsAppStep
{
    public int Order => StepOrder.Help;

    public Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(HelpCommand.IsHelpRequest(context.Message) ? WhatsAppPrompts.HelpMenuText() : null);
    }
}
