using MeuSecretario.Api.Http;

namespace MeuSecretario.Api.Contracts;

/// <summary>
/// Os cinco formatos de resposta do Flask (specs/foundation/api-contract.md §1).
/// Cada rota escolhe o seu; nunca unificar. As chaves saem na ordem de inserção.
/// </summary>
public static class Envelopes
{
    internal static IResult Json(Dictionary<string, object?> body, int statusCode) =>
        Results.Json(body, JsonDefaults.Options, contentType: null, statusCode: statusCode);

    /// <summary>Envelope A — <c>/auth/*</c> e <c>/api/*</c> com JWT.</summary>
    public static class A
    {
        public static IResult Success(object? data = null, string? message = null, int statusCode = StatusCodes.Status200OK)
        {
            var body = new Dictionary<string, object?> { ["status"] = "success" };
            if (message is not null)
            {
                body["message"] = message;
            }
            if (data is not null)
            {
                body["data"] = data;
            }
            return Json(body, statusCode);
        }

        public static IResult Error(string message, int statusCode) =>
            Json(new Dictionary<string, object?> { ["status"] = "error", ["message"] = message }, statusCode);
    }

    /// <summary>Envelope B — admin, webhooks e calendar-alerts; extras ficam na raiz.</summary>
    public static class B
    {
        public static IResult Success(string mensagem, IReadOnlyDictionary<string, object?>? extras = null, int statusCode = StatusCodes.Status200OK)
        {
            var body = new Dictionary<string, object?> { ["status"] = "sucesso", ["mensagem"] = mensagem };
            if (extras is not null)
            {
                foreach (var (key, value) in extras)
                {
                    body[key] = value;
                }
            }
            return Json(body, statusCode);
        }

        public static IResult Error(string mensagem, int statusCode) =>
            Json(new Dictionary<string, object?> { ["status"] = "erro", ["mensagem"] = mensagem }, statusCode);
    }

    /// <summary>Envelope C — <c>/api-keys/*</c>: sem <c>status</c>; o erro leva título e detalhe.</summary>
    public static class C
    {
        public static IResult Success(IReadOnlyDictionary<string, object?> body, int statusCode = StatusCodes.Status200OK) =>
            Json(new Dictionary<string, object?>(body), statusCode);

        public static IResult Error(string erro, string mensagem, int statusCode) =>
            Json(new Dictionary<string, object?> { ["erro"] = erro, ["mensagem"] = mensagem }, statusCode);
    }

    /// <summary>Envelope D — filtro de segurança.</summary>
    public static class D
    {
        public static IResult Error(string error, string message, int statusCode) =>
            Json(new Dictionary<string, object?> { ["error"] = error, ["message"] = message }, statusCode);
    }

    /// <summary>Envelope E — <c>/webhook-whatsapp</c>: o texto vai em <c>resposta</c>; o 400 usa <c>mensagem</c>.</summary>
    public static class E
    {
        public static IResult Success(string resposta) =>
            Json(new Dictionary<string, object?> { ["status"] = "sucesso", ["resposta"] = resposta }, StatusCodes.Status200OK);

        public static IResult Error(string resposta, int statusCode) =>
            Json(new Dictionary<string, object?> { ["status"] = "erro", ["resposta"] = resposta }, statusCode);

        public static IResult BadRequest(string mensagem) =>
            Json(new Dictionary<string, object?> { ["status"] = "erro", ["mensagem"] = mensagem }, StatusCodes.Status400BadRequest);
    }
}
