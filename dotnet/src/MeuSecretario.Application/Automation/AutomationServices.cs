using System.Security.Cryptography;
using System.Text;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Categories;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Llm;

namespace MeuSecretario.Application.Automation;

/// <summary>
/// <c>get_user_by_api_key</c> (spec 011 §3.0): compara cada chave guardada (decifrada se parece Fernet; texto puro legado,
/// desvio 017) com a recebida, em tempo constante. Fernet que não decifra é ignorado (nunca vale o próprio token).
/// </summary>
public sealed class UserApiKeyAuthenticator(IUserKeyReader reader, IEncryptionService encryption) : IUserApiKeyAuthenticator
{
    public async Task<AutomationUser?> AuthenticateAsync(string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var presented = Encoding.UTF8.GetBytes(key);
        foreach (var row in await reader.ListAsync(cancellationToken))
        {
            string candidate;
            if (encryption.LooksEncrypted(row.StoredKey))
            {
                try
                {
                    candidate = encryption.Decrypt(row.StoredKey);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    continue;
                }
            }
            else
            {
                candidate = row.StoredKey;
            }

            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), presented))
            {
                return new AutomationUser(row.UserId, row.WhatsApp);
            }
        }

        return null;
    }
}

/// <summary>Categoria escolhida pelo Gemini e a lista oferecida (a mensagem de confirmação precisa dela).</summary>
public sealed record Categorization(int CategoryId, IReadOnlyList<CategoryOption> Options);

/// <summary>
/// <c>get_user_categories</c> + <c>get_fallback_category_id</c> + <c>categorize_transaction</c> (spec 011 §3.1 passo 3).
/// Resposta bloqueada, vazia, não numérica ou fora da lista vira o <c>Outros</c> de fallback; erro de chave, cota ou
/// permissão do Gemini sobe para a rota (RN-011-05).
/// </summary>
public sealed class TransactionCategorizer(IAutomationLookups lookups, ICategoryLookup categories, ILlmClient llm)
{
    public async Task<Categorization> CategorizeAsync(
        int userId, bool income, string? description, string transactionType, CancellationToken cancellationToken)
    {
        var options = await lookups.ListCategoriesAsync(userId, income, cancellationToken);
        var fallback = await categories.FindFallbackAsync(income, cancellationToken);
        var prompt = AutomationPrompts.Categorize(options, description, transactionType, fallback);

        string text;
        try
        {
            text = await llm.GenerateTextAsync(new LlmRequest(prompt, userId, LlmCachePolicies.Category), cancellationToken);
        }
        catch (LlmBlockedException)
        {
            return Result(fallback, options);
        }

        var chosen = ParseId(text);
        var id = chosen is { } value && options.Any(o => o.Id == value) ? value : fallback;
        return Result(id, options);
    }

    private static Categorization Result(int? id, IReadOnlyList<CategoryOption> options) =>
        new(id ?? throw new InvalidOperationException("Categoria 'Outros' de fallback não encontrada."), options);

    /// <summary><c>int(response_text.strip().replace("`", ""))</c>; qualquer falha devolve <c>null</c> (usa o fallback).</summary>
    private static int? ParseId(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var cleaned = text.Trim().Replace("`", string.Empty, StringComparison.Ordinal);
        return PyInt.TryParse(cleaned, 0, out var number) && number >= int.MinValue && number <= int.MaxValue
            ? (int)number
            : null;
    }
}

