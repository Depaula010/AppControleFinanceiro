using MeuSecretario.Application.CoachAgenda;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>Spec 016 (Q-016-1): "chave do coach" pelo WhatsApp gera a chave da própria pessoa e mais nada.</summary>
public class CoachKeyStepTests
{
    private readonly IChavesDaAgenda _chaves = Substitute.For<IChavesDaAgenda>();
    private readonly CoachKeyStep _step;

    public CoachKeyStepTests() => _step = new CoachKeyStep(_chaves);

    private static PipelineContext Context(string message, int userId = 7) =>
        new(new WhatsAppUser(userId, "Rafael"), "5531999990001", message, message.ToLowerInvariant(), null);

    [Theory]
    [InlineData("chave do coach")]
    [InlineData("Chave do Coach")]
    [InlineData("  chave da agenda  ")]
    [InlineData("gerar chave do coach!")]
    [InlineData("chave   da agenda do coach")]
    public void Reconhece_as_frases_do_pedido(string mensagem)
    {
        CoachKeyCommand.IsKeyRequest(mensagem).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("chave")]
    [InlineData("qual a chave do pix")]
    [InlineData("paguei 50 de chave do coach")]
    [InlineData("minha chave")]
    public void Qualquer_outra_frase_passa_direto(string mensagem)
    {
        CoachKeyCommand.IsKeyRequest(mensagem).ShouldBeFalse();
    }

    [Fact]
    public async Task Outra_mensagem_nao_gera_nada()
    {
        (await _step.HandleAsync(Context("qual o saldo?"), CancellationToken.None)).ShouldBeNull();

        await _chaves.DidNotReceiveWithAnyArgs().GerarAsync(default, default);
    }

    [Fact]
    public async Task O_pedido_gera_a_chave_de_quem_escreveu_e_responde_com_ela_uma_vez()
    {
        _chaves.GerarAsync(9, Arg.Any<CancellationToken>()).Returns(new ChaveGerada("abc123def456", "f456", DateTime.UtcNow));

        var resposta = await _step.HandleAsync(Context("chave do coach", userId: 9), CancellationToken.None);

        resposta.ShouldNotBeNull();
        resposta.ShouldContain("abc123def456");
        resposta.ShouldContain("Fichas → Conexões e chaves");
        await _chaves.Received(1).GerarAsync(9, Arg.Any<CancellationToken>());
        await _chaves.DidNotReceive().GerarAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void O_passo_vem_depois_dos_relatorios_e_antes_dos_comandos_de_transacao()
    {
        _step.Order.ShouldBeGreaterThan(StepOrder.Reports);
        _step.Order.ShouldBeLessThan(StepOrder.TransactionReply);
    }
}
