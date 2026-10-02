using System.Globalization;
using System.Text;
using MeuSecretario.Application.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MeuSecretario.Infrastructure.Security;

/// <summary>
/// HS256 com <c>API_SECRET_KEY</c>, claims <c>user_id</c> (inteiro), <c>iat</c> e <c>exp</c>; sem iss/aud/sub/jti.
/// Validação equivalente à do PyJWT: só HS256, assinatura e <c>exp</c> sem tolerância de relógio (exp == agora já expira).
/// Não usa o middleware JwtBearer: o filtro <c>RequireUser</c> precisa das 4 mensagens de 401 do Flask.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    public const int MinSecretLength = 32;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private const string UserIdClaim = "user_id";

    private readonly SymmetricSecurityKey _key;
    private readonly TimeProvider _timeProvider;
    // SetDefaultTimesOnTokenCreation = false: sem isso o handler injeta "nbf" com o relógio real; o PyJWT só emite iat e exp.
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false, SetDefaultTimesOnTokenCreation = false };

    public JwtTokenService(string secret, TimeProvider timeProvider)
    {
        if (secret is null || secret.Length < MinSecretLength)
        {
            // Sem o valor do segredo na mensagem.
            throw new ArgumentException($"API_SECRET_KEY deve ter no mínimo {MinSecretLength} caracteres.", nameof(secret));
        }

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _timeProvider = timeProvider;
    }

    public string Issue(int userId)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            // Claims como dicionário preserva user_id como número JSON (o PyJWT emite inteiro).
            Claims = new Dictionary<string, object> { [UserIdClaim] = userId },
            IssuedAt = now,
            Expires = now.Add(Lifetime),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        };
        return _handler.CreateToken(descriptor);
    }

    public JwtValidation Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return JwtValidation.Invalid;
        }

        try
        {
            var parameters = new TokenValidationParameters
            {
                IssuerSigningKey = _key,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = ValidateLifetime,
            };

            // ValidateTokenAsync é só CPU (sem I/O); sem contexto de sincronização no ASP.NET Core.
            var result = _handler.ValidateTokenAsync(token, parameters).GetAwaiter().GetResult();
            if (!result.IsValid)
            {
                return JwtValidation.Invalid;
            }

            return new JwtValidation(true, ReadUserId(result.Claims));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return JwtValidation.Invalid;
        }
    }

    private bool ValidateLifetime(DateTime? notBefore, DateTime? expires, SecurityToken token, TokenValidationParameters parameters)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // PyJWT: exp <= agora => expirado. Sem exp => rejeitado (RequireExpirationTime).
        return expires.HasValue && expires.Value > now && (!notBefore.HasValue || notBefore.Value <= now);
    }

    private static int? ReadUserId(IDictionary<string, object> claims)
    {
        if (!claims.TryGetValue(UserIdClaim, out var value))
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
