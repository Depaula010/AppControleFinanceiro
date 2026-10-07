using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Llm;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Application.Automation;

/// <summary>
/// Falha que o Flask deixa subir como exceção (500): quebra do <c>float()</c>, conta ausente, lançamento recusado. O texto
/// nunca vai ao cliente nem ao log (desvio 008); só o tipo.
/// </summary>
public sealed class AutomationFailureException(string reason) : Exception(reason);

/// <summary>Lança pelo serviço da 004 e traduz a recusa em exceção (o Flask quebraria no <c>INSERT</c>).</summary>
public sealed class DirectPoster(ITransactionPosting posting)
{
    public async Task<PostedTransaction> PostAsync(
        int userId, int accountId, int categoryId, TransactionKind kind, double amount, string? description, DateOnly date,
        CancellationToken cancellationToken)
    {
        if (description is null)
        {
            throw new AutomationFailureException("descrição ausente");
        }

        if (!MoneyDecimal.TryFromDouble(amount, out var decimalAmount))
        {
            throw new AutomationFailureException("valor fora do numeric(15,2)");
        }

        var result = await posting.PostSimpleAsync(
            new PostSimpleTransaction(userId, accountId, categoryId, kind, decimalAmount, description, date), cancellationToken);
        return result is PostingResult<PostedTransaction>.Ok ok
            ? ok.Value
            : throw new AutomationFailureException("lançamento recusado: " + ((PostingResult<PostedTransaction>.Failed)result).Error);
    }
}

public abstract record AutomateResult
{
    public sealed record NotConfigured : AutomateResult;

    public sealed record MissingData : AutomateResult;

    public sealed record Unauthorized : AutomateResult;

    /// <summary>Gravada direto (Redis fora ou pendência não gravada): <c>{"status":"sucesso"}</c>.</summary>
    public sealed record Saved : AutomateResult;

    public sealed record Pending(string TransactionId) : AutomateResult;
}

/// <param name="Missing"><c>texto</c> ou <c>user_api_key</c> falso.</param>
/// <param name="Text">Nulo quando o texto verdadeiro não é texto (o Flask quebra em <c>.lower()</c>).</param>
public sealed record AutomateInput(bool Missing, string? Key, string? Text);

/// <summary><c>POST /webhook-automate</c> (spec 011 §3.1).</summary>
public sealed class ProcessAutomateNotification(
    IIntegrationSettings settings,
    IUserApiKeyAuthenticator authenticator,
    ILlmClient llm,
    IAutomationLookups lookups,
    TransactionCategorizer categorizer,
    CurrentInvoiceResolver invoices,
    ICache cache,
    IPendingTransactionStore pending,
    DirectPoster poster,
    IBotClient bot,
    IClock clock)
{
    public async Task<AutomateResult> ExecuteAsync(AutomateInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.GeminiApiKey))
        {
            return new AutomateResult.NotConfigured();
        }

        if (input.Missing)
        {
            return new AutomateResult.MissingData();
        }

        var user = await authenticator.AuthenticateAsync(input.Key, cancellationToken);
        if (user is null)
        {
            return new AutomateResult.Unauthorized();
        }

        var text = input.Text ?? throw new AutomationFailureException("texto não é texto");

        var raw = await llm.GenerateTextAsync(new LlmRequest(AutomationPrompts.ExtractFromNotification(text), user.UserId), cancellationToken);
        if (string.IsNullOrEmpty(raw))
        {
            throw new LlmException("Falha na extração (Automate): Resposta vazia do Gemini.");
        }

        var extraction = NotificationParser.Parse(raw) ?? throw new AutomationFailureException("extração inválida");
        var today = clock.Today;

        var accounts = await lookups.ListActiveAccountsAsync(user.UserId, cancellationToken);
        var account = AutomateChoice.ChooseAccount(text, extraction.FlowType, accounts)
                      ?? throw new AutomationFailureException("usuário sem contas ativas");

        var category = await categorizer.CategorizeAsync(user.UserId, extraction.IsIncome, extraction.Description, extraction.FlowType, cancellationToken);

        int? invoiceId = account.Type == AccountTypes.Card
            ? await invoices.ResolveAsync(user.UserId, account.Id, cancellationToken)
            : null;

        var valueDb = extraction.IsIncome ? extraction.Value : -extraction.Value;
        var data = PendingTransaction.FromAutomate(
            user.UserId, account.Id, category.CategoryId, invoiceId, extraction.Description, valueDb, extraction.Value, extraction.FlowType, today);

        if (!await cache.IsAvailableAsync(cancellationToken))
        {
            await SaveDirectAsync(user, account, category, extraction, today, notify: true, cancellationToken);
            return new AutomateResult.Saved();
        }

        var transactionId = await pending.CreateAsync(user.WhatsApp, data.ToData(), cancellationToken);
        if (transactionId is null)
        {
            await SaveDirectAsync(user, account, category, extraction, today, notify: false, cancellationToken);
            return new AutomateResult.Saved();
        }

        await bot.SendTextAsync(user.WhatsApp, ConfirmationMessage.Build(data, category.Options, transactionId), cancellationToken);
        return new AutomateResult.Pending(transactionId);
    }

    private async Task SaveDirectAsync(
        AutomationUser user, AccountRef account, Categorization category, NotificationExtraction extraction, DateOnly today, bool notify,
        CancellationToken cancellationToken)
    {
        var kind = extraction.IsIncome ? TransactionKind.Income : TransactionKind.Expense;
        await poster.PostAsync(user.UserId, account.Id, category.CategoryId, kind, extraction.Value, extraction.Description, today, cancellationToken);
        if (!notify)
        {
            return;
        }

        var label = await lookups.GetCategoryLabelAsync(category.CategoryId, cancellationToken) ?? "Categoria Desconhecida";
        await bot.SendTextAsync(user.WhatsApp, AutomationTexts.AutomateSaved(extraction.Description, extraction.Value, label), cancellationToken);
    }
}

