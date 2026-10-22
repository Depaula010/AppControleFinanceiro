using System.Globalization;
using MeuSecretario.Api.Accounts;
using MeuSecretario.Api.Auth;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Bills;
using MeuSecretario.Infrastructure.Bills;

namespace MeuSecretario.Api.Bills;

/// <summary>
/// Feature 005: <c>GET/POST /api/bills</c>, <c>PUT/DELETE /api/bills/{id}</c> e <c>GET /api/bills/summary</c>, mais o
/// alias <c>/api/contas-mensais</c> (sem <c>summary</c>, <c>api.py</c>). Todas as rotas exigem JWT (<c>RequireUser</c>).
/// </summary>
public sealed class BillsModule : IFeatureModule
{
    private static readonly string[] BasePaths = ["/api/bills", "/api/contas-mensais"];

    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<IBillRepository, BillRepository>();
        services.AddScoped<IEmergencyReserveService, EmergencyReserveService>();
        services.AddScoped<ListBills>();
        services.AddScoped<CreateBill>();
        services.AddScoped<UpdateBill>();
        services.AddScoped<DeleteBill>();
        services.AddScoped<GetBillsSummary>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        foreach (var basePath in BasePaths)
        {
            app.MapGet(basePath, ListAsync).RequireUser();
            app.MapPost(basePath, CreateAsync).RequireUser();
            // O conversor <int:id> do Flask só casa dígitos: com outro valor a rota nem existe e o 404 vem antes da
            // autenticação (AC-005-82). O filtro de id vem antes do RequireUser por isso.
            app.MapPut($"{basePath}/{{id}}", UpdateAsync).AddEndpointFilter<NumericIdFilter>().RequireUser();
            app.MapDelete($"{basePath}/{{id}}", DeleteAsync).AddEndpointFilter<NumericIdFilter>().RequireUser();

            // Os outros métodos em /{id}: id numérico ⇒ 405 (a rota existe para PUT e DELETE); senão 404 HTML.
            app.Map($"{basePath}/{{id}}", OtherMethods);
        }

        // O resumo só existe em /api/bills e só para GET; os demais métodos respondem 405 (a rota estática existe).
        app.MapGet("/api/bills/summary", SummaryAsync).RequireUser();
        app.Map("/api/bills/summary", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
    }

    private static async Task<IResult> ListAsync(
        ICurrentUser user, ListBills useCase, ILogger<BillsModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            return BillsResponses.List(await useCase.ExecuteAsync(user.UserId, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BillsLog.ListFailed(logger, user.UserId, SafeError.Describe(ex));
            return Fail(BillsMessages.ListFailed);
        }
    }

    private static async Task<IResult> SummaryAsync(
        ICurrentUser user, GetBillsSummary useCase, ILogger<BillsModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            return BillsResponses.Summary(await useCase.ExecuteAsync(user.UserId, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BillsLog.SummaryFailed(logger, user.UserId, SafeError.Describe(ex));
            return Fail(BillsMessages.SummaryFailed);
        }
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request, ICurrentUser user, CreateBill useCase, ILogger<BillsModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await AccountsBodyReader.ReadAsync(request, cancellationToken);

            return await useCase.ExecuteAsync(user.UserId, body, cancellationToken) switch
            {
                CreateBillResult.Created created => BillsResponses.Created(created.Bill),
                CreateBillResult.Invalid invalid => BadRequest(BillsMessages.For(invalid.Reason)),
                CreateBillResult.AccountNotFound => NotFoundWith(BillsMessages.AccountNotFound),
                CreateBillResult.SubcategoryNotFound => NotFoundWith(BillsMessages.SubcategoryNotFound),
                _ => throw new InvalidOperationException("Resultado de criação de conta mensal desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BillsLog.CreateFailed(logger, user.UserId, SafeError.Describe(ex));
            return Fail(BillsMessages.CreateFailed);
        }
    }

    private static async Task<IResult> UpdateAsync(
        string id, HttpRequest request, ICurrentUser user, UpdateBill useCase, ILogger<BillsModule> logger, CancellationToken cancellationToken)
    {
        var billId = ParseId(id);
        try
        {
            var body = await AccountsBodyReader.ReadAsync(request, cancellationToken);

            return await useCase.ExecuteAsync(user.UserId, billId, body, cancellationToken) switch
            {
                UpdateBillResult.Updated updated => BillsResponses.Updated(updated.Bill),
                UpdateBillResult.Invalid invalid => BadRequest(BillsMessages.For(invalid.Reason)),
                UpdateBillResult.NotFound => NotFoundWith(BillsMessages.NotFound),
                UpdateBillResult.AccountNotFound => NotFoundWith(BillsMessages.AccountNotFound),
                UpdateBillResult.SubcategoryNotFound => NotFoundWith(BillsMessages.SubcategoryNotFound),
                _ => throw new InvalidOperationException("Resultado de atualização de conta mensal desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BillsLog.UpdateFailed(logger, user.UserId, billId, SafeError.Describe(ex));
            return Fail(BillsMessages.UpdateFailed);
        }
    }

    private static async Task<IResult> DeleteAsync(
        string id, ICurrentUser user, DeleteBill useCase, ILogger<BillsModule> logger, CancellationToken cancellationToken)
    {
        var billId = ParseId(id);
        try
        {
            return await useCase.ExecuteAsync(user.UserId, billId, cancellationToken)
                ? BillsResponses.Deleted()
                : NotFoundWith(BillsMessages.NotFound);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            BillsLog.DeleteFailed(logger, user.UserId, billId, SafeError.Describe(ex));
            return Fail(BillsMessages.DeleteFailed);
        }
    }

    private static IResult OtherMethods(string id) =>
        NumericIdFilter.IsNumeric(id)
            ? Results.StatusCode(StatusCodes.Status405MethodNotAllowed)
            : new FlaskNotFoundResult();

    /// <summary>
    /// O filtro só deixa passar dígitos. Um número que não cabe em <see cref="int"/> nunca é o id de um agendamento:
    /// vira 0 (a identidade começa em 1), e o resultado é "conta mensal não encontrada" (RN-005-16).
    /// </summary>
    private static int ParseId(string id) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var billId) ? billId : 0;

    private static IResult BadRequest(string message) => BillsResponses.Error(message, StatusCodes.Status400BadRequest);

    private static IResult NotFoundWith(string message) => BillsResponses.Error(message, StatusCodes.Status404NotFound);

    private static IResult Fail(string message) => BillsResponses.Error(message, StatusCodes.Status500InternalServerError);
}
