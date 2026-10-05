using MeuSecretario.Api.Auth;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Settings;
using MeuSecretario.Domain.Settings;
using MeuSecretario.Infrastructure.Settings;

namespace MeuSecretario.Api.Settings;

/// <summary>
/// Feature 007: as 11 rotas <c>/api/user/*</c> (perfil, chaves de API próprias, notificações, endereços favoritos e
/// <c>settings</c> agregado), todas com JWT (<c>RequireUser</c>) e valendo só para o usuário do token. Falha inesperada:
/// 500 com texto fixo da rota (desvio 008); só o log leva a descrição segura do erro.
/// </summary>
public sealed class SettingsModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSettingsInfrastructure();

        services.AddScoped<GetProfile>();
        services.AddScoped<UpdateProfile>();
        services.AddScoped<GetApiKeySettings>();
        services.AddScoped<SaveOwnApiKey>();
        services.AddScoped<ValidateProviderKey>();
        services.AddScoped<GetNotificationSettings>();
        services.AddScoped<SaveNotificationSettings>();
        services.AddScoped<ListAddresses>();
        services.AddScoped<SaveAddress>();
        services.AddScoped<DeleteAddress>();
        services.AddScoped<GetUserSettings>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/user").AddEndpointFilter(NoStore);

        group.MapGet("/profile", GetProfileAsync).RequireUser();
        group.MapPut("/profile", UpdateProfileAsync).RequireUser();
        group.MapGet("/api-keys", GetApiKeysAsync).RequireUser();
        group.MapPut("/api-keys", SaveApiKeyAsync).RequireUser();
        group.MapPost("/api-keys/validate", ValidateApiKeyAsync).RequireUser();
        group.MapGet("/notifications", GetNotificationsAsync).RequireUser();
        group.MapPut("/notifications", SaveNotificationsAsync).RequireUser();
        group.MapGet("/addresses", GetAddressesAsync).RequireUser();
        group.MapPost("/addresses", SaveAddressAsync).RequireUser();
        group.MapDelete("/addresses/{label}", DeleteAddressAsync).RequireUser();
        group.MapGet("/settings", GetSettingsAsync).RequireUser();
    }

    /// <summary>Perfil e preferências de chave não devem ser guardados por proxy nem navegador; não altera o contrato.</summary>
    private static ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return next(context);
    }

    // ------------------------------------------------------------------ 1 e 2. perfil

    private static Task<IResult> GetProfileAsync(ICurrentUser user, GetProfile useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "perfil", SettingsMessages.ProfileFetchFailed, async () =>
            await useCase.ExecuteAsync(user.UserId, ct) is { } profile
                ? Envelopes.A.Success(SettingsResponses.Profile(profile))
                : NotFound(SettingsMessages.UserNotFound));

    private static Task<IResult> UpdateProfileAsync(
        HttpRequest request, ICurrentUser user, UpdateProfile useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "atualizar perfil", SettingsMessages.ProfileUpdateFailed, async () =>
        {
            var body = await SettingsBodyReader.ReadAsync(request, ct);
            return await useCase.ExecuteAsync(user.UserId, body, ct) switch
            {
                UpdateProfileResult.Updated updated => Envelopes.A.Success(
                    SettingsResponses.Profile(updated.Profile), SettingsMessages.ProfileUpdated),
                UpdateProfileResult.Invalid invalid => BadRequest(SettingsMessages.For(invalid.Error)),
                UpdateProfileResult.NotFound => NotFound(SettingsMessages.UserNotFound),
                UpdateProfileResult.EmailTaken => SettingsResponses.Error(SettingsMessages.EmailTaken, StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException("Resultado de atualização de perfil desconhecido."),
            };
        });

    // ------------------------------------------------------------------ 3, 4 e 5. chaves de API

    private static Task<IResult> GetApiKeysAsync(ICurrentUser user, GetApiKeySettings useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "chaves", SettingsMessages.ApiKeysFetchFailed, async () =>
            Envelopes.A.Success(SettingsResponses.ApiKeys(await useCase.ExecuteAsync(user.UserId, ct))));

    private static Task<IResult> SaveApiKeyAsync(
        HttpRequest request, ICurrentUser user, SaveOwnApiKey useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "salvar chave", SettingsMessages.ApiKeySaveFailed, async () =>
        {
            var body = await SettingsBodyReader.ReadAsync(request, ct);
            return await useCase.ExecuteAsync(user.UserId, body, ct) switch
            {
                SaveOwnApiKeyResult.Saved => Envelopes.A.Success(message: SettingsMessages.ApiKeySaved),
                SaveOwnApiKeyResult.Invalid invalid => BadRequest(SettingsMessages.For(invalid.Error)),
                _ => throw new InvalidOperationException("Resultado de gravação de chave desconhecido."),
            };
        });

    private static Task<IResult> ValidateApiKeyAsync(
        HttpRequest request, ICurrentUser user, ValidateProviderKey useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "validar chave", SettingsMessages.ValidateFailed, async () =>
        {
            var body = await SettingsBodyReader.ReadAsync(request, ct);
            switch (await useCase.ExecuteAsync(body, ct))
            {
                case ValidateProviderKeyResult.Checked result:
                    return Envelopes.A.Success(new Dictionary<string, object?> { ["is_valid"] = result.IsValid });
                case ValidateProviderKeyResult.Invalid invalid:
                    return BadRequest(SettingsMessages.For(invalid.Error));
                case ValidateProviderKeyResult.Unavailable unavailable:
                    // A mensagem da exceção pode citar a URL do provedor: só a descrição segura vai ao log.
                    SettingsLog.ProviderUnavailable(logger, user.UserId, SafeError.Describe(unavailable.Error));
                    return SettingsResponses.Error(SettingsMessages.ValidateUnavailable, StatusCodes.Status503ServiceUnavailable);
                default:
                    throw new InvalidOperationException("Resultado de validação de chave desconhecido.");
            }
        });

    // ------------------------------------------------------------------ 6 e 7. notificações

    private static Task<IResult> GetNotificationsAsync(
        ICurrentUser user, GetNotificationSettings useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "notificações", SettingsMessages.NotificationsFetchFailed, async () =>
            Envelopes.A.Success(SettingsResponses.Notifications(await useCase.ExecuteAsync(user.UserId, ct))));

    private static Task<IResult> SaveNotificationsAsync(
        HttpRequest request, ICurrentUser user, SaveNotificationSettings useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "salvar notificações", SettingsMessages.NotificationsSaveFailed, async () =>
        {
            var body = await SettingsBodyReader.ReadAsync(request, ct);
            return await useCase.ExecuteAsync(user.UserId, body, ct) switch
            {
                SaveNotificationsResult.Saved => Envelopes.A.Success(message: SettingsMessages.NotificationsSaved),
                SaveNotificationsResult.Invalid invalid => BadRequest(SettingsMessages.For(invalid.Error)),
                _ => throw new InvalidOperationException("Resultado de gravação de notificações desconhecido."),
            };
        });

    // ------------------------------------------------------------------ 8, 9 e 10. endereços

    private static Task<IResult> GetAddressesAsync(ICurrentUser user, ListAddresses useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "endereços", SettingsMessages.AddressesFetchFailed, async () =>
            Envelopes.A.Success(SettingsResponses.Addresses(await useCase.ExecuteAsync(user.UserId, ct))));

    private static Task<IResult> SaveAddressAsync(
        HttpRequest request, ICurrentUser user, SaveAddress useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "salvar endereço", SettingsMessages.AddressSaveFailed, async () =>
        {
            var body = await SettingsBodyReader.ReadAsync(request, ct);
            return await useCase.ExecuteAsync(user.UserId, body, ct) switch
            {
                SaveAddressResult.Saved => Envelopes.A.Success(message: SettingsMessages.AddressSaved, statusCode: StatusCodes.Status201Created),
                SaveAddressResult.Invalid invalid => BadRequest(SettingsMessages.For(invalid.Error)),
                _ => throw new InvalidOperationException("Resultado de gravação de endereço desconhecido."),
            };
        });

    private static Task<IResult> DeleteAddressAsync(
        string label, ICurrentUser user, DeleteAddress useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "remover endereço", SettingsMessages.AddressDeleteFailed, async () =>
            await useCase.ExecuteAsync(user.UserId, label, ct) switch
            {
                DeleteAddressResult.Deleted => Envelopes.A.Success(message: SettingsMessages.AddressDeleted),
                DeleteAddressResult.InvalidLabel => BadRequest(SettingsMessages.InvalidLabel),
                DeleteAddressResult.NotFound => NotFound(SettingsMessages.AddressNotFound),
                _ => throw new InvalidOperationException("Resultado de remoção de endereço desconhecido."),
            });

    // ------------------------------------------------------------------ 11. settings

    private static Task<IResult> GetSettingsAsync(ICurrentUser user, GetUserSettings useCase, ILogger<SettingsModule> logger, CancellationToken ct) =>
        RunAsync(logger, user, "configurações", SettingsMessages.SettingsFetchFailed, async () =>
            await useCase.ExecuteAsync(user.UserId, ct) is { } settings
                ? Envelopes.A.Success(SettingsResponses.Settings(settings))
                : NotFound(SettingsMessages.UserNotFound));

    // ------------------------------------------------------------------ utilidades

    private static async Task<IResult> RunAsync(
        ILogger<SettingsModule> logger, ICurrentUser user, string operation, string failureMessage, Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SettingsLog.Failed(logger, operation, user.UserId, SafeError.Describe(ex));
            return SettingsResponses.Error(failureMessage, StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult BadRequest(string message) => SettingsResponses.Error(message, StatusCodes.Status400BadRequest);

    private static IResult NotFound(string message) => SettingsResponses.Error(message, StatusCodes.Status404NotFound);
}
