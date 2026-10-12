using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Categories;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Llm;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// <c>FixedBillsService.settle_fixed_bill</c> (<c>fixed_bills_service.py:150-225</c>): despesa na conta e na subcategoria do
/// agendamento, com a descrição dele, pela porta de lançamento da 004. Agendamento inexistente lança.
/// </summary>
public sealed class FixedBillSettler(IAutomationLookups lookups, DirectPoster poster) : IFixedBillSettler
{
    public async Task<int> SettleAsync(int userId, int scheduleId, double amount, DateOnly day, CancellationToken cancellationToken)
    {
        var target = await lookups.FindSettleTargetAsync(userId, scheduleId, cancellationToken)
                     ?? throw new AutomationFailureException("agendamento não encontrado");
        var posted = await poster.PostAsync(
            userId, target.AccountId, target.SubcategoryId, TransactionKind.Expense, amount, target.Description, day, cancellationToken);
        return posted.TransactionId;
    }
}

/// <summary>
/// Passo 8 do pipeline (<c>whatsapp_handler.py:444-705</c>, spec 012 §6.b): "paguei", "quitei", "liquidei", "saldei" ou "zerei" em
/// qualquer parte do texto disparam o fluxo antes do Gemini classificar a intenção. Cada item vira quitação de conta fixa
/// (casamento difuso) ou despesa avulsa; o erro de um item não impede os seguintes (uma transação do banco por lançamento).
/// </summary>
public sealed class PaymentStep(
    ILlmClient llm,
    IAutomationLookups lookups,
    ICategoryLookup categoryLookup,
    TransactionCategorizer categorizer,
    AccountResolver resolver,
    IFixedBillSettler settler,
    DirectPoster poster,
    IClock clock,
    IFinanceDiagnostics diagnostics) : IWhatsAppStep
{
    public int Order => StepOrder.Payment;

    public async Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (!MessageIntent.HasPaymentWord(context.Message))
        {
            return null;
        }

        var userId = context.User.Id;
        var raw = await llm.GenerateTextAsync(new LlmRequest(WhatsAppPrompts.PaymentItems(context.Message), userId), cancellationToken);
        var extraction = PaymentExtraction.Parse(LlmJson.Parse(raw));
        if (extraction.Items.Count == 0)
        {
            return PaymentReply.NothingRecognized;
        }

        var bills = await lookups.ListFixedBillsAsync(userId, cancellationToken);
        var settled = new List<SettledBill>();
        var expenses = new List<CreatedExpense>();
        var missing = new List<string>();
        var single = extraction.Items.Count == 1;

        foreach (var item in extraction.Items)
        {
            var match = BillMatching.FindBest(BillMatching.CleanDescription(item), bills);
            if (match is not null)
            {
                double? amount = extraction.HasTotal && single
                    ? extraction.TotalValue
                    : (match.ExpectedAmount is { } expected && expected != 0 ? (double)expected : null);
                if (amount is null or 0)
                {
                    missing.Add(match.Description);
                    continue;
                }

                try
                {
                    await settler.SettleAsync(userId, match.ScheduleId, amount.Value, clock.Today, cancellationToken);
                    settled.Add(new SettledBill(
                        match.Description, amount.Value, match.ExpectedAmount is { } e && e != 0 ? (double)e : 0, match.ScheduleType, match.Category));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    diagnostics.PaymentItemFailed(ex.GetType().Name);
                }

                continue;
            }

            if (!(extraction.HasTotal && single))
            {
                missing.Add(item);
                continue;
            }

            try
            {
                var created = await CreateExpenseAsync(context, item, extraction.TotalValue!.Value, cancellationToken);
                if (created is null)
                {
                    missing.Add(item);
                }
                else
                {
                    expenses.Add(created);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.PaymentItemFailed(ex.GetType().Name);
            }
        }

        return PaymentReply.Build(settled, expenses, missing, extraction.TriggerWord);
    }

    /// <summary>Despesa avulsa; <c>null</c> = sem categoria <c>Outros</c> ou sem conta (o item vai para os "sem valor").</summary>
    private async Task<CreatedExpense?> CreateExpenseAsync(PipelineContext context, string item, double value, CancellationToken cancellationToken)
    {
        var userId = context.User.Id;
        var options = await lookups.ListCategoriesAsync(userId, income: false, cancellationToken);
        var fallback = await categoryLookup.FindFallbackAsync(false, cancellationToken);
        if (fallback is null)
        {
            return null;
        }

        var categorization = await categorizer.ChooseAsync(userId, options, fallback, item, TransactionIntentRules.Expense, cancellationToken);
        var account = await resolver.ChooseAsync(userId, context.Message, TransactionIntentRules.Expense, cancellationToken);
        if (account is null)
        {
            return null;
        }

        await poster.PostAsync(
            userId, account.Account.Id, categorization.CategoryId, TransactionKind.Expense, value, item, clock.Today, cancellationToken);
        var categoryName = options.FirstOrDefault(c => c.Id == categorization.CategoryId)?.SubName ?? "Outros";
        return new CreatedExpense(item, value, categoryName, account.Account.Name);
    }
}
