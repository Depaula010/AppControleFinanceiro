using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CoachAgenda;
using MeuSecretario.Domain.CoachAgenda;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application;

/// <summary>Spec 015 (T3): validação da entrada, Google fora do ar, tempo limite e nível por calendário (AC-015-03, 05, 10).</summary>
public sealed class CoachAgendaUseCaseTests
{
    private readonly IGoogleAccountService _accounts = Substitute.For<IGoogleAccountService>();
    private readonly ICalendarClient _calendar = Substitute.For<ICalendarClient>();
    private readonly ICoachAgendaConfig _config = Substitute.For<ICoachAgendaConfig>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public CoachAgendaUseCaseTests()
    {
        _accounts.IsConnectedAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        _config.NivelDeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(NivelDeDetalhe.Completo);
        _config.MascararAsync(Arg.Any<CancellationToken>()).Returns(true);
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 10, 25, 21, 0, 0, TimeSpan.Zero));
        _clock.TimeZone.Returns(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
    }

    private ConsultarAgendaDoCoach Caso() => new(_accounts, _calendar, _config, _clock);

    private static CalendarEvent Ev(string id, string inicio, string calendario = "cal-1", string titulo = "Reunião") =>
        new(id, titulo, inicio, inicio, "Sala", "Pauta", false, "Trabalho", calendario);

    [Theory]
    [InlineData(null, "2026-11-08")]
    [InlineData("2026-10-26", null)]
    [InlineData("26/10/2026", "2026-11-08")]
    [InlineData("2026-13-40", "2026-11-08")]
    [InlineData("2026-11-08", "2026-10-26")]
    [InlineData("2026-10-26", "2026-12-07")]          // 43 dias
    public async Task AC_015_03_Entrada_invalida_nao_chama_o_Google(string? de, string? ate)
    {
        var r = await Caso().ExecuteAsync(7, de, ate, default);

        r.Status.ShouldBe(StatusDaAgendaDoCoach.EntradaInvalida);
        await _calendar.DidNotReceiveWithAnyArgs().GetEventsInRangeAsync(default, default, default, default);
    }

    [Fact]
    public async Task Exatamente_42_dias_e_aceito()
    {
        _calendar.GetEventsInRangeAsync(7, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);

        (await Caso().ExecuteAsync(7, "2026-10-26", "2026-12-06", default)).Status.ShouldBe(StatusDaAgendaDoCoach.Ok);
    }

    [Fact]
    public async Task AC_015_10_Google_nao_conectado_vira_NaoConectado()
    {
        _accounts.IsConnectedAsync(7, Arg.Any<CancellationToken>()).Returns(false);

        (await Caso().ExecuteAsync(7, "2026-10-26", "2026-11-08", default)).Status.ShouldBe(StatusDaAgendaDoCoach.NaoConectado);
        await _calendar.DidNotReceiveWithAnyArgs().GetEventsInRangeAsync(default, default, default, default);
    }

    [Fact]
    public async Task AC_015_10_Excecao_de_credencial_tambem_vira_NaoConectado()
    {
        _calendar.GetEventsInRangeAsync(7, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<CalendarEvent>>(_ => throw new GoogleNotConnectedException("x"));

        (await Caso().ExecuteAsync(7, "2026-10-26", "2026-11-08", default)).Status.ShouldBe(StatusDaAgendaDoCoach.NaoConectado);
    }

    [Fact]
    public async Task AC_015_10_Falha_do_Google_vira_Indisponivel_sem_vazar_a_excecao()
    {
        _calendar.GetEventsInRangeAsync(7, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CalendarEvent>>(_ => throw new CalendarApiException("conteúdo secreto do evento", System.Net.HttpStatusCode.InternalServerError));

        var r = await Caso().ExecuteAsync(7, "2026-10-26", "2026-11-08", default);

        r.Status.ShouldBe(StatusDaAgendaDoCoach.Indisponivel);
        r.Agenda.ShouldBeNull();
    }

    [Fact]
    public async Task AC_015_10_Estourar_o_tempo_limite_vira_Indisponivel()
    {
        _calendar.GetEventsInRangeAsync(7, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CalendarEvent>>(_ => throw new OperationCanceledException());          // o teto de 10 s cancela o token interno, não o do chamador

        (await Caso().ExecuteAsync(7, "2026-10-26", "2026-11-08", default)).Status.ShouldBe(StatusDaAgendaDoCoach.Indisponivel);
    }

    [Fact]
    public async Task Cancelar_a_chamada_de_fora_continua_cancelando()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _calendar.GetEventsInRangeAsync(7, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<CalendarEvent>>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => Caso().ExecuteAsync(7, "2026-10-26", "2026-11-08", cts.Token));
    }

    [Fact]
    public async Task AC_015_05_O_nivel_do_calendario_vem_da_configuracao_e_o_resultado_sai_ordenado()
    {
        _calendar.GetEventsInRangeAsync(7, new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 8), Arg.Any<CancellationToken>())
            .Returns([Ev("a", "2026-10-27T15:00:00-03:00", "trabalho"), Ev("b", "2026-10-27T08:00:00-03:00", "pessoal")]);
        _config.NivelDeAsync("trabalho", Arg.Any<CancellationToken>()).Returns(NivelDeDetalhe.Ocupado);

        var r = await Caso().ExecuteAsync(7, "2026-10-26", "2026-11-08", default);

        r.Status.ShouldBe(StatusDaAgendaDoCoach.Ok);
        r.Agenda!.Eventos.Select(e => (e.Inicio, e.Nivel, e.Titulo)).ShouldBe(
        [
            ("2026-10-27T08:00:00-03:00", NivelDeDetalhe.Completo, "Reunião"),
            ("2026-10-27T15:00:00-03:00", NivelDeDetalhe.Ocupado, null),
        ]);
        (r.Agenda.De, r.Agenda.Ate, r.Agenda.GeradoEm).ShouldBe(("2026-10-26", "2026-11-08", "2026-10-25T18:00:00-03:00"));
    }

    [Fact]
    public async Task O_nivel_de_cada_calendario_e_pedido_uma_vez_so()
    {
        _calendar.GetEventsInRangeAsync(7, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([Ev("a", "2026-10-27T08:00:00-03:00"), Ev("b", "2026-10-27T09:00:00-03:00"), Ev("c", "2026-10-27T10:00:00-03:00")]);

        await Caso().ExecuteAsync(7, "2026-10-26", "2026-11-08", default);

        await _config.Received(1).NivelDeAsync("cal-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC_015_07_O_mascaramento_segue_a_configuracao()
    {
        _calendar.GetEventsInRangeAsync(7, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Ev("a", "2026-10-27T08:00:00-03:00", titulo: "Call https://zoom.us/j/9")]);

        (await Caso().ExecuteAsync(7, "2026-10-26", "2026-11-08", default)).Agenda!.Eventos[0].Titulo.ShouldBe("Call [link]");

        _config.MascararAsync(Arg.Any<CancellationToken>()).Returns(false);
        (await Caso().ExecuteAsync(7, "2026-10-26", "2026-11-08", default)).Agenda!.Eventos[0].Titulo.ShouldBe("Call https://zoom.us/j/9");
    }
}
