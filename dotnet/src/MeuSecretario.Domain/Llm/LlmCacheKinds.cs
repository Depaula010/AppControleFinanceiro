namespace MeuSecretario.Domain.Llm;

/// <summary>Prefixos e TTL do cache do Gemini (<c>CACHE_TTL_CONFIG</c> do Python, spec 010 RN-010-27).</summary>
public static class LlmCacheKinds
{
    public const string Intent = "intent";
    public const string Category = "category";
    public const string ExtractTransaction = "extract_trans";
    public const string PeriodQuery = "period_query";
    public const string CalendarQuery = "calendar_query";
    public const string ChartType = "chart_type";
    public const string InvoiceQuery = "fatura_query";
    public const string BalanceQuery = "saldo_query";
    public const string Other = "other";

    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(259200);

    /// <summary>Prefixos com contador próprio nas métricas (o último, <see cref="Other"/>, recebe os demais).</summary>
    public static IReadOnlyList<string> MetricPrefixes { get; } =
        [Intent, Category, ExtractTransaction, PeriodQuery, CalendarQuery, ChartType, InvoiceQuery, BalanceQuery, Other];

    /// <summary>TTL do tipo; prefixo desconhecido usa 3 dias.</summary>
    public static TimeSpan TtlFor(string prefix) => prefix switch
    {
        Intent => TimeSpan.FromSeconds(604800),
        Category => TimeSpan.FromSeconds(2592000),
        ExtractTransaction => TimeSpan.FromSeconds(86400),
        PeriodQuery or CalendarQuery or ChartType or InvoiceQuery or BalanceQuery => TimeSpan.FromSeconds(604800),
        _ => DefaultTtl,
    };

    /// <summary>Nome do contador de métricas para o prefixo (<c>other</c> se não listado).</summary>
    public static string MetricKey(string prefix) =>
        MetricPrefixes.Contains(prefix) ? prefix : Other;
}
