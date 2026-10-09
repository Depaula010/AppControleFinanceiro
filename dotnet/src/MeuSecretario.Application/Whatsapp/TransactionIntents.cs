using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Llm;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// <c>RendaIntent</c> e <c>DespesaIntent</c> (<c>transaction_intents.py:22-309</c>, spec 012 §6.a): extrai valor e descrição, valida,
/// escolhe conta e categoria e grava a <b>pendência</b> de confirmação no Redis. Intenção "template": qualquer exceção vira
/// <see cref="WhatsAppTexts.IntentFailed"/> no <see cref="IntentRouter"/>.
/// </summary>
public abstract class TransactionIntent(
    string type,
    ILlmClient llm,
    IWhatsAppFinanceReader reader,
    IAutomationLookups lookups,
    TransactionCategorizer categorizer,
    AccountResolver resolver,
    IInvoiceForDate invoices,
    IPendingTransactionStore pending,
    IClock clock) : IWhatsAppIntent
{
    public string Name => type;

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var userId = context.User.Id;
        var income = type == TransactionIntentRules.Income;

        // extract_income_params / extract_expense_params (gemini_service.py:1724-1889).
        var raw = await llm.GenerateTextAsync(
            new LlmRequest(AutomationPrompts.ExtractTransactionDetails(context.Message, type), userId, LlmCachePolicies.ExtractTransaction),
            cancellationToken);
        var extracted = ExtractedTransaction.Parse(LlmJson.Parse(raw), type);

        int? scheduleAccountId = null;
        int? scheduleSubcategoryId = null;
        string? scheduleDescription = null;
        if (!extracted.NotNone && !string.IsNullOrEmpty(extracted.Description))
        {
            var schedule = await reader.FindRecurringByDescriptionAsync(userId, extracted.Description, cancellationToken);
            if (schedule is { ExpectedAmount: { } expected } && expected != 0)
            {
                extracted = extracted.WithScheduleValue((double)expected);
                scheduleAccountId = schedule.AccountId;
                scheduleSubcategoryId = schedule.SubcategoryId;
                scheduleDescription = schedule.Description;
            }
        }

        var invalid = TransactionIntentRules.Validate(type, extracted);
        if (invalid is not null)
        {
            return invalid;
        }

        var resolved = await resolver.ResolveAsync(userId, type, context.Message, extracted.AccountName, scheduleAccountId, cancellationToken);
        if (resolved is null)
        {
            return TransactionIntentRules.AccountNotFound;
        }

        IReadOnlyList<CategoryOption> options;
        int categoryId;
        if (scheduleSubcategoryId is { } scheduleCategory)
        {
            options = await lookups.ListCategoriesAsync(userId, income, cancellationToken);
            categoryId = scheduleCategory;
        }
        else
        {
            var categorization = await categorizer.CategorizeAsync(userId, income, extracted.Description, type, cancellationToken);
            options = categorization.Options;
            categoryId = categorization.CategoryId;
        }

        int? invoiceId = null;
        if (!income && TransactionIntentRules.IsCreditAccount(resolved.Account.Type))
        {
            if (extracted.Date is not null)
            {
                // O Flask passa o texto da data a get_or_create_fatura, que lê .day (AttributeError).
                throw new InvalidOperationException("Data da transação não é uma data.");
            }

            invoiceId = await invoices.ResolveAsync(userId, resolved.Account.Id, clock.Today, cancellationToken);
        }

        var build = TransactionIntentRules.BuildPending(type, extracted, userId, resolved.Account, categoryId, invoiceId, scheduleDescription, clock.Today);
        if (string.IsNullOrEmpty(context.Number))
        {
            return "❌ Erro interno: número WhatsApp não disponível";
        }

        var data = build.Data.ToDictionary(entry => entry.Key, entry => (object?)entry.Value);
        var transactionId = await pending.CreateAsync(context.Number, data, cancellationToken);
        return transactionId is null
            ? TransactionIntentRules.PendingFailed
            : ConfirmationMessage.Build(build.Message, options, transactionId, build.Installments, build.Total);
    }
}

public sealed class IncomeIntent(
    ILlmClient llm,
    IWhatsAppFinanceReader reader,
    IAutomationLookups lookups,
    TransactionCategorizer categorizer,
    AccountResolver resolver,
    IInvoiceForDate invoices,
    IPendingTransactionStore pending,
    IClock clock)
    : TransactionIntent(TransactionIntentRules.Income, llm, reader, lookups, categorizer, resolver, invoices, pending, clock);

public sealed class ExpenseIntent(
    ILlmClient llm,
    IWhatsAppFinanceReader reader,
    IAutomationLookups lookups,
    TransactionCategorizer categorizer,
    AccountResolver resolver,
    IInvoiceForDate invoices,
    IPendingTransactionStore pending,
    IClock clock)
    : TransactionIntent(TransactionIntentRules.Expense, llm, reader, lookups, categorizer, resolver, invoices, pending, clock);