public abstract record ApiTransactionResult
{
    public sealed record NotConfigured : ApiTransactionResult;

    public sealed record InvalidJson : ApiTransactionResult;

    public sealed record Missing(IReadOnlyList<string> Fields) : ApiTransactionResult;

    public sealed record InvalidValue(string Reason) : ApiTransactionResult;

    public sealed record InvalidPaymentType : ApiTransactionResult;

    public sealed record Unauthorized : ApiTransactionResult;

    public sealed record AccountNotFound(string Name) : ApiTransactionResult;

    /// <summary>Redis fora: gravada direto, com a mensagem "(Redis indisponivel)".</summary>
    public sealed record SavedWithoutRedis(string CategoryLabel) : ApiTransactionResult;

    /// <summary>Pendência não gravada: gravada direto, com a mensagem "(erro no Redis)".</summary>
    public sealed record SavedAfterRedisError : ApiTransactionResult;

    public sealed record Pending(string TransactionId, string CategoryLabel, string AccountName, bool LinkedToInvoice) : ApiTransactionResult;
}

/// <summary><c>POST /api/transacao</c> (spec 011 §3.2). <paramref name="Body"/> nulo = corpo ausente, inválido ou que não é objeto.</summary>
public sealed class RegisterApiTransaction(
    IIntegrationSettings settings,
    IUserApiKeyAuthenticator authenticator,
    IAutomationLookups lookups,
    TransactionCategorizer categorizer,
    CurrentInvoiceResolver invoices,
    BudgetValidator budget,
    ICache cache,
    IPendingTransactionStore pending,
    DirectPoster poster,
    IBotClient bot,
    IClock clock)
{
    public async Task<ApiTransactionResult> ExecuteAsync(JsonElement? body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.GeminiApiKey))
        {
            return new ApiTransactionResult.NotConfigured();
        }

        if (body is not { } root)
        {
            return new ApiTransactionResult.InvalidJson();
        }

        var parsed = ApiTransactionInput.Parse(root);
        switch (parsed)
        {
            case ApiTransactionParse.UnexpectedType:
                throw new AutomationFailureException("campo de texto com tipo inesperado");
            case ApiTransactionParse.Missing missing:
                return new ApiTransactionResult.Missing(missing.Fields);
            case ApiTransactionParse.InvalidValue invalid:
                return new ApiTransactionResult.InvalidValue(invalid.Reason);
            case ApiTransactionParse.InvalidPaymentType:
                return new ApiTransactionResult.InvalidPaymentType();
        }

        var input = ((ApiTransactionParse.Ok)parsed).Data;

        var user = await authenticator.AuthenticateAsync(input.UserApiKey, cancellationToken);
        if (user is null)
        {
            return new ApiTransactionResult.Unauthorized();
        }

        var account = await lookups.FindAccountByNameOrTypeAsync(user.UserId, input.AccountName, cancellationToken);
        if (account is null)
        {
            return new ApiTransactionResult.AccountNotFound(input.AccountName);
        }

        var category = await categorizer.CategorizeAsync(
            user.UserId, income: false, input.FinalDescription, TransactionTypes.Expense, cancellationToken);

        int? invoiceId = input.PaymentType == PaymentType.Credit
            ? await invoices.ResolveAsync(user.UserId, account.Id, cancellationToken)
            : null;

        var today = clock.Today;
        var check = await budget.ValidateAsync(user.UserId, category.CategoryId, input.Value, today, cancellationToken);
        var warning = check.RequiresConfirmation ? check.Message : null;

        var data = PendingTransaction.FromApiEndpoint(
            user.UserId, account.Id, account.Name, account.Type, category.CategoryId, invoiceId, input.Local, input.Description,
            input.FinalDescription, -input.Value, input.Value, input.PaymentType, today, warning);

        if (!await cache.IsAvailableAsync(cancellationToken))
        {
            await poster.PostAsync(user.UserId, account.Id, category.CategoryId, TransactionKind.Expense, input.Value, input.FinalDescription, today, cancellationToken);
            var label = await lookups.GetCategoryLabelAsync(category.CategoryId, cancellationToken) ?? "Categoria Desconhecida";
            await bot.SendTextAsync(user.WhatsApp, AutomationTexts.ApiSaved(input.FinalDescription, input.Value, label), cancellationToken);
            return new ApiTransactionResult.SavedWithoutRedis(label);
        }

        var transactionId = await pending.CreateAsync(user.WhatsApp, data.ToData(), cancellationToken);
        if (transactionId is null)
        {
            await poster.PostAsync(user.UserId, account.Id, category.CategoryId, TransactionKind.Expense, input.Value, input.FinalDescription, today, cancellationToken);
            return new ApiTransactionResult.SavedAfterRedisError();
        }

        await pending.SetLastPendingAsync(user.WhatsApp, transactionId, cancellationToken);
        await bot.SendTextAsync(user.WhatsApp, ConfirmationMessage.Build(data, category.Options, transactionId), cancellationToken);
        var categoryLabel = await lookups.GetCategoryLabelAsync(category.CategoryId, cancellationToken) ?? "Categoria Desconhecida";
        return new ApiTransactionResult.Pending(transactionId, categoryLabel, account.Name, invoiceId is not null);
    }
}

