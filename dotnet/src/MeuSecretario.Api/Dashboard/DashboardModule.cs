using MeuSecretario.Api.Auth;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Dashboard;
using MeuSecretario.Domain.Dashboard;
using MeuSecretario.Infrastructure.Dashboard;

namespace MeuSecretario.Api.Dashboard;

/// <summary>
/// Feature 006: as 4 leituras do dashboard e seus aliases (<c>api.py:174-178, 181-280, 927-1159, 1162-1298</c>). Todas
/// exigem JWT (<c>RequireUser</c>), só respondem GET (os outros métodos dão 405 pelo roteamento) e nunca escrevem.
/// </summary>
public sealed class DashboardModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<GetDashboardSummary>();
        services.AddScoped<GetDashboardCharts>();
        services.AddScoped<GetRecentTransactions>();
        services.AddScoped<GetDashboardAlerts>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        foreach (var path in new[] { "/api/dashboard/summary", "/api/dashboard/resumo", "/api/dashboard/stats" })
        {
            app.MapGet(path, SummaryAsync).RequireUser();
        }

        app.MapGet("/api/dashboard/charts", ChartsAsync).RequireUser();

        // O literal /api/transactions/recent vence o {id} da feature 004 (que responde 405 aos outros métodos).
        foreach (var path in new[] { "/api/dashboard/recent", "/api/transactions/recent", "/api/transacoes/recentes" })
        {
            app.MapGet(path, RecentAsync).RequireUser();
        }

        app.MapGet("/api/dashboard/alerts", AlertsAsync).RequireUser();
    }

    private static async Task<IResult> SummaryAsync(
        ICurrentUser user, GetDashboardSummary useCase, ILogger<DashboardModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            return DashboardResponses.Summary(await useCase.ExecuteAsync(user.UserId, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DashboardLog.SummaryFailed(logger, user.UserId, SafeError.Describe(ex));
            return Fail(DashboardMessages.SummaryFailed);
        }
    }

    private static async Task<IResult> ChartsAsync(
        HttpRequest request, ICurrentUser user, GetDashboardCharts useCase, ILogger<DashboardModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            // Com o parâmetro repetido vale o primeiro valor (request.args.get do Flask).
            if (MonthsParameter.Parse(request.Query["meses"].FirstOrDefault()) is not MonthsResult.Ok months)
            {
                return DashboardResponses.Error(DashboardMessages.MonthsOutOfRange, StatusCodes.Status400BadRequest);
            }

            return DashboardResponses.Charts(await useCase.ExecuteAsync(user.UserId, months.Months, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DashboardLog.ChartsFailed(logger, user.UserId, SafeError.Describe(ex));
            return Fail(DashboardMessages.ChartsFailed);
        }
    }

    private static async Task<IResult> RecentAsync(
        ICurrentUser user, GetRecentTransactions useCase, ILogger<DashboardModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            return DashboardResponses.Recent(await useCase.ExecuteAsync(user.UserId, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DashboardLog.RecentFailed(logger, user.UserId, SafeError.Describe(ex));
            return Fail(DashboardMessages.RecentFailed);
        }
    }

    private static async Task<IResult> AlertsAsync(
        ICurrentUser user, GetDashboardAlerts useCase, ILogger<DashboardModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            return DashboardResponses.Alerts(await useCase.ExecuteAsync(user.UserId, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DashboardLog.AlertsFailed(logger, user.UserId, SafeError.Describe(ex));
            return Fail(DashboardMessages.AlertsFailed);
        }
    }

    private static IResult Fail(string message) => DashboardResponses.Error(message, StatusCodes.Status500InternalServerError);
}
