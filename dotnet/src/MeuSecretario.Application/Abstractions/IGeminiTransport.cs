namespace MeuSecretario.Application.Abstractions;

/// <summary>Ponte HTTP para o <c>generateContent</c> do Gemini (só a 010c implementa). A chave vai só na requisição.</summary>
public interface IGeminiTransport
{
    Task<string> GenerateAsync(string prompt, string apiKey, CancellationToken cancellationToken);
}
