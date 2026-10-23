using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Application.Ops;
using MeuSecretario.Infrastructure.Configuration;
using MeuSecretario.Infrastructure.Ops;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Ops;

/// <summary>Avisos operacionais para o dono (desvio 261): falha de job vira WhatsApp, no máximo um por origem a cada 30 min.</summary>
public class OpsAlertsTests
{
    private const string Owner = "5531999990000";

    private readonly FakeTimeProvider _time = new();
    private readonly IBotClient _bot = Substitute.For<IBotClient>();

    public OpsAlertsTests()
    {
        _time.SetUtcNow(DateTimeOffset.Parse("2026-10-23T23:00:00-03:00", System.Globalization.CultureInfo.InvariantCulture));
        _bot.SendTextAsync(default!, default!, default).ReturnsForAnyArgs(true);
    }

    private OpsAlertWorker Worker(OpsAlertChannel? channel = null) =>
        new(channel ?? new OpsAlertChannel(true), _bot, Owner, _time, NullLogger<OpsAlertWorker>.Instance);

    [Fact]
    public void Throttle_OnePerSourceInTheWindow_CountsTheRest_AndReportsThemOnTheNextSend()
    {
        var throttle = new AlertThrottle(_time, TimeSpan.FromMinutes(30));

        throttle.TryAcquire("CHECKIN-NOTURNO", out var first).ShouldBeTrue();
        first.ShouldBe(0);
        throttle.TryAcquire("CHECKIN-NOTURNO", out _).ShouldBeFalse();
        throttle.TryAcquire("CHECKIN-NOTURNO", out _).ShouldBeFalse();
        throttle.TryAcquire("MOTOR-AGENDAMENTOS", out _).ShouldBeTrue();   // outra origem não é afetada

        _time.Advance(TimeSpan.FromMinutes(31));

        throttle.TryAcquire("CHECKIN-NOTURNO", out var afterWindow).ShouldBeTrue();
        afterWindow.ShouldBe(2);
        throttle.TryAcquire("CHECKIN-NOTURNO", out var again).ShouldBeFalse();
        again.ShouldBe(0);
    }

    [Fact]
    public void JobLog_OnlyFailuresRaiseAnAlert_AndEverythingStillReachesTheLog()
    {
        var inner = Substitute.For<IJobLog>();
        var alerts = Substitute.For<IOpsAlerts>();
        var log = new AlertingJobLog(inner, alerts);

        log.Info("CHECKIN-NOTURNO", "ok");
        log.Warning("CHECKIN-NOTURNO", "atenção");
        log.Failure("MOTOR-AGENDAMENTOS", "ERRO ao processar Agendamento ID 7: InvalidOperationException");

        inner.Received(1).Info("CHECKIN-NOTURNO", "ok");
        inner.Received(1).Warning("CHECKIN-NOTURNO", "atenção");
        inner.Received(1).Failure("MOTOR-AGENDAMENTOS", "ERRO ao processar Agendamento ID 7: InvalidOperationException");
        alerts.Received(1).Raise("MOTOR-AGENDAMENTOS", "ERRO ao processar Agendamento ID 7: InvalidOperationException");
        alerts.ReceivedWithAnyArgs(1).Raise(default!, default!);
    }

    [Fact]
    public async Task Worker_SendsTheAlertToTheOwner_OncePerWindow_AndMentionsWhatWasHeld()
    {
        var worker = Worker();

        await worker.ProcessAsync(new OpsAlert("CHECKIN-NOTURNO", "Falha ao enviar o check-in para usuário 1; sessão removida"), CancellationToken.None);
        await worker.ProcessAsync(new OpsAlert("CHECKIN-NOTURNO", "outra falha"), CancellationToken.None);
        await worker.ProcessAsync(new OpsAlert("CHECKIN-NOTURNO", "e outra"), CancellationToken.None);

        await _bot.Received(1).SendTextAsync(
            Owner, "🚨 *ALERTA - CHECKIN-NOTURNO*\nFalha ao enviar o check-in para usuário 1; sessão removida\n_23/10 23:00_", Arg.Any<CancellationToken>());

        _time.Advance(OpsAlertWorker.Window + TimeSpan.FromMinutes(1));
        await worker.ProcessAsync(new OpsAlert("CHECKIN-NOTURNO", "voltou a falhar"), CancellationToken.None);

        await _bot.Received(1).SendTextAsync(
            Owner,
            Arg.Is<string>(text => text.Contains("voltou a falhar", StringComparison.Ordinal) && text.EndsWith("(+2 falha(s) desta origem desde o último aviso)", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Worker_ABotFailureOrException_NeverBreaksTheJob()
    {
        _bot.SendTextAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult(false));
        var worker = Worker();
        await worker.ProcessAsync(new OpsAlert("A", "falha"), CancellationToken.None);

        _bot.SendTextAsync(default!, default!, default).ReturnsForAnyArgs<Task<bool>>(_ => throw new HttpRequestException("bot fora"));
        await worker.ProcessAsync(new OpsAlert("B", "falha"), CancellationToken.None);
    }

    [Fact]
    public async Task Channel_WithoutADestination_DoesNothing_AndWithOneDeliversInTheBackground()
    {
        var off = new OpsAlertChannel(false);
        off.Raise("A", "falha");
        off.Reader.TryRead(out _).ShouldBeFalse();

        var on = new OpsAlertChannel(true);
        var worker = Worker(on);
        var delivered = new TaskCompletionSource();
        _bot.SendTextAsync(default!, default!, default).ReturnsForAnyArgs(_ =>
        {
            delivered.TrySetResult();
            return Task.FromResult(true);
        });

        await worker.StartAsync(CancellationToken.None);
        on.Raise("MOTOR-AGENDAMENTOS", "ERRO durante execução: TimeoutException");
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        await _bot.Received(1).SendTextAsync(Owner, Arg.Is<string>(t => t.Contains("MOTOR-AGENDAMENTOS", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("+55 (31) 99999-0000", "5531999990000")]
    [InlineData("5531999990000", "5531999990000")]
    public void Setting_KeepsOnlyTheDigits(string? configured, string expected)
    {
        var values = new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://u:p@h:5432/db",
            ["API_SECRET_KEY"] = "0123456789abcdef0123456789abcdef",
            ["ENCRYPTION_KEY"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            ["GEMINI_API_KEY"] = "gemini",
            ["REDIS_URL"] = "redis://redis:6379",
            ["OPS_ALERT_WHATSAPP"] = configured,
        };

        AppSettings.Load(key => values.GetValueOrDefault(key)).OpsAlertWhatsapp.ShouldBe(expected);
    }
}
