using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// O que <c>extract_income_params</c>/<c>extract_expense_params</c> tiram da resposta do Gemini (<c>gemini_service.py:1724-1889</c>).
/// O prompt só pede <c>valor_decimal</c> e <c>descricao_bruta</c>; <c>data</c>, <c>conta</c> e <c>parcelamento</c> só existem se o
/// modelo os inventar (desvio 154 proposto: portado como está).
/// </summary>
/// <param name="NotNone"><c>valor is not None</c> no Python (<c>false</c>/<c>0</c>/<c>""</c> são "não nulos" mas falsos).</param>
/// <param name="Invalid">Valor verdadeiro que não é número: o Python quebra em <c>valor &lt;= 0</c> (<c>TypeError</c>).</param>
public sealed record ExtractedTransaction(
    double? Value, bool NotNone, bool Invalid, string? Description, string? Date, string? AccountName, JsonElement? Installments)
{
    public static ExtractedTransaction Parse(JsonElement root, string defaultDescription)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            // dict.get num valor que não é dicionário: AttributeError.
            throw new InvalidOperationException("Resposta de extração não é um objeto.");
        }

        double? value = null;
        var notNone = false;
        var invalid = false;
        if (root.TryGetProperty("valor_decimal", out var valueElement) && valueElement.ValueKind != JsonValueKind.Null)
        {
            notNone = true;
            switch (valueElement.ValueKind)
            {
                case JsonValueKind.Number:
                    value = double.Parse(valueElement.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
                    break;
                case JsonValueKind.True:
                    value = 1;
                    break;
                case JsonValueKind.False:
                    value = 0;
                    break;
                default:
                    if (PyJsonValue.IsTruthy(valueElement))
                    {
                        invalid = true;
                    }
                    else
                    {
                        value = 0;
                    }

                    break;
            }
        }

        var description = defaultDescription;
        if (root.TryGetProperty("descricao_bruta", out var descriptionElement))
        {
            description = descriptionElement.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => descriptionElement.GetString(),
                _ => throw new InvalidOperationException("descricao_bruta não é texto."),
            };
        }

        string? date = null;
        if (root.TryGetProperty("data", out var dateElement) && PyJsonValue.IsTruthy(dateElement))
        {
            date = PyJsonValue.Str(dateElement);
        }

        string? account = null;
        if (root.TryGetProperty("conta", out var accountElement) && PyJsonValue.IsTruthy(accountElement))
        {
            account = accountElement.ValueKind == JsonValueKind.String
                ? accountElement.GetString()
                : throw new InvalidOperationException("conta não é texto.");
        }

        JsonElement? installments = root.TryGetProperty("parcelamento", out var installmentsElement) ? installmentsElement.Clone() : null;
        return new ExtractedTransaction(value, notNone, invalid, description, date, account, installments);
    }

    /// <summary>Com o valor trocado pelo previsto do agendamento recorrente.</summary>
    public ExtractedTransaction WithScheduleValue(double expected) => this with { Value = expected, NotNone = true };
}

/// <summary>Resultado da validação de Renda/Despesa: mensagem ao usuário ou <c>null</c> (válido).</summary>
public static class TransactionIntentRules
{
    public const string Income = "Renda";
    public const string Expense = "Despesa";

    public const string TooLow = "❌ Valor muito baixo. Verifique se informou corretamente.";
    public const string InvalidInstallments = "❌ Número de parcelas inválido (deve ser entre 2 e 48).";
    public const string AccountNotFound = "❌ Conta não encontrada. Por favor, especifique a conta.";
    public const string PendingFailed = "❌ Erro ao criar transação pendente";

    public static string MissingValue(string type) =>
        type == Income
            ? "❌ Não consegui identificar o valor da renda. Por favor, informe o valor."
            : "❌ Não consegui identificar o valor da despesa. Por favor, informe o valor.";

