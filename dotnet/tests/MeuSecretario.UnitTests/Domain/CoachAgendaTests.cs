using MeuSecretario.Domain.CoachAgenda;

namespace MeuSecretario.UnitTests.Domain;

/// <summary>Spec 015 (T1 e T2): níveis, mascaramento e montagem da agenda para o Coach (AC-015-04 a 09, 12).</summary>
public sealed class CoachAgendaTests
{
    [Theory]
    [InlineData("Link: https://meet.google.com/abc-defg-hij ok", "Link: [link] ok")]
    [InlineData("Entrar em www.empresa.com.br/sala agora", "Entrar em [link] agora")]
    [InlineData("Sala zoom.us/j/123456789 às 10h", "Sala [link] às 10h")]
    [InlineData("Teams: teams.microsoft.com/l/meetup-join/xyz", "Teams: [link]")]
    [InlineData("Fale com maria.silva+rh@empresa.com.br hoje", "Fale com [e-mail] hoje")]
    [InlineData("Ligar (11) 98765-4321 ou +55 21 3456-7890", "Ligar [telefone] ou [telefone]")]
    [InlineData("WhatsApp 11987654321", "WhatsApp [telefone]")]
    [InlineData("senha: abc123 e PIN=4455", "senha: [código] e PIN=[código]")]
    [InlineData("Código de acesso: 998877", "Código de acesso: [código]")]
    [InlineData("Protocolo 4839201 anexo", "Protocolo [código] anexo")]
    public void AC_015_07_Mascara_link_email_telefone_e_codigo(string entrada, string esperado)
    {
        MascaradorDeTexto.Mascarar(entrada).ShouldBe(esperado);
    }

    [Theory]
    [InlineData("Reunião de planejamento às 9h30")]
    [InlineData("Academia: perna A, 60 min")]
    [InlineData("27/10/2026 15:00")]
    [InlineData("R$ 1500,00 do aluguel")]
    [InlineData("")]
    public void Texto_sem_nada_sensivel_sai_igual(string entrada)
    {
        MascaradorDeTexto.Mascarar(entrada).ShouldBe(entrada);
    }

    [Fact]
    public void Texto_nulo_vira_vazio()
    {
        MascaradorDeTexto.Mascarar(null).ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("completo", NivelDeDetalhe.Completo)]
    [InlineData("COMPLETO", NivelDeDetalhe.Completo)]
    [InlineData("titulos", NivelDeDetalhe.Titulos)]
    [InlineData("títulos", NivelDeDetalhe.Titulos)]
    [InlineData("ocupado", NivelDeDetalhe.Ocupado)]
    [InlineData("qualquer coisa", NivelDeDetalhe.Ocupado)]
    [InlineData("", NivelDeDetalhe.Ocupado)]
    [InlineData(null, NivelDeDetalhe.Ocupado)]
    public void AC_015_06_Nivel_desconhecido_vale_o_mais_privado(string? valor, NivelDeDetalhe esperado)
    {
        NivelDeDetalheTexto.Ler(valor).ShouldBe(esperado);
    }

    private static EventoBruto Evento(string inicio = "2026-10-27T09:00:00-03:00", string fim = "2026-10-27T10:00:00-03:00", bool diaInteiro = false,
        string titulo = "Reunião", string descricao = "Pauta do dia", string local = "Sala 3", string calendario = "Trabalho", string idCalendario = "cal-1") =>
        new(titulo, inicio, fim, local, descricao, diaInteiro, calendario, idCalendario);

    [Fact]
    public void AC_015_04_Cada_nivel_inclui_so_os_seus_campos()
    {
        var eventos = new[] { Evento() };

        var ocupado = AgendaParaOCoach.Montar(eventos, _ => NivelDeDetalhe.Ocupado, true).Single();
        var titulos = AgendaParaOCoach.Montar(eventos, _ => NivelDeDetalhe.Titulos, true).Single();
        var completo = AgendaParaOCoach.Montar(eventos, _ => NivelDeDetalhe.Completo, true).Single();

        (ocupado.Titulo, ocupado.Descricao, ocupado.Local, ocupado.Calendario).ShouldBe((null, null, null, null));
        (titulos.Titulo, titulos.Descricao, titulos.Local, titulos.Calendario).ShouldBe(("Reunião", null, null, null));
        (completo.Titulo, completo.Descricao, completo.Local, completo.Calendario).ShouldBe(("Reunião", "Pauta do dia", "Sala 3", "Trabalho"));
    }

    [Fact]
    public void O_nivel_vale_por_calendario()
    {
        var eventos = new[] { Evento(idCalendario: "trabalho"), Evento(idCalendario: "pessoal", inicio: "2026-10-27T11:00:00-03:00") };

        var r = AgendaParaOCoach.Montar(eventos, id => id == "trabalho" ? NivelDeDetalhe.Ocupado : NivelDeDetalhe.Completo, true);

        r.Select(e => e.Nivel).ShouldBe([NivelDeDetalhe.Ocupado, NivelDeDetalhe.Completo]);
        r[0].Titulo.ShouldBeNull();
        r[1].Titulo.ShouldBe("Reunião");
    }

