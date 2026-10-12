using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Auth;

namespace MeuSecretario.Api.Auth;

/// <summary>
/// Feature 001: <c>POST /auth/register</c>, <c>/auth/login</c> e <c>/auth/verify</c> (envelope A com campos na raiz)
/// mais os serviços compartilhados (usuário atual) usados pelo filtro <c>RequireUser</c>.
/// </summary>
public sealed class AuthModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());

        services.AddScoped<RegisterUser>();
        services.AddScoped<LoginUser>();
        services.AddScoped<VerifyToken>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", RegisterAsync);
        app.MapPost("/auth/login", LoginAsync);
        app.MapPost("/auth/verify", VerifyAsync);
    }

    private static async Task<IResult> RegisterAsync(
        HttpRequest request, RegisterUser useCase, ILogger<AuthModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await JsonBody.ReadObjectAsync(request, cancellationToken);
            var result = await useCase.ExecuteAsync(
                new RegisterInput(
                    body.GetText("nome"),
                    body.GetText("whatsapp"),
                    body.GetText("password"),
                    body.GetRaw("dia_vencimento"),
                    body.GetRaw("dia_fechamento")),
                cancellationToken);

            return result switch
            {
                RegisterResult.Success success => Envelopes.A.SuccessWithFields(
                    new Dictionary<string, object?> { ["user_id"] = success.UserId },
                    AuthMessages.Registered,
                    StatusCodes.Status201Created),
                RegisterResult.MissingFields => BadRequest(AuthMessages.RegisterMissingFields),
                RegisterResult.PasswordTooShort => BadRequest(AuthMessages.PasswordTooShort),
                RegisterResult.MissingDays => BadRequest(AuthMessages.RegisterMissingDays),
                RegisterResult.DayOutOfRange range => BadRequest(AuthMessages.DayOutOfRange(range.Field)),
                RegisterResult.DaysNotInteger => BadRequest(AuthMessages.DaysNotInteger),
                RegisterResult.WhatsappTaken => Envelopes.A.Error(AuthMessages.WhatsappTaken, StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException("Resultado de cadastro desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nunca loga a exceção inteira: a mensagem do PostgreSQL pode conter o número do WhatsApp (desvio 008).
            AuthLog.RegisterFailed(logger, SafeError.Describe(ex));
            return Envelopes.A.Error(AuthMessages.RegisterFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> LoginAsync(
        HttpRequest request, LoginUser useCase, ILogger<AuthModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await JsonBody.ReadObjectAsync(request, cancellationToken);
            var result = await useCase.ExecuteAsync(new LoginInput(body.GetText("whatsapp"), body.GetText("password")), cancellationToken);

            return result switch
            {
                LoginResult.Success success => Envelopes.A.SuccessWithFields(
                    new Dictionary<string, object?>
                    {
                        ["token"] = success.Token,
                        ["user"] = new Dictionary<string, object?>
                        {
                            ["id"] = success.User.Id,
                            ["nome"] = success.User.Nome,
                            ["whatsapp"] = success.User.Whatsapp,
                        },
                    }),
                LoginResult.MissingFields => BadRequest(AuthMessages.LoginMissingFields),
                LoginResult.NotRegistered => Envelopes.A.Error(AuthMessages.NotRegistered, StatusCodes.Status404NotFound),
                LoginResult.NoPassword => Unauthorized(AuthMessages.NoPassword),
                LoginResult.WrongPassword => Unauthorized(AuthMessages.WrongPassword),
                _ => throw new InvalidOperationException("Resultado de login desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AuthLog.LoginFailed(logger, SafeError.Describe(ex));
            return Envelopes.A.Error(AuthMessages.LoginFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> VerifyAsync(
        HttpRequest request, VerifyToken useCase, ILogger<AuthModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await JsonBody.ReadObjectAsync(request, cancellationToken);

            return useCase.Execute(body.GetText("token")) switch
            {
                VerifyResult.Valid valid => Envelopes.A.SuccessWithFields(
                    new Dictionary<string, object?> { ["valid"] = true, ["user_id"] = valid.UserId }),
                VerifyResult.MissingToken => BadRequest(AuthMessages.MissingToken),
                VerifyResult.Invalid => Unauthorized(AuthMessages.InvalidToken),
                VerifyResult.NoUserId => Envelopes.A.Error(AuthMessages.VerifyFailed, StatusCodes.Status500InternalServerError),
                _ => throw new InvalidOperationException("Resultado de verificação desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AuthLog.VerifyFailed(logger, SafeError.Describe(ex));
            return Envelopes.A.Error(AuthMessages.VerifyFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult BadRequest(string message) => Envelopes.A.Error(message, StatusCodes.Status400BadRequest);

    private static IResult Unauthorized(string message) => Envelopes.A.Error(message, StatusCodes.Status401Unauthorized);
}