    /// <summary><c>validate()</c> de <c>RendaIntent</c>/<c>DespesaIntent</c> (<c>transaction_intents.py:47-56, 178-192</c>).</summary>
    public static string? Validate(string type, ExtractedTransaction p)
    {
        if (p.Invalid)
        {
            throw new InvalidOperationException("Valor extraído não é numérico.");
        }

        if (p.Value is not { } value || value <= 0)
        {
            return MissingValue(type);
        }

        if (value < 0.01)
        {
            return TooLow;
        }

        if (type == Expense && p.Installments is { } installments && PyJsonValue.IsTruthy(installments))
        {
            return IntegerBetween(installments, 2, 48) ? null : InvalidInstallments;
        }

        return null;
    }

    /// <summary><c>isinstance(x, int)</c> (inclui <c>true</c>, que é 1) e a faixa; <c>float</c>, texto e listas não são inteiros.</summary>
    private static bool IntegerBetween(JsonElement element, int min, int max)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = element.GetRawText();
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') >= 0)
        {
            return false;
        }

        return BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
               && number >= min && number <= max;
    }

    /// <summary>Número de parcelas depois de validado (`null` quando não há parcelamento verdadeiro).</summary>
    public static int? InstallmentCount(ExtractedTransaction p) =>
        p.Installments is { ValueKind: JsonValueKind.Number } element && PyJsonValue.IsTruthy(element)
            ? int.Parse(element.GetRawText(), CultureInfo.InvariantCulture)
            : null;

    /// <summary>Tipo de conta que vira <c>tipo_pagamento = "credito"</c> (`'crédito' in conta_tipo.lower()`).</summary>
    public static bool IsCreditAccount(string? accountType) =>
        accountType is not null && accountType.ToLowerInvariant().Contains("crédito", StringComparison.Ordinal);

    /// <summary>
    /// A pendência no Redis (<c>transacao_data</c>, <c>transaction_intents.py:97-114, 258-275</c>), na ordem das chaves do
    /// Flask, mais o modelo da mensagem de confirmação.
    /// </summary>
    public static PendingBuild BuildPending(
        string type, ExtractedTransaction p, int userId, AccountRef account, int categoryId, int? invoiceId, string? scheduleDescription,
        DateOnly today)
    {
        var value = p.Value!.Value;
        var installments = type == Expense ? InstallmentCount(p) : null;
        double? total = null;
        var original = value;
        var valueDb = type == Income ? value : -Math.Abs(value);
        if (installments is > 1)
        {
            total = value;
            original = value / installments.Value;
            valueDb = -Math.Abs(original);
        }

        var isCredit = type == Expense && IsCreditAccount(account.Type);
        var paymentType = isCredit ? "credito" : "debito";
        var date = p.Date ?? today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var data = new JsonObject
        {
            ["usuario_id"] = userId,
            ["conta_id"] = account.Id,
            ["conta_nome"] = account.Name,
            ["conta_tipo"] = account.Type,
            ["categoria_id"] = categoryId,
            ["fatura_id"] = invoiceId,
            ["descricao"] = p.Description,
            ["descricao_final"] = scheduleDescription,
            ["valor_db"] = valueDb,
            ["valor_original"] = original,
            ["valor_total"] = total,
            ["num_parcelas"] = type == Expense && p.Installments is { } raw ? JsonNode.Parse(raw.GetRawText()) : null,
            ["tipo_transacao"] = type,
            ["tipo_pagamento"] = paymentType,
            ["data_transacao"] = date,
            ["origem"] = "whatsapp",
        };

        var message = new PendingTransaction(
            userId, account.Id, categoryId, invoiceId, valueDb, original, type, date, "whatsapp", p.Description,
            Local: null, AccountName: account.Name, AccountType: account.Type, FinalDescription: scheduleDescription, PaymentType: paymentType);
        return new PendingBuild(data, message, installments, total);
    }
}

/// <summary>Pendência pronta para gravar e o modelo da mensagem de confirmação.</summary>
public sealed record PendingBuild(JsonObject Data, PendingTransaction Message, int? Installments, double? Total);
