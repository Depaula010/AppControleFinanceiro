using System.Globalization;
using MeuSecretario.Api.Auth;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Accounts;
using MeuSecretario.Infrastructure.Accounts;

namespace MeuSecretario.Api.Accounts;

/// <summary>
/// Feature 002: <c>GET/POST /api/accounts</c> e <c>PUT/DELETE /api/accounts/{id}</c>, mais o alias <c>/api/contas</c>
/// (mesma lógica, <c>api.py:1017-1063</c>). Todas as rotas exigem JWT (<c>RequireUser</c>).
/// </summary>
public sealed class AccountsModule : IFeatureModule
{
    private static readonly string[] BasePaths = ["/api/accounts", "/api/contas"];

    /// <summary>Equivale ao <c>&lt;int:id&gt;</c> do Flask: só dígitos (<c>-1</c>, <c>1.5</c> e <c>abc</c> não casam).</summary>
    private const string NumericId = "{id:regex(^[0-9]+$)}";

    /// <summary>O contrário: qualquer segmento que não seja só dígitos cai no 404 HTML do Flask, com qualquer método.</summary>
    private const string NonNumericId = "{id:regex(^(?![0-9]+$)[^/]+$)}";

    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ListAccounts>();
        services.AddScoped<CreateAccount>();
        services.AddScoped<UpdateAccount>();
        services.AddScoped<DeleteAccount>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        foreach (var basePath in BasePaths)
        {
            app.MapGet(basePath, ListAsync).RequireUser();
            app.MapPost(basePath, CreateAsync).RequireUser();
            app.MapPut($"{basePath}/{NumericId}", UpdateAsync).RequireUser();
            app.MapDelete($"{basePath}/{NumericId}", DeleteAsync).RequireUser();

            // O roteamento do Flask responde 404 antes da autenticação (AC-002-83).
            app.Map($"{basePath}/{NonNumericId}", FlaskNotFound.WriteAsync);
        }
    }

    private static async Task<IResult> ListAsync(
        ICurrentUser user, ListAccounts useCase, ILogger<AccountsModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            return AccountsResponses.List(await useCase.ExecuteAsync(user.UserId, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AccountsLog.ListFailed(logger, user.UserId, SafeError.Describe(ex));
            return AccountsResponses.Error(AccountsMessages.ListFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request, ICurrentUser user, CreateAccount useCase, ILogger<AccountsModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await AccountsBodyReader.ReadAsync(request, cancellationToken);

            return await useCase.ExecuteAsync(user.UserId, body, cancellationToken) switch
            {
                CreateAccountResult.Created created => AccountsResponses.Created(created.Account),
                CreateAccountResult.Invalid invalid => BadRequest(AccountsMessages.For(invalid.Reason)),
                CreateAccountResult.NameTaken => BadRequest(AccountsMessages.NameDuplicate),
                _ => throw new InvalidOperationException("Resultado de criação de conta desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AccountsLog.CreateFailed(logger, user.UserId, SafeError.Describe(ex));
            return AccountsResponses.Error(AccountsMessages.CreateFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> UpdateAsync(
        string id, HttpRequest request, ICurrentUser user, UpdateAccount useCase, ILogger<AccountsModule> logger, CancellationToken cancellationToken)
    {
        var accountId = ParseId(id);
        try
        {
            var body = await AccountsBodyReader.ReadAsync(request, cancellationToken);

            return await useCase.ExecuteAsync(user.UserId, accountId, body, cancellationToken) switch
            {
                UpdateAccountResult.Updated updated => AccountsResponses.Updated(updated.Account),
                UpdateAccountResult.Invalid invalid => BadRequest(AccountsMessages.For(invalid.Reason)),
                UpdateAccountResult.NotFound => NotFound(),
                UpdateAccountResult.NameTaken => BadRequest(AccountsMessages.NameDuplicateOther),
                _ => throw new InvalidOperationException("Resultado de atualização de conta desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AccountsLog.UpdateFailed(logger, user.UserId, accountId, SafeError.Describe(ex));
            return AccountsResponses.Error(AccountsMessages.UpdateFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> DeleteAsync(
        string id, ICurrentUser user, DeleteAccount useCase, ILogger<AccountsModule> logger, CancellationToken cancellationToken)
    {
        var accountId = ParseId(id);
        try
        {
            return await useCase.ExecuteAsync(user.UserId, accountId, cancellationToken) switch
            {
                DeleteAccountResult.Deleted => AccountsResponses.Deleted(),
                DeleteAccountResult.NotFound => NotFound(),
                DeleteAccountResult.HasTransactions has => BadRequest(AccountsMessages.HasTransactions(has.Count)),
                DeleteAccountResult.HasActiveSchedules has => BadRequest(AccountsMessages.HasActiveSchedules(has.Count)),
                _ => throw new InvalidOperationException("Resultado de remoção de conta desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AccountsLog.DeleteFailed(logger, user.UserId, accountId, SafeError.Describe(ex));
            return AccountsResponses.Error(AccountsMessages.DeleteFailed, StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// A rota só deixa passar dígitos. Um número que não cabe em <see cref="int"/> nunca é o id de uma conta:
    /// vira 0 (a identidade começa em 1), e o resultado é "conta não encontrada" como no Postgres (RN-002-18).
    /// </summary>
    private static int ParseId(string id) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var accountId) ? accountId : 0;

    private static IResult BadRequest(string message) => AccountsResponses.Error(message, StatusCodes.Status400BadRequest);

    private static IResult NotFound() => AccountsResponses.Error(AccountsMessages.NotFound, StatusCodes.Status404NotFound);
}
