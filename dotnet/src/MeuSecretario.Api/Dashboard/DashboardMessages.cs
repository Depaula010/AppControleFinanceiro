namespace MeuSecretario.Api.Dashboard;

/// <summary>Textos exatos do Flask (spec 006 §3). Mudar uma palavra aqui é mudar o contrato.</summary>
public static class DashboardMessages
{
    public const string MonthsOutOfRange = "Parâmetro 'meses' deve estar entre 1 e 12";

    public const string SummaryFailed = "Erro ao carregar resumo do dashboard";
    public const string ChartsFailed = "Erro ao carregar dados dos gráficos";
    public const string RecentFailed = "Erro ao carregar transações recentes";
    public const string AlertsFailed = "Erro ao buscar alertas";
}
