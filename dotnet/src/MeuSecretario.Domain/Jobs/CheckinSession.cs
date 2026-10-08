using System.Globalization;
using System.Text.Json.Serialization;

namespace MeuSecretario.Domain.Jobs;

/// <summary>
/// Item da sessão do check-in no Redis: as colunas da query do Python (mesmos nomes, ordem do <c>SELECT</c>) mais
/// <c>status_text</c> e <c>dias_atraso</c> (<c>create_checkin_session</c>, <c>nightly_checkin_service.py:333-359</c>).
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

/// <summary>Valor de <c>nightly_checkin:{whatsapp}:{id}</c>.</summary>
public sealed record CheckinSession
{
    [JsonPropertyName("items")]
    public required IReadOnlyDictionary<string, CheckinSessionItem> Items { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("total_items")]
    public required int TotalItems { get; init; }

    /// <summary>Monta a sessão: receitas primeiro, depois as despesas sem cartão; índices <c>"1"</c> a <c>"N"</c> (RN-013-44).</summary>
    public static CheckinSession Build(IEnumerable<CheckinBill> bills, DateOnly today)
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

        return new CheckinSession
        {
            Items = items,
            CreatedAt = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TotalItems = items.Count,
        };
    }
}
