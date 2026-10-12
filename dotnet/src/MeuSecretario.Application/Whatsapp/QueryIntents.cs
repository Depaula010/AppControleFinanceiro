using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Bills;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Llm;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>Leituras das consultas financeiras da 012c (spec 012 §6.c); Infrastructure implementa com Dapper.</summary>
public interface IWhatsAppQueryReader
{
    /// <summary><c>get_saldo_contas</c>: contas ativas, saldo consolidado, <c>ORDER BY tipo_conta, nome_conta</c>.</summary>
    Task<IReadOnlyList<AccountBalance>> GetBalancesAsync(int userId, int? accountId, CancellationToken cancellationToken);

    /// <summary><c>get_pote_status</c>: potes ativos por nome, cada um com o gasto do seu período (desvio 015).</summary>
    Task<IReadOnlyList<PotLine>> GetPotsAsync(int userId, DateOnly today, CancellationToken cancellationToken);

    /// <summary><c>get_fatura_valor</c> (depois do <c>EnsureCurrent</c>): faturas abertas; com cartão, só a mais antiga.</summary>
    Task<IReadOnlyList<InvoiceSummary>> GetOpenInvoicesAsync(int userId, int? cardId, CancellationToken cancellationToken);

    /// <summary><c>get_fatura_detalhada</c> (depois do <c>EnsureCurrent</c>).</summary>
    Task<InvoiceDetail?> GetInvoiceDetailAsync(int userId, int cardId, CancellationToken cancellationToken);

    /// <summary><c>update_saldo_inicial</c>: <c>true</c> se alguma linha mudou.</summary>
    Task<bool> UpdateInitialBalanceAsync(int userId, int accountId, decimal value, CancellationToken cancellationToken);

    /// <summary><c>get_category_spending</c> sem o sinal trocado: soma bruta das despesas da categoria no mês de <paramref name="today"/>.</summary>
    Task<double> GetCategorySpendingAsync(int userId, string categoryName, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<PeriodExpense>> ListExpensesAsync(int userId, DateOnly start, DateOnly last, CancellationToken cancellationToken);

    Task<IReadOnlyList<CategoryExpense>> ListCategoryExpensesAsync(int userId, string category, DateOnly start, DateOnly last, CancellationToken cancellationToken);
}

internal static class QueryJson
{
    public static JsonElement Object(string text)
    {
        var root = LlmJson.Parse(text);
        return root.ValueKind == JsonValueKind.Object ? root : throw new InvalidOperationException("Resposta de extração não é um objeto.");
    }

    public static JsonElement? Get(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value : null;

    public static string TextOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw new InvalidOperationException("Valor não é texto.");
}

/// <summary><c>Consulta Saldo</c> (inline).</summary>
public sealed class BalanceIntent(ILlmClient llm, IAutomationLookups lookups, IWhatsAppFinanceReader finance, IWhatsAppQueryReader reader) : IWhatsAppIntent
{
    public string Name => "Consulta Saldo";

    public bool Swallows => false;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var userId = context.User.Id;
        var accounts = await lookups.ListActiveAccountsAsync(userId, cancellationToken);
        var raw = await llm.GenerateTextAsync(
            new LlmRequest(WhatsAppPrompts.BalanceQuery(context.Message, AccountsJson.Dump(accounts, withId: false)), userId), cancellationToken);
        var query = QueryJson.Object(raw);

        int? accountId = null;
        if (QueryJson.Get(query, "nome_conta") is { } name && PyJsonValue.IsTruthy(name))
        {
            var text = QueryJson.TextOf(name);
            accountId = await finance.FindAccountIdByNameAsync(userId, text, fallback: false, type: null, cancellationToken);
            if (accountId is null)
            {
                return BalanceMessages.NotFound(text);
            }
        }

        return BalanceMessages.Format(await reader.GetBalancesAsync(userId, accountId, cancellationToken));
    }
}

/// <summary><c>Consulta Reserva</c> (inline; desvio 014: <see cref="IEmergencyReserveService"/>).</summary>
public sealed class ReserveIntent(IEmergencyReserveService reserve) : IWhatsAppIntent
{
    public string Name => "Consulta Reserva";

    public bool Swallows => false;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var status = await reserve.GetStatusAsync(context.User.Id, cancellationToken);
        return ReserveMessages.Summary(status.MonthlyEquivalent, status.Total, status.Months);
    }
}

/// <summary><c>Consulta Detalhes Reserva</c> (inline).</summary>
public sealed class ReserveDetailsIntent(IEmergencyReserveService reserve) : IWhatsAppIntent
{
    public string Name => "Consulta Detalhes Reserva";

    public bool Swallows => false;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var status = await reserve.GetStatusAsync(context.User.Id, cancellationToken);
        var items = await reserve.GetItemsAsync(context.User.Id, cancellationToken);
        if (items.Count == 0)
        {
            return ReserveMessages.NoItems;
        }

        return ReserveMessages.Details(
            status.MonthlyEquivalent, status.Total, status.Months,
            [.. items.Select(i => new ReserveLine(i.Description, i.Amount, i.Periodicity, i.Impact))]);
    }
}

/// <summary><c>Consulta Potes</c> (inline; desvio 015).</summary>
public sealed class PotsIntent(IWhatsAppQueryReader reader, IClock clock) : IWhatsAppIntent
{
    public string Name => "Consulta Potes";

    public bool Swallows => false;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken) =>
        PotMessages.Format(await reader.GetPotsAsync(context.User.Id, clock.Today, cancellationToken));
}

/// <summary><c>Listar Contas</c> (template): contas ativas agrupadas por tipo.</summary>
public sealed class ListAccountsIntent(IAutomationLookups lookups) : IWhatsAppIntent
{
    public string Name => "Listar Contas";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken) =>
        AccountListMessage.Format(await lookups.ListActiveAccountsAsync(context.User.Id, cancellationToken));
}