public abstract record SmsPaymentResult
{
    public sealed record DatabaseUnavailable : SmsPaymentResult;

    public sealed record MissingData : SmsPaymentResult;

    public sealed record Unauthorized : SmsPaymentResult;

    public sealed record Settled(string BillDescription, int TransactionId) : SmsPaymentResult;

    public sealed record Registered : SmsPaymentResult;
}

/// <summary><c>POST /webhook-sms-payment</c> (spec 011 §3.3). <paramref name="Body"/> nulo = corpo ausente, inválido ou que não é objeto.</summary>
public sealed class ProcessSmsPayment(
    IDatabaseHealth health,
    IUserApiKeyAuthenticator authenticator,
    IAutomationLookups lookups,
    ILlmClient llm,
    TransactionCategorizer categorizer,
    DirectPoster poster,
    IBotClient bot,
    IClock clock)
{
    public async Task<SmsPaymentResult> ExecuteAsync(JsonElement? body, CancellationToken cancellationToken)
    {
        if (!(await health.CheckAsync(cancellationToken)).Connected)
        {
            return new SmsPaymentResult.DatabaseUnavailable();
        }

        JsonElement? Field(string name) => body is { } root && root.TryGetProperty(name, out var value) ? value : null;
        var key = Field("user_api_key");
        var description = Field("descricao_pagamento");
        var amount = Field("valor_pago");
        var paymentAccount = Field("conta_pagamento");
        var dateText = Field("data_pagamento");

        if (!PyJsonValue.IsTruthy(key) || !PyJsonValue.IsTruthy(description) || !PyJsonValue.IsTruthy(amount) || !PyJsonValue.IsTruthy(paymentAccount))
        {
            return new SmsPaymentResult.MissingData();
        }

        var user = await authenticator.AuthenticateAsync(key is { ValueKind: JsonValueKind.String } k ? k.GetString() : null, cancellationToken);
        if (user is null)
        {
            return new SmsPaymentResult.Unauthorized();
        }

        var date = clock.Today;
        if (PyJsonValue.IsTruthy(dateText) && !PyIsoDate.TryParse(dateText!.Value, out date))
        {
            throw new AutomationFailureException("data_pagamento inválida");
        }

        if (description!.Value.ValueKind != JsonValueKind.String)
        {
            throw new AutomationFailureException("descricao_pagamento não é texto");
        }

        var paymentDescription = description.Value.GetString()!;
        var bills = await lookups.ListFixedBillsAsync(user.UserId, cancellationToken);
        var match = BillMatching.FindBest(BillMatching.CleanDescription(paymentDescription), bills);

        return match is not null
            ? await SettleAsync(user, match, amount!.Value, date, cancellationToken)
            : await RegisterLooseAsync(user, paymentDescription, amount!.Value, paymentAccount!.Value, date, cancellationToken);
    }

    private async Task<SmsPaymentResult> SettleAsync(
        AutomationUser user, FixedBill match, JsonElement amount, DateOnly date, CancellationToken cancellationToken)
    {
        var value = ToValue(amount);
        var target = await lookups.FindSettleTargetAsync(user.UserId, match.ScheduleId, cancellationToken)
                     ?? throw new AutomationFailureException("agendamento não encontrado");

        var posted = await poster.PostAsync(
            user.UserId, target.AccountId, target.SubcategoryId, TransactionKind.Expense, value, target.Description, date, cancellationToken);

        await bot.SendTextAsync(
            user.WhatsApp, AutomationTexts.BillSettled(match.Description, FormatPaid(amount), match.Category), cancellationToken);
        return new SmsPaymentResult.Settled(match.Description, posted.TransactionId);
    }

    private async Task<SmsPaymentResult> RegisterLooseAsync(
        AutomationUser user, string description, JsonElement amount, JsonElement paymentAccount, DateOnly date, CancellationToken cancellationToken)
    {
        // O Flask chama o Gemini e ignora o resultado; a falha dele derruba a rota (desvio 096).
        var raw = await llm.GenerateTextAsync(
            new LlmRequest(
                AutomationPrompts.ExtractTransactionDetails($"Paguei {PyJsonValue.Str(amount)} de {description}", TransactionTypes.Expense),
                user.UserId,
                LlmCachePolicies.ExtractTransaction),
            cancellationToken);
        _ = LlmJson.Parse(raw);

        var category = await categorizer.CategorizeAsync(user.UserId, income: false, description, TransactionTypes.Expense, cancellationToken);
        var accountId = await lookups.FindAccountIdWithFallbackAsync(user.UserId, "Carteira", cancellationToken)
                        ?? throw new AutomationFailureException("usuário sem contas ativas");

        var accountText = PyJsonValue.Str(paymentAccount);
        await poster.PostAsync(
            user.UserId, accountId, category.CategoryId, TransactionKind.Expense, ToValue(amount), $"{description} ({accountText})", date, cancellationToken);

        await bot.SendTextAsync(user.WhatsApp, AutomationTexts.SmsRegistered(description, FormatPaid(amount), accountText), cancellationToken);
        return new SmsPaymentResult.Registered();
    }

    /// <summary><c>float(valor_pago)</c>: o que o <c>float()</c> recusa, ou que o banco não guarda, derruba a rota.</summary>
    private static double ToValue(JsonElement amount) =>
        PyJsonValue.ToFloat(amount) is FloatConversion.Ok ok && double.IsFinite(ok.Value)
            ? ok.Value
            : throw new AutomationFailureException("valor_pago inválido");

    /// <summary>
    /// <c>formatar_moeda(valor_pago)</c> com o valor como veio: texto numérico grava mas falha ao formatar, depois do
    /// <c>commit</c> (500 sem aviso; desvio 098).
    /// </summary>
    private static string FormatPaid(JsonElement amount) =>
        amount.ValueKind == JsonValueKind.String
            ? throw new AutomationFailureException("formatar_moeda recebeu texto")
            : MoneyText.Brl(((FloatConversion.Ok)PyJsonValue.ToFloat(amount)).Value);
}
