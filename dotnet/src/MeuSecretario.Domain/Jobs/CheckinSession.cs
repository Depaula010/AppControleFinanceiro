using System.Globalization;
using System.Text.Json.Serialization;

namespace MeuSecretario.Domain.Jobs;

/// <summary>
/// Item da sessão do check-in no Redis: as colunas da query do Python (mesmos nomes, ordem do <c>SELECT</c>) mais
/// <c>status_text</c> e <c>dias_atraso</c> (<c>create_checkin_session</c>, <c>nightly_checkin_service.py</c>).
/// O formato é o contrato com o handler de resposta (012) e com o rollback para o Flask.
/// </summary>
public sealed record CheckinSessionItem
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("descricao")]
    public required string Description { get; init; }

    [JsonPropertyName("valor_previsto")]
    public required double? ExpectedAmount { get; init; }

    [JsonPropertyName("dia_execucao")]
    public required int Day { get; init; }

    [JsonPropertyName("conta_id")]
    public required int AccountId { get; init; }

    [JsonPropertyName("subcategoria_id")]
    public required int SubcategoryId { get; init; }

    [JsonPropertyName("usuario_id")]
    public required int UserId { get; init; }

    [JsonPropertyName("nome_conta")]
    public required string AccountName { get; init; }

    [JsonPropertyName("tipo_conta")]
    public required string AccountType { get; init; }

    [JsonPropertyName("categoria")]
    public required string Category { get; init; }

    [JsonPropertyName("nome_macro")]
    public required string MacroName { get; init; }

    [JsonPropertyName("nome_grupo")]
    public required string GroupName { get; init; }

    [JsonPropertyName("tipo_agendamento")]
    public required string Kind { get; init; }

    [JsonPropertyName("parcelas_executadas")]
    public required int? ExecutedInstallments { get; init; }

    [JsonPropertyName("total_parcelas")]
    public required int? TotalInstallments { get; init; }

    /// <summary>Texto ISO <c>AAAA-MM-DD</c> (o Python converte <c>date</c> para <c>isoformat</c> antes de gravar).</summary>
    [JsonPropertyName("data_vencimento_real")]
    public required string? DueDate { get; init; }

    [JsonPropertyName("status_text")]
    public required string StatusText { get; init; }

    [JsonPropertyName("dias_atraso")]
    public required int DaysLate { get; init; }
}

/// <summary>
/// Fatura de cartão numerada no check-in (desvio 264). Fica em <c>invoices</c> da sessão, separada de <c>items</c>, então o formato
/// do Flask (e o rollback para ele) não muda: o Flask ignora o campo extra.
/// </summary>
public sealed record CheckinInvoiceItem
{
    [JsonPropertyName("fatura_id")]
    public required int InvoiceId { get; init; }

    /// <summary>Conta do cartão (<c>contas.id</c>).</summary>
    [JsonPropertyName("conta_id")]
    public required int CardAccountId { get; init; }

    [JsonPropertyName("usuario_id")]
    public required int UserId { get; init; }

    [JsonPropertyName("cartao")]
    public required string Card { get; init; }

    [JsonPropertyName("valor")]
    public required double Amount { get; init; }

    [JsonPropertyName("data_vencimento")]
    public required string DueDate { get; init; }

    /// <summary>As faturas vencidas e depois as que vencem hoje, na ordem em que a mensagem as numera.</summary>
    public static IReadOnlyList<CheckinInvoiceItem> FromSnapshot(CheckinSnapshot snapshot, int userId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return
        [
            .. snapshot.OverdueInvoices.Select(i => Create(i.Id, i.AccountId, userId, i.Card, i.Total, i.DueDate)),
            .. snapshot.InvoicesDueToday.Select(i => Create(i.Id, i.AccountId, userId, i.AccountName, i.Total, i.DueDate)),
        ];
    }

    private static CheckinInvoiceItem Create(int invoiceId, int accountId, int userId, string card, decimal total, DateOnly due) => new()
    {
        InvoiceId = invoiceId,
        CardAccountId = accountId,
        UserId = userId,
        Card = card,
        Amount = (double)total,
        DueDate = due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };
}

/// <summary>Valor de <c>nightly_checkin:{whatsapp}:{id}</c>.</summary>
public sealed record CheckinSession
{
    [JsonPropertyName("items")]
    public required IReadOnlyDictionary<string, CheckinSessionItem> Items { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("total_items")]
    public required int TotalItems { get; init; }

    /// <summary>Faturas numeradas depois das contas (índices a partir de <c>items.Count + 1</c>); <c>TotalItems</c> conta as duas listas.</summary>
    [JsonPropertyName("invoices")]
    public IReadOnlyDictionary<string, CheckinInvoiceItem> Invoices { get; init; } = new Dictionary<string, CheckinInvoiceItem>();

    /// <summary>Monta a sessão: receitas primeiro, depois as despesas sem cartão; índices <c>"1"</c> a <c>"N"</c> (RN-013-44).</summary>
    public static CheckinSession Build(IEnumerable<CheckinBill> bills, DateOnly today, IEnumerable<CheckinInvoiceItem>? invoices = null)
    {
        ArgumentNullException.ThrowIfNull(bills);
        var items = new Dictionary<string, CheckinSessionItem>();
        var index = 1;
        foreach (var bill in CheckinRules.OrderForSession(bills))
        {
            var (status, daysLate) = CheckinRules.CategorizeByDelay(bill.Day, today);
            items[index.ToString(CultureInfo.InvariantCulture)] = new CheckinSessionItem
            {
                Id = bill.Id,
                Description = bill.Description,
                ExpectedAmount = bill.ExpectedAmount is { } amount ? (double)amount : null,
                Day = bill.Day,
                AccountId = bill.AccountId,
                SubcategoryId = bill.SubcategoryId,
                UserId = bill.UserId,
                AccountName = bill.AccountName,
                AccountType = bill.AccountType,
                Category = bill.Category,
                MacroName = bill.MacroName,
                GroupName = bill.GroupName,
                Kind = bill.Kind,
                ExecutedInstallments = bill.ExecutedInstallments,
                TotalInstallments = bill.TotalInstallments,
                DueDate = bill.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StatusText = status ?? $"Atrasado {daysLate.ToString(CultureInfo.InvariantCulture)} dias",
                DaysLate = daysLate,
            };
            index++;
        }

        var invoiceItems = new Dictionary<string, CheckinInvoiceItem>();
        foreach (var invoice in invoices ?? [])
        {
            invoiceItems[index.ToString(CultureInfo.InvariantCulture)] = invoice;
            index++;
        }

        return new CheckinSession
        {
            Items = items,
            Invoices = invoiceItems,
            CreatedAt = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TotalItems = items.Count + invoiceItems.Count,
        };
    }
}
