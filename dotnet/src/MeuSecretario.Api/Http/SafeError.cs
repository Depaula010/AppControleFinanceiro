namespace MeuSecretario.Api.Http;

/// <summary>
/// Descrição de exceção segura para log (desvio 008): só o tipo (e o código SQL, se houver). A mensagem pode
/// conter dados pessoais, como o <c>Detail</c> de uma violação de unicidade do PostgreSQL com o número de WhatsApp.
/// </summary>
public static class SafeError
{
    public static string Describe(Exception exception)
    {
        // Sem referência a Npgsql na Api (regra de arquitetura): o SqlState vem por reflexão do nome do tipo.
        var sqlState = exception.GetType().GetProperty("SqlState")?.GetValue(exception) as string;
        return sqlState is null ? exception.GetType().Name : $"{exception.GetType().Name} (SqlState {sqlState})";
    }
}