/// <summary><c>Ajustar Saldo Inicial</c> (template): valor por regex, conta contida na mensagem, <c>UPDATE contas.saldo_inicial</c>.</summary>
public sealed class AdjustBalanceIntent(IAutomationLookups lookups, IWhatsAppQueryReader reader) : IWhatsAppIntent
{
    public string Name => "Ajustar Saldo Inicial";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var userId = context.User.Id;
        var value = AdjustBalance.ParseValue(context.Message);
        var accounts = await lookups.ListActiveAccountsAsync(userId, cancellationToken);
        var account = AdjustBalance.FindAccount(context.Message, accounts);

        if (value is null)
        {
            return AdjustBalance.MissingValue;
        }

        if (account is null)
        {
            return AdjustBalance.NoAccountFound(accounts);
        }

        if (!MoneyDecimal.TryFromDouble(value.Value, out var amount))
        {
            throw new AutomationFailureException("valor fora do numeric(15,2)");
        }

        return await reader.UpdateInitialBalanceAsync(userId, account.Id, amount, cancellationToken)
            ? AdjustBalance.Success(account, value.Value)
            : AdjustBalance.Failed;
    }
}

/// <summary><c>Consulta Valor Fatura</c> (template): fatura resumida ou detalhada de um cartão (ou de todos).</summary>
public sealed class InvoiceValueIntent(
    ILlmClient llm, IAutomationLookups lookups, IWhatsAppFinanceReader finance, IInvoiceService invoices, IWhatsAppQueryReader reader) : IWhatsAppIntent
{
    public string Name => "Consulta Valor Fatura";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var userId = context.User.Id;
        var accounts = await lookups.ListActiveAccountsAsync(userId, cancellationToken);
        var raw = await llm.GenerateTextAsync(
            new LlmRequest(WhatsAppPrompts.InvoiceQuery(context.Message, AccountsJson.Dump(accounts, withId: false)), userId), cancellationToken);
        var query = QueryJson.Object(raw);

        string? cardName = null;
        int? cardId = null;
        if (QueryJson.Get(query, "conta_cartao") is { } card && card.ValueKind != JsonValueKind.Null)
        {
            // O nome volta do Gemini como veio (verdadeiro ou não); só um nome verdadeiro é procurado.
            if (PyJsonValue.IsTruthy(card))
            {
                cardName = PyJsonValue.Str(card);
                cardId = await finance.FindAccountIdByNameAsync(userId, QueryJson.TextOf(card), fallback: false, type: null, cancellationToken);
            }
        }

        var wantsDetails = InvoiceMessages.WantsDetails(context.Message);
        if (!string.IsNullOrEmpty(cardName) && cardId is null)
        {
            return InvoiceMessages.NotFound(cardName);
        }

        if (wantsDetails && cardId is { } id)
        {
            await invoices.EnsureCurrentAsync(userId, id, cancellationToken);
            return InvoiceMessages.Detailed(await reader.GetInvoiceDetailAsync(userId, id, cancellationToken), cardName);
        }

        await invoices.EnsureCurrentAsync(userId, cardId, cancellationToken);
        return InvoiceMessages.Summary(await reader.GetOpenInvoicesAsync(userId, cardId, cancellationToken), cardName);
    }
}

/// <summary><c>Consulta Categoria Específica</c> (template): gasto da categoria no mês de hoje.</summary>
public sealed class CategorySpendIntent(ILlmClient llm, IWhatsAppQueryReader reader, IClock clock) : IWhatsAppIntent
{
    public string Name => "Consulta Categoria Específica";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var raw = await llm.GenerateTextAsync(new LlmRequest(WhatsAppPrompts.CategoryQuery(context.Message), context.User.Id), cancellationToken);
        var query = QueryJson.Object(raw);
        if (QueryJson.Get(query, "nome_categoria") is not { } name || !PyJsonValue.IsTruthy(name))
        {
            return CategorySpendMessage.Missing;
        }

        var category = QueryJson.TextOf(name);
        var raw2 = await reader.GetCategorySpendingAsync(context.User.Id, category, clock.Today, cancellationToken);
        return CategorySpendMessage.Format(category, raw2 * -1.0);
    }
}

/// <summary><c>Consulta Período</c> (template): despesas de um período, com ou sem filtro de categoria.</summary>
public sealed class PeriodIntent(ILlmClient llm, IWhatsAppQueryReader reader, IClock clock) : IWhatsAppIntent
{
    public string Name => "Consulta Período";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var userId = context.User.Id;
        var raw = await llm.GenerateTextAsync(
            new LlmRequest(WhatsAppPrompts.PeriodQuery(context.Message), userId, LlmCachePolicies.PeriodQuery), cancellationToken);
        var query = QueryJson.Object(raw);

        string? periodType = "hoje";
        if (QueryJson.Get(query, "period_type") is { } type)
        {
            periodType = type.ValueKind == JsonValueKind.String ? type.GetString() : null;
        }

        var category = QueryJson.Get(query, "categoria");
        var (start, end, description) = PeriodQuery.Range(periodType, clock.Today);
        if (category is { } filter && PyJsonValue.IsTruthy(filter))
        {
            var name = QueryJson.TextOf(filter);
            var expenses = await reader.ListCategoryExpensesAsync(userId, name, start, end, cancellationToken);
            return PeriodQuery.ForCategory(name, PeriodQuery.Total(expenses.Select(e => e.Value)), expenses, description);
        }

        return PeriodQuery.General(await reader.ListExpensesAsync(userId, start, end, cancellationToken), description);
    }
}
