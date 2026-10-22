using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Automation;

/// <summary>Prompts do Gemini desta feature, copiados byte a byte de <c>gemini_service.py</c> (spec 011 §7.4; recuo de 4 espaços do Python).</summary>
public static class AutomationPrompts
{
    /// <summary><c>extract_from_notification</c> (<c>gemini_service.py</c>); sem cache.</summary>
    public static string ExtractFromNotification(string notificationText) =>
        "\n    Analise a notificação: \"" + notificationText + "\"\n"
        + "    Retorne APENAS JSON com: \"valor_decimal\" (sempre positivo), \"descricao_bruta\", \"tipo_fluxo\" (\"Renda\" ou \"Despesa\").\n"
        + "    ";

    /// <summary><c>categorize_transaction</c> (<c>gemini_service.py</c>); <paramref name="fallbackId"/> nulo vira o texto <c>None</c>.</summary>
    public static string Categorize(IEnumerable<CategoryOption> categories, string? description, string transactionType, int? fallbackId) =>
        "\n    Minhas subcategorias são: " + PythonJson.DumpCategories(categories) + "\n"
        + "    A transação teve a descrição: \"" + (description ?? "None") + "\"; O tipo é: \"" + transactionType + "\"\n"
        + "    Qual é o \"id\" da subcategoria que melhor corresponde? Se for genérico, use o \"id\" de \"Outros\" (que é "
        + (fallbackId is { } id ? id.ToString(CultureInfo.InvariantCulture) : "None") + ").\n"
        + "    Responda APENAS com o número do ID.\n"
        + "    ";

    /// <summary><c>extract_transaction_details</c> (<c>gemini_service.py</c>); cache <c>extract_trans</c>, global.</summary>
    public static string ExtractTransactionDetails(string message, string intent) =>
        "Analise a mensagem: \"" + message + "\"\n"
        + "    O tipo é: \"" + intent + "\".\n"
        + "\n"
        + "    Extraia:\n"
        + "    - \"valor_decimal\": Valor numérico mencionado (ou null se não mencionar)\n"
        + "    - \"descricao_bruta\": APENAS o nome/item principal (não a frase completa)\n"
        + "\n"
        + "    IMPORTANTE para descrição:\n"
        + "    - \"recebi meu salário\" → \"salário\"\n"
        + "    - \"recebi o swile\" → \"swile\"\n"
        + "    - \"paguei a conta de luz\" → \"luz\" ou \"conta de luz\"\n"
        + "    - \"gastei 50 na padaria\" → \"padaria\"\n"
        + "    - \"recebi 500 de freelance\" → \"freelance\"\n"
        + "\n"
        + "    Responda APENAS com JSON.\n"
        + "    Ex: {\"valor_decimal\": 50.00, \"descricao_bruta\": \"Padaria\"}\n"
        + "    Ex: {\"valor_decimal\": null, \"descricao_bruta\": \"salário\"}";
}

/// <summary>Textos de WhatsApp e de resposta fixos das rotas (<c>transaction_handler.py</c>, <c>fixed_bills_service.py</c>).</summary>
public static class AutomationTexts
{
    /// <summary>Automate sem Redis (<c>transaction_handler.py</c>); descrição nula escreve <c>None</c>.</summary>
    public static string AutomateSaved(string? description, double value, string categoryLabel) =>
        $"Transacao salva!\n\nDescricao: {description ?? "None"}\nValor: {MoneyText.Brl(value)}\nCategoria: {categoryLabel}";

    /// <summary><c>/api/transacao</c> sem Redis (<c>transaction_handler.py</c>).</summary>
    public static string ApiSaved(string finalDescription, double value, string categoryLabel) =>
        $"Transacao salva!\n\n{finalDescription}\n{MoneyText.Brl(value)}\n{categoryLabel}";

    /// <summary>Quitação por SMS (<c>fixed_bills_service.py</c>); <paramref name="paid"/> já é o texto do <c>formatar_moeda</c>.</summary>
    public static string BillSettled(string description, string paidFormatted, string category) =>
        "✅ *CONTA QUITADA!* ✅\n\n"
        + $"📝 {description}\n"
        + $"💰 {paidFormatted}\n"
        + $"📊 {category}\n\n"
        + "_Esta conta não será mais cobrada automaticamente este mês._";

    /// <summary>Despesa avulsa por SMS (<c>transaction_handler.py</c>).</summary>
    public static string SmsRegistered(string description, string paidFormatted, string paymentAccount) =>
        "Pagamento Registrado!\n\n"
        + $"{description}\n"
        + $"{paidFormatted}\n"
        + $"{paymentAccount}\n\n"
        + "_Nao encontrei uma conta fixa correspondente, entao registrei como despesa avulsa._";
}

