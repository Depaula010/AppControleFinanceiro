namespace MeuSecretario.Application.Abstractions;

/// <param name="IsValid">Assinatura HS256, algoritmo e <c>exp</c> válidos.</param>
/// <param name="UserId">
/// Claim <c>user_id</c>. Nulo quando o token é válido mas não traz o claim (ou ele não é inteiro).
/// </param>
public sealed record JwtValidation(bool IsValid, int? UserId)
{
    public static JwtValidation Invalid { get; } = new(false, null);
}

/// <summary>JWT compatível com o PyJWT do Flask (spec 001, RN-001-01 a RN-001-03).</summary>
public interface IJwtTokenService
{
    /// <summary>Emite HS256 com <c>user_id</c> (inteiro), <c>iat</c> e <c>exp</c> (+24 h), em segundos.</summary>
    string Issue(int userId);

    /// <summary>Nunca lança: qualquer problema resulta em <see cref="JwtValidation.Invalid"/>.</summary>
    JwtValidation Validate(string token);
}