/// <summary>
/// Fatura do ciclo de hoje para a pendência: <c>ensure_current_invoice_exists</c> e <c>get_or_create_fatura</c> comitados
/// antes de seguir (spec 011 §3.1 passo 4; o Flask faz <c>conn.commit()</c> aqui). Devolve <c>null</c> sem cartão com os dois dias.
/// </summary>
public sealed class CurrentInvoiceResolver(IInvoiceService invoices, ITransactionsUnitOfWork unitOfWork, IClock clock)
{
    public async Task<int?> ResolveAsync(int userId, int accountId, CancellationToken cancellationToken)
    {
        await invoices.EnsureCurrentAsync(userId, accountId, cancellationToken);

        await using var session = await unitOfWork.BeginAsync(cancellationToken);
        var account = await session.FindActiveAccountAsync(userId, accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var resolved = await InvoiceLinking.ResolveAsync(session, account, clock.Today, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return resolved?.Id;
    }
}

/// <summary><c>validate_budget</c> (spec 011 §7.6): só avisa; nunca bloqueia (desvio 015).</summary>
public sealed class BudgetValidator(IBudgetReader reader)
{
    public async Task<BudgetCheck> ValidateAsync(int userId, int subcategoryId, double amount, DateOnly date, CancellationToken cancellationToken)
    {
        var pots = await reader.ListPotsForSubcategoryAsync(userId, subcategoryId, cancellationToken);
        if (pots.Count == 0)
        {
            return BudgetCheck.None;
        }

        var spends = new List<(PotRow Pot, double Spent)>();
        foreach (var pot in pots)
        {
            var (start, end) = BudgetPeriods.For(pot.Periodicity, date);
            var spent = await reader.SumPotSpendAsync(userId, pot.Id, start, end, cancellationToken);
            spends.Add((pot, (double)Math.Abs(spent)));
        }

        return BudgetValidation.Evaluate(spends, amount);
    }
}

/// <summary>
/// <c>process_invoice_schedules</c> (spec 011 §7.7) numa transação do banco. Erro de um agendamento derruba tudo (500 na rota;
/// o Flask engole o erro e a transação aborta, desvio 101). Idempotente por (fatura, agendamento).
/// </summary>
public sealed class InvoiceScheduleProcessor(IInvoiceScheduleUnitOfWork unitOfWork) : IInvoiceScheduleProcessor
{
    public async Task<InvoiceScheduleStats> ProcessAsync(int invoiceId, int cardAccountId, DateOnly referenceDate, CancellationToken cancellationToken)
    {
        await using var session = await unitOfWork.BeginAsync(cancellationToken);

        var processed = 0;
        var fixedCount = 0;
        var installments = 0;
        var reminders = 0;
        var details = new List<InvoiceScheduleDetail>();

        foreach (var schedule in await session.ListSchedulesAsync(cardAccountId, referenceDate.Month, cancellationToken))
        {
            if (await session.TransactionExistsAsync(invoiceId, schedule.ScheduleId, cancellationToken))
            {
                continue;
            }

            var isInstallment = schedule.ScheduleType == InvoiceScheduleRules.Installment;
            if (isInstallment && schedule.InstallmentsExecuted is null)
            {
                // O Flask soma None + 1 ao montar a descrição (TypeError engolido): o agendamento é pulado.
                continue;
            }

            var income = schedule.GroupName == "Renda";
            var description = InvoiceScheduleRules.Description(
                schedule.Description, schedule.ScheduleType, schedule.InstallmentsExecuted.GetValueOrDefault(), schedule.TotalInstallments);

            await session.InsertTransactionAsync(
                new NewScheduledTransaction(
                    schedule.UserId, cardAccountId, schedule.SubcategoryId, invoiceId, schedule.ScheduleId, description,
                    InvoiceScheduleRules.SignedAmount(schedule.ExpectedAmount, income),
                    income ? "Renda" : "Despesa",
                    InvoiceScheduleRules.TransactionDate(referenceDate, schedule.DayOfMonth)),
                cancellationToken);

            if (isInstallment)
            {
                var next = schedule.InstallmentsExecuted!.Value + 1;
                await session.AdvanceInstallmentAsync(
                    schedule.ScheduleId, next, !InvoiceScheduleRules.IsLastInstallment(next, schedule.TotalInstallments), cancellationToken);
            }

            processed++;
            switch (schedule.ScheduleType)
            {
                case InvoiceScheduleRules.Fixed:
                    fixedCount++;
                    break;
                case InvoiceScheduleRules.Installment:
                    installments++;
                    break;
                case InvoiceScheduleRules.Reminder:
                    reminders++;
                    break;
            }

            details.Add(new InvoiceScheduleDetail(schedule.ScheduleType, description, InvoiceScheduleRules.DetailAmount(schedule.ExpectedAmount)));
        }

        await session.CommitAsync(cancellationToken);
        return new InvoiceScheduleStats(processed, fixedCount, installments, reminders, details);
    }
}

/// <summary>
/// Populador real das faturas novas (RN-011-14): o <c>EnsureCurrentAsync</c> da 004 o chama depois do commit, como o
/// <c>ensure_current_invoice_exists</c> do Flask. Usa o vencimento como data de referência.
/// </summary>
public sealed class InvoiceSchedulePopulator(IInvoiceScheduleProcessor processor) : IInvoiceSchedulePopulator
{
    public async Task PopulateAsync(int invoiceId, int cardAccountId, DateOnly dueDate, CancellationToken cancellationToken) =>
        await processor.ProcessAsync(invoiceId, cardAccountId, dueDate, cancellationToken);
}