/// <summary>Dados de uma transação aguardando confirmação (<c>transacao_data</c> do Flask, na ordem das chaves).</summary>
public sealed record PendingTransaction(
    int UserId,
    int AccountId,
    int CategoryId,
    int? InvoiceId,
    double ValueDb,
    double ValueOriginal,
    string TransactionType,
    string Date,
    string Origin,
    string? Description,
    string? Local = null,
    string? AccountName = null,
    string? AccountType = null,
    string? FinalDescription = null,
    string? PaymentType = null,
    string? BudgetWarning = null,
    bool FromApi = false)
{
    /// <summary>Pendência do <c>/webhook-automate</c> (<c>transaction_handler.py</c>).</summary>
    public static PendingTransaction FromAutomate(
        int userId, int accountId, int categoryId, int? invoiceId, string? description, double valueDb, double valueOriginal,
        string transactionType, DateOnly date) =>
        new(userId, accountId, categoryId, invoiceId, valueDb, valueOriginal, transactionType, DateText(date), "automate", description);

    /// <summary>Pendência do <c>/api/transacao</c> (<c>transaction_handler.py</c>).</summary>
    public static PendingTransaction FromApiEndpoint(
        int userId, int accountId, string accountName, string accountType, int categoryId, int? invoiceId, string local,
        string? description, string finalDescription, double valueDb, double valueOriginal, string paymentType, DateOnly date,
        string? budgetWarning) =>
        new(userId, accountId, categoryId, invoiceId, valueDb, valueOriginal, Transactions.TransactionTypes.Expense, DateText(date),
            "api_endpoint", description, local, accountName, accountType, finalDescription, paymentType, budgetWarning, FromApi: true);

    /// <summary>Dicionário na ordem e com as chaves do Python (a 012 lê a mesma chave do Redis).</summary>
    public IReadOnlyDictionary<string, object?> ToData()
    {
        var data = new Dictionary<string, object?> { ["usuario_id"] = UserId, ["conta_id"] = AccountId };
        if (FromApi)
        {
            data["conta_nome"] = AccountName;
            data["conta_tipo"] = AccountType;
        }

        data["categoria_id"] = CategoryId;
        data["fatura_id"] = InvoiceId;
        if (FromApi)
        {
            data["local"] = Local;
            data["descricao"] = Description;
            data["descricao_final"] = FinalDescription;
        }
        else
        {
            data["descricao"] = Description;
        }

        data["valor_db"] = ValueDb;
        data["valor_original"] = ValueOriginal;
        data["tipo_transacao"] = TransactionType;
        if (FromApi)
        {
            data["tipo_pagamento"] = PaymentType;
        }

        data["data_transacao"] = Date;
        data["origem"] = Origin;
        if (FromApi)
        {
            data["budget_warning"] = BudgetWarning;
        }

        return data;
    }

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary><c>format_confirmation_message</c> (<c>transaction_confirmation_service.py</c>, spec 011 §7.5).</summary>
public static class ConfirmationMessage
{
    private const string Line = "━━━━━━━━━━━━━━━━━━━━";

    /// <summary>
    /// A linha do cartão leva o emoji de cartão (o Python tem o caractere U+FFFD no lugar, arquivo corrompido; desvio 100).
    /// </summary>
    public static string Build(
        PendingTransaction data, IReadOnlyList<CategoryOption> categories, string transactionId, int? installments = null, double? totalValue = null)
    {
        var local = data.Local;
        var description = data.Description;
        if (string.IsNullOrEmpty(local))
        {
            local = data.Description;
            description = null;
        }

        var categoryName = "Desconhecida";
        foreach (var category in categories)
        {
            if (category.Id == data.CategoryId)
            {
                categoryName = $"{category.MacroName} → {category.SubName}";
                break;
            }
        }

        var valueText = MoneyText.Brl(data.ValueOriginal);
        var typeEmoji = data.TransactionType == "Renda" ? "💰" : "💸";
        var multi = installments is > 1;

        var sb = new StringBuilder("💰 *CONFIRME SUA TRANSAÇÃO*\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"📍 *{local ?? "None"}*\n");
        if (!string.IsNullOrEmpty(description))
        {
            sb.Append(CultureInfo.InvariantCulture, $"📝 {description}\n");
        }

        sb.Append(multi
            ? $"💵 *{MoneyText.Brl(totalValue)}* ({installments}x de {valueText})\n"
            : $"💵 *{valueText}*\n");
        sb.Append(CultureInfo.InvariantCulture, $"{typeEmoji} {data.TransactionType}\n");

        if (!string.IsNullOrEmpty(data.AccountName))
        {
            if (data.PaymentType == "credito" && data.InvoiceId is not null)
            {
                sb.Append(multi
                    ? $"💳 {data.AccountName} (1ª parcela na fatura atual)\n"
                    : $"💳 {data.AccountName} (na fatura)\n");
            }
            else
            {
                var label = data.PaymentType switch
                {
                    "debito" => "Débito",
                    "pix" => "PIX",
                    "dinheiro" => "Dinheiro",
                    var other => other ?? "None",
                };
                sb.Append(CultureInfo.InvariantCulture, $"🏦 {data.AccountName} ({label})\n");
            }
        }

        sb.Append(CultureInfo.InvariantCulture, $"📂 {categoryName}\n");

        if (!string.IsNullOrEmpty(data.BudgetWarning))
        {
            sb.Append('\n').Append(Line).Append('\n').Append("⚠️ *AVISO DE ORÇAMENTO*\n\n").Append(data.BudgetWarning).Append('\n');
        }

        sb.Append('\n').Append(Line).Append('\n')
            .Append("🔹 *OPÇÕES:*\n\n")
            .Append("✅ *OK* para confirmar\n")
            .Append("✏️ *TROCAR* para mudar categoria\n")
            .Append("❌ *CANCELAR* para descartar\n\n")
            .Append(CultureInfo.InvariantCulture, $"_ID: {transactionId} | Expira em 5 min_");
        return sb.ToString();
    }
}
