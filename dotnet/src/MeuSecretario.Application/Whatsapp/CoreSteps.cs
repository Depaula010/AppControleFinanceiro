using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// Check-in noturno ativo (<c>whatsapp_handler.py:267-289</c>): palavra de escape apaga a sessão e segue o pipeline; senão a
/// resposta é do responsável pelo check-in (a 013). Sem responsável (ou mensagem não tratada) o pipeline segue.
/// </summary>
public sealed class CheckinStep(ICheckinSession session, INightlyCheckinResponder responder) : IWhatsAppStep
{
    public int Order => StepOrder.Checkin;

    public async Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var checkinId = await session.GetActiveIdAsync(context.Number, cancellationToken);
        if (string.IsNullOrEmpty(checkinId))
        {
            return null;
        }

        if (MessageIntent.HasEscapeKeyword(context.Message))
        {
            await session.ClearActiveAsync(context.Number, cancellationToken);
            return null;
        }

        return await responder.RespondAsync(context.Number, context.Message, checkinId, cancellationToken);
    }
}

/// <summary>
/// <c>cancelar</c> solto sem nada pendente (<c>whatsapp_handler.py:430-442</c>): avisa em vez de mandar ao Gemini.
/// Roda depois da confirmação de transação, então só chega aqui sem <c>last_pending</c> ou com a pendência expirada.
/// </summary>
public sealed class BareCancelStep(IPendingTransactionState transactions, IPendingEventStore events) : IWhatsAppStep
{
    public int Order => StepOrder.BareCancel;

    public async Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (!MessageIntent.IsBareCancel(context.Lower, context.Message))
        {
            return null;
        }

        var hasTransaction = await transactions.GetLastPendingIdAsync(context.Number, cancellationToken) is not null;
        var latest = await events.GetLatestAsync(context.Number, cancellationToken);
        var hasEvent = latest.Data is not null;

        return !hasTransaction && !hasEvent ? WhatsAppTexts.NothingPendingToCancel : null;
    }
}

/// <summary>Intenção <c>Menu de Ajuda</c> (<c>admin_intents.py:MenuAjudaIntent</c>): o menu fixo.</summary>
public sealed class HelpIntent : IWhatsAppIntent
{
    public string Name => "Menu de Ajuda";

    public bool Swallows => true;

    public Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken) =>
        Task.FromResult(WhatsAppPrompts.HelpMenuText());
}
