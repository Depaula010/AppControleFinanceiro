using System.Globalization;
using MeuSecretario.Api.Accounts;
using MeuSecretario.Api.Auth;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Infrastructure.Transactions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.Api.Transactions;

/// <summary>
/// Feature 004: <c>GET/POST /api/transactions</c> e <c>PUT/DELETE /api/transactions/{id}</c> (envelope A, JWT), mais o
/// serviço de lançamento (<see cref="ITransactionPosting"/>, <see cref="IInvoiceService"/>) que 011, 012 e 013 consomem.
/// </summary>
public sealed class TransactionsModule : IFeatureModule
{
    private const string BasePath = "/api/transactions";

    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ITransactionsUnitOfWork, TransactionsUnitOfWork>();
        services.AddScoped<ListTransactions>();
        services.AddScoped<CreateTransaction>();
        services.AddScoped<UpdateTransaction>();
        services.AddScoped<DeleteTransaction>();

        services.AddScoped<ITransactionPosting, TransactionPosting>();
        services.AddScoped<ITransactionsDiagnostics, TransactionsDiagnostics>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        // Populador vazio até a 013 registrar o real antes deste módulo (Q-004-6).
        services.TryAddScoped<IInvoiceSchedulePopulator, NullInvoiceSchedulePopulator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet(BasePath, ListAsync).RequireUser();
        app.MapPost(BasePath, CreateAsync).RequireUser();
        // O conversor <int:id> do Flask só casa dígitos: com outro valor o 404 vem antes da autenticação (AC-004-81).
        app.MapPut($"{BasePath}/{{id}}", UpdateAsync).AddEndpointFilter<NumericIdFilter>().RequireUser();
        app.MapDelete($"{BasePath}/{{id}}", DeleteAsync).AddEndpointFilter<NumericIdFilter>().RequireUser();

        // Outros métodos em /{id}: id numérico ou `recent` (rota do GET da 006) ⇒ 405; senão 404 HTML.
        app.Map($"{BasePath}/{{id}}", OtherMethods);
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request, ICurrentUser user, ListTransactions useCase, ILogger<TransactionsModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            // Com o parâmetro repetido vale o primeiro valor (request.args.get do Flask).
            var parsed = ListQueryParser.Parse(
                request.Query["limit"].FirstOrDefault(),
                request.Query["offset"].FirstOrDefault(),
                request.Query["tipo"].FirstOrDefault(),
                request.Query["data_inicio"].FirstOrDefault(),
                request.Query["data_fim"].FirstOrDefault());

            if (parsed is ListQueryResult.Rejected rejected)
            {
                return BadRequest(TransactionsMessages.For(rejected.Error));
            }

            var filter = ((ListQueryResult.Ok)parsed).Filter;
            return TransactionsResponses.List(await useCase.ExecuteAsync(user.UserId, filter, cancellationToken), filter);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TransactionsLog.ListFailed(logger, user.UserId, SafeError.Describe(ex));
            return TransactionsResponses.Error(TransactionsMessages.ListFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request, ICurrentUser user, CreateTransaction useCase, ILogger<TransactionsModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await AccountsBodyReader.ReadAsync(request, cancellationToken);

            return await useCase.ExecuteAsync(user.UserId, body, cancellationToken) switch
            {
                CreateTransactionResult.Created created => TransactionsResponses.Created(created.Input, created.Posted),
                CreateTransactionResult.Invalid invalid => BadRequest(TransactionsMessages.For(invalid.Reason)),
                CreateTransactionResult.AccountNotFound => NotFound(TransactionsMessages.AccountNotFound),
                CreateTransactionResult.SubcategoryNotFound => NotFound(TransactionsMessages.SubcategoryNotFound),
                _ => throw new InvalidOperationException("Resultado de criação de transação desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TransactionsLog.CreateFailed(logger, user.UserId, SafeError.Describe(ex));
            return TransactionsResponses.Error(TransactionsMessages.CreateFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> UpdateAsync(
        string id, HttpRequest request, ICurrentUser user, UpdateTransaction useCase, ILogger<TransactionsModule> logger, CancellationToken cancellationToken)
    {
        var transactionId = ParseId(id);
        try
        {
            var body = await AccountsBodyReader.ReadAsync(request, cancellationToken);

            return await useCase.ExecuteAsync(user.UserId, transactionId, body, cancellationToken) switch
            {
                UpdateTransactionResult.Updated updated => TransactionsResponses.Updated(updated.Transaction),
                UpdateTransactionResult.Invalid invalid => BadRequest(TransactionsMessages.For(invalid.Reason)),
                UpdateTransactionResult.NotFound => NotFound(TransactionsMessages.NotFound),
                UpdateTransactionResult.AccountNotFound => NotFound(TransactionsMessages.AccountNotFound),
                UpdateTransactionResult.SubcategoryNotFound => NotFound(TransactionsMessages.SubcategoryNotFound),
                _ => throw new InvalidOperationException("Resultado de atualização de transação desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TransactionsLog.UpdateFailed(logger, user.UserId, transactionId, SafeError.Describe(ex));
            return TransactionsResponses.Error(TransactionsMessages.UpdateFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DeleteAsync(
        string id, ICurrentUser user, DeleteTransaction useCase, ILogger<TransactionsModule> logger, CancellationToken cancellationToken)
    {
        var transactionId = ParseId(id);
        try
        {
            return await useCase.ExecuteAsync(user.UserId, transactionId, cancellationToken)
                ? TransactionsResponses.Deleted()
                : NotFound(TransactionsMessages.NotFound);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TransactionsLog.DeleteFailed(logger, user.UserId, transactionId, SafeError.Describe(ex));
            return TransactionsResponses.Error(TransactionsMessages.DeleteFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult OtherMethods(string id) =>
        NumericIdFilter.IsNumeric(id) || id == "recent"
            ? Results.StatusCode(StatusCodes.Status405MethodNotAllowed)
            : new FlaskNotFoundResult();

    /// <summary>Número que não cabe em <see cref="int"/> nunca é o id de uma transação: vira 0 e responde 404 (RN-004-18).</summary>
    private static int ParseId(string id) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var transactionId) ? transactionId : 0;

    private static IResult BadRequest(string message) => TransactionsResponses.Error(message, StatusCodes.Status400BadRequest);

    private static IResult NotFound(string message) => TransactionsResponses.Error(message, StatusCodes.Status404NotFound);
}
