using MeuSecretario.Application.CoachAgenda;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// "chave do coach" (spec 016, Q-016-1): gera a chave da agenda de quem escreveu e responde com ela <b>uma única vez</b>. A pessoa cola a chave no
/// Coach (Fichas → Conexões e chaves) e não precisa fazer mais nada aqui. Gerar de novo revoga a anterior. Não chama o Gemini e nada da chave vai para log.
/// </summary>
public sealed class CoachKeyStep(IChavesDaAgenda chaves) : IWhatsAppStep
{
    public int Order => StepOrder.CoachKey;

    public async Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CoachKeyCommand.IsKeyRequest(context.Message))
        {
            return null;
        }

        var nova = await chaves.GerarAsync(context.User.Id, cancellationToken).ConfigureAwait(false);
        return $"""
            🔑 *Chave da agenda para o Coach*

            {nova.Chave}

            Copie a chave e cole no Coach, em Fichas → Conexões e chaves, depois toque em "Testar conexão". Não precisa fazer mais nada por aqui.

            Ela aparece só desta vez e vale só para ler a sua agenda. Se pedir outra, esta deixa de valer. Apague esta mensagem depois de colar.
            """;
    }
}