    [Fact]
    public void AC_015_07_O_mascaramento_vale_em_titulo_descricao_e_local_e_pode_ser_desligado()
    {
        var e = Evento(titulo: "Call https://zoom.us/j/1", descricao: "Ligue (11) 98765-4321", local: "maria@x.com");

        var ligado = AgendaParaOCoach.Montar([e], _ => NivelDeDetalhe.Completo, true).Single();
        var desligado = AgendaParaOCoach.Montar([e], _ => NivelDeDetalhe.Completo, false).Single();

        (ligado.Titulo, ligado.Descricao, ligado.Local).ShouldBe(("Call [link]", "Ligue [telefone]", "[e-mail]"));
        (desligado.Titulo, desligado.Descricao, desligado.Local).ShouldBe(("Call https://zoom.us/j/1", "Ligue (11) 98765-4321", "maria@x.com"));
    }

    [Fact]
    public void AC_015_08_Limites_de_tamanho_valem_depois_do_mascaramento_e_cortam_com_reticencias()
    {
        var e = Evento(titulo: new string('t', 300), descricao: new string('d', 2000), local: new string('l', 500));

        var r = AgendaParaOCoach.Montar([e], _ => NivelDeDetalhe.Completo, true).Single();

        (r.Titulo!.Length, r.Descricao!.Length, r.Local!.Length).ShouldBe((121, 601, 121));
        r.Titulo.ShouldEndWith("…");
        AgendaParaOCoach.Montar([Evento(titulo: new string('t', 120))], _ => NivelDeDetalhe.Titulos, true).Single().Titulo!.Length.ShouldBe(120);
    }

    [Fact]
    public void AC_015_08_Uma_marca_nunca_e_cortada_pela_metade()
    {
        // 110 letras + um link longo: mascarado vira " [link]" (117 no total) e cabe; cortar ANTES de mascarar deixaria o endereço pela metade
        var e = Evento(titulo: new string('a', 110) + " https://meet.google.com/aaaa-bbbb-cccc-dddd-eeee");

        AgendaParaOCoach.Montar([e], _ => NivelDeDetalhe.Titulos, true).Single().Titulo.ShouldBe(new string('a', 110) + " [link]");
    }

    [Fact]
    public void AC_015_09_Dia_inteiro_sai_so_com_a_data_e_com_hora_sai_com_o_deslocamento_de_Sao_Paulo()
    {
        var diaInteiro = Evento(inicio: "2026-10-28", fim: "2026-10-29", diaInteiro: true);
        var emUtc = Evento(inicio: "2026-10-27T12:00:00Z", fim: "2026-10-27T13:30:00Z");

        var r = AgendaParaOCoach.Montar([diaInteiro, emUtc], _ => NivelDeDetalhe.Ocupado, true);

        r[0].ShouldBe(new EventoParaOCoach("2026-10-27T09:00:00-03:00", "2026-10-27T10:30:00-03:00", false, NivelDeDetalhe.Ocupado, null, null, null, null));
        (r[1].Inicio, r[1].Fim, r[1].DiaInteiro).ShouldBe(("2026-10-28", "2026-10-29", true));
    }

    [Fact]
    public void Sai_ordenado_por_inicio()
    {
        var r = AgendaParaOCoach.Montar(
            [Evento(inicio: "2026-10-27T15:00:00-03:00"), Evento(inicio: "2026-10-27T08:00:00-03:00"), Evento(inicio: "2026-10-26T20:00:00-03:00")],
            _ => NivelDeDetalhe.Ocupado, true);

        r.Select(e => e.Inicio).ShouldBe(["2026-10-26T20:00:00-03:00", "2026-10-27T08:00:00-03:00", "2026-10-27T15:00:00-03:00"]);
    }

    [Fact]
    public void AC_015_12_O_evento_de_saida_nao_tem_como_levar_id_nem_participantes()
    {
        var campos = typeof(EventoParaOCoach).GetProperties().Select(p => p.Name).ToList();
        var entrada = typeof(EventoBruto).GetProperties().Select(p => p.Name).ToList();

        campos.ShouldNotContain(nome => nome.Contains("Id", StringComparison.Ordinal) || nome.Contains("Particip", StringComparison.OrdinalIgnoreCase) || nome.Contains("Email", StringComparison.OrdinalIgnoreCase));
        entrada.ShouldNotContain("Id");                                                        // só o id do CALENDÁRIO entra, para achar o nível
        entrada.ShouldNotContain("IdDoEvento");
    }
}
