using MeuSecretario.Application.Automation;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>Conta escolhida e de onde veio (<c>agendamento</c>, <c>mencionada</c>, <c>padrao</c> ou <c>fallback</c>).</summary>
public sealed record ResolvedAccount(AccountRef Account, string Origin);

/// <summary>
/// <c>resolve_account_for_transaction</c> e <c>choose_account_for_transaction</c> (<c>finance_service.py</c>, spec 012 §7.6):
/// conta do agendamento, conta citada, conta padrão do tipo, primeira conta. Só contas <b>ativas</b> (desvio 039).
/// </summary>
public sealed class AccountResolver(IAutomationLookups lookups, IWhatsAppFinanceReader reader)
{
    /// <param name="scheduleAccountId">Conta do agendamento recorrente (prioridade máxima), se achou algum.</param>
    /// <param name="accountNameParam">Nome informado pelo Gemini: se verdadeiro, a menção é procurada <b>nele</b> e não na mensagem.</param>
    public async Task<ResolvedAccount?> ResolveAsync(
        int userId, string transactionType, string? message, string? accountNameParam, int? scheduleAccountId, CancellationToken cancellationToken)
    {
        if (scheduleAccountId is { } scheduleAccount)
        {
            var fromSchedule = await reader.FindActiveAccountAsync(userId, scheduleAccount, cancellationToken);
            if (fromSchedule is not null)
            {
                return new ResolvedAccount(fromSchedule, "agendamento");
            }
        }

        var source = !string.IsNullOrEmpty(accountNameParam) ? accountNameParam : message;
        return await ChooseFromAsync(userId, transactionType, source, cancellationToken);
    }

    /// <summary><c>choose_account_for_transaction</c>: menção na mensagem, padrão, primeira.</summary>
    public Task<ResolvedAccount?> ChooseAsync(int userId, string message, string transactionType, CancellationToken cancellationToken) =>
        ChooseFromAsync(userId, transactionType, message, cancellationToken);

    private async Task<ResolvedAccount?> ChooseFromAsync(int userId, string transactionType, string? text, CancellationToken cancellationToken)
    {
        IReadOnlyList<AccountRef>? accounts = null;
        if (!string.IsNullOrEmpty(text) && AccountMention.ExtractName(text) is not null)
        {
            accounts = await lookups.ListActiveAccountsAsync(userId, cancellationToken);
            var mentioned = AccountMention.Match(text, accounts);
            if (mentioned is not null)
            {
                return new ResolvedAccount(mentioned, "mencionada");
            }
        }

        var defaults = await reader.GetDefaultAccountsAsync(userId, cancellationToken);
        var defaultId = transactionType switch
        {
            TransactionIntentRules.Income => defaults.IncomeAccountId,
            TransactionIntentRules.Expense => defaults.ExpenseAccountId,
            _ => null,
        };
        if (defaultId is { } id)
        {
            var account = await reader.FindActiveAccountAsync(userId, id, cancellationToken);
            if (account is not null)
            {
                return new ResolvedAccount(account, "padrao");
            }
        }

        accounts ??= await lookups.ListActiveAccountsAsync(userId, cancellationToken);
        return accounts.Count > 0 ? new ResolvedAccount(accounts[0], "fallback") : null;
    }
}
