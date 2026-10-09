using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CalendarAlerts;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.CalendarAlerts;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MeuSecretario.UnitTests.Application.CalendarAlerts;

public class CalendarAlertUseCasesTests
{
    private const int UserId = 7;

    private readonly ICalendarAlertConfigRepository _repository = Substitute.For<ICalendarAlertConfigRepository>();
    private readonly IGoogleAccountService _google = Substitute.For<IGoogleAccountService>();
    private readonly ICalendarAlertDiagnostics _diagnostics = Substitute.For<ICalendarAlertDiagnostics>();

    public CalendarAlertUseCasesTests()
    {
        _google.IsConnectedAsync(UserId, Arg.Any<CancellationToken>()).Returns(true);
        _repository.GetOrCreateAsync(UserId, Arg.Any<CancellationToken>()).Returns(new CalendarAlertConfig(false, 1));
        _repository.UpdateAsync(UserId, Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(call => new CalendarAlertConfig(call.ArgAt<bool?>(1) ?? false, call.ArgAt<int?>(2) ?? 1));
    }

    private static RawBody Body(params (string Key, RawValue Value)[] fields) =>
        new(fields.ToDictionary(f => f.Key, f => f.Value));

    private UpdateCalendarAlertConfig Update() => new(_repository, _google, _diagnostics);

    private static AlertRejection RejectionOf(AlertConfigResult result) => ((AlertConfigResult.Rejected)result).Reason;

    // ---------------------------------------------------------------- GET

    [Fact]
    [Trait("AC", "014-10")]
    public async Task Get_ReturnsTheRepositoryConfig()
    {
        var config = await new GetCalendarAlertConfig(_repository).ExecuteAsync(UserId, CancellationToken.None);

        config.ShouldBe(new CalendarAlertConfig(false, 1));
    }

    // ---------------------------------------------------------------- POST config

    [Theory]
    [Trait("AC", "014-20")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Config_WithoutParameters_IsRejected_AndDoesNotTouchGoogleOrTheDatabase(bool hasEmptyBody)
    {
        var body = hasEmptyBody ? Body(("ativo", RawValue.Null), ("minutos_antes", RawValue.Null)) : null;

        var result = await Update().ExecuteAsync(UserId, body, CancellationToken.None);

        RejectionOf(result).ShouldBe(AlertRejection.NoParameters);
        await _google.DidNotReceiveWithAnyArgs().IsConnectedAsync(default, default);
        await _repository.DidNotReceiveWithAnyArgs().GetOrCreateAsync(default, default);
    }

    [Fact]
    [Trait("AC", "014-21")]
    public async Task Config_ActivatingWithoutGoogle_IsRejected_BeforeAnyWrite()
    {
        _google.IsConnectedAsync(UserId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Update().ExecuteAsync(UserId, Body(("ativo", RawValue.Bool(true))), CancellationToken.None);

        RejectionOf(result).ShouldBe(AlertRejection.NotConnected);
        await _repository.DidNotReceiveWithAnyArgs().GetOrCreateAsync(default, default);
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default, default);
    }

    [Fact]
    [Trait("AC", "014-22")]
    public async Task Config_Activating_WithGoogle_Writes()
    {
        var result = await Update().ExecuteAsync(UserId, Body(("ativo", RawValue.Bool(true))), CancellationToken.None);

        var ok = result.ShouldBeOfType<AlertConfigResult.Ok>();
        ok.Message.ShouldBe("Alertas de tarefas ativados");
        ok.Config.ShouldBe(new CalendarAlertConfig(true, 1));
        await _repository.Received(1).UpdateAsync(UserId, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "014-23")]
    public async Task Config_Deactivating_DoesNotAskGoogle()
    {
        var result = await Update().ExecuteAsync(UserId, Body(("ativo", RawValue.Bool(false))), CancellationToken.None);

        result.ShouldBeOfType<AlertConfigResult.Ok>().Message.ShouldBe("Alertas de tarefas desativados");
        await _google.DidNotReceiveWithAnyArgs().IsConnectedAsync(default, default);
    }

    [Fact]
    [Trait("AC", "014-24")]
    public async Task Config_OnlyMinutes_DoesNotAskGoogle_AndLeavesActiveAlone()
    {
        var result = await Update().ExecuteAsync(UserId, Body(("minutos_antes", RawValue.Whole(5))), CancellationToken.None);

        result.ShouldBeOfType<AlertConfigResult.Ok>().Message.ShouldBe("alerta configurado para 5 minuto(s) antes");
        await _google.DidNotReceiveWithAnyArgs().IsConnectedAsync(default, default);
        await _repository.Received(1).UpdateAsync(UserId, null, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "014-25")]
    public async Task Config_ActiveAndMinutes_JoinsTheMessage()
    {
        var result = await Update().ExecuteAsync(
            UserId, Body(("ativo", RawValue.Bool(true)), ("minutos_antes", RawValue.Whole(10))), CancellationToken.None);

        result.ShouldBeOfType<AlertConfigResult.Ok>().Message
            .ShouldBe("Alertas de tarefas ativados e alerta configurado para 10 minuto(s) antes");
    }

    [Theory]
    [Trait("AC", "014-26")]
    [InlineData(0)]
    [InlineData(61)]
    public async Task Config_InvalidMinutes_IsRejected_AfterGetOrCreate_AndWithoutUpdate(int minutes)
    {
        var result = await Update().ExecuteAsync(UserId, Body(("minutos_antes", RawValue.Whole(minutes))), CancellationToken.None);

        RejectionOf(result).ShouldBe(AlertRejection.InvalidMinutes);
        await _repository.Received(1).GetOrCreateAsync(UserId, Arg.Any<CancellationToken>());
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default, default);
    }

    [Fact]
    [Trait("AC", "014-27")]
    public async Task Config_NonBooleanActive_IsRejectedAsInvalidActive_BeforeGoogleAndBeforeCreatingTheRow()
    {
        // Desvio 120: texto "true"/"false" também é recusado.
        foreach (var value in new[] { RawValue.Text("abc"), RawValue.Text("true"), RawValue.Whole(1) })
        {
            var result = await Update().ExecuteAsync(UserId, Body(("ativo", value)), CancellationToken.None);

            RejectionOf(result).ShouldBe(AlertRejection.InvalidActive);
        }

        await _google.DidNotReceiveWithAnyArgs().IsConnectedAsync(default, default);
        await _repository.DidNotReceiveWithAnyArgs().GetOrCreateAsync(default, default);
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default, default);
    }

    [Fact]
    [Trait("AC", "014-28")]
    public async Task Config_FalsyNonBooleanActive_SkipsGoogleAndIsInvalidActive()
    {
        var result = await Update().ExecuteAsync(
            UserId, Body(("ativo", RawValue.Whole(0)), ("minutos_antes", RawValue.Whole(3))), CancellationToken.None);

        RejectionOf(result).ShouldBe(AlertRejection.InvalidActive);
        await _google.DidNotReceiveWithAnyArgs().IsConnectedAsync(default, default);
    }

    [Fact]
    [Trait("AC", "014-29")]
    public async Task Config_UpdateThrowing_BecomesUpdateFailed_AndIsLogged()
    {
        var boom = new InvalidOperationException("detalhe interno");
        _repository.UpdateAsync(UserId, Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).ThrowsAsync(boom);

        var result = await Update().ExecuteAsync(UserId, Body(("ativo", RawValue.Bool(false))), CancellationToken.None);

        RejectionOf(result).ShouldBe(AlertRejection.UpdateFailed);
        _diagnostics.Received(1).ConfigUpdateFailed(UserId, boom);
    }

    [Fact]
    [Trait("AC", "014-29")]
    public async Task Config_GoogleThrowing_Propagates_SoTheRouteAnswers500()
    {
        _google.IsConnectedAsync(UserId, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => Update().ExecuteAsync(UserId, Body(("ativo", RawValue.Bool(true))), CancellationToken.None));
    }

    // ---------------------------------------------------------------- ativar

    [Fact]
    [Trait("AC", "014-30")]
    public async Task Activate_WithoutGoogle_IsRejected_AndNeverWrites()
    {
        _google.IsConnectedAsync(UserId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await new ActivateCalendarAlerts(_repository, _google, _diagnostics)
            .ExecuteAsync(UserId, null, CancellationToken.None);

        RejectionOf(result).ShouldBe(AlertRejection.NotConnected);
        await _repository.DidNotReceiveWithAnyArgs().GetOrCreateAsync(default, default);
    }

    [Fact]
    [Trait("AC", "014-30")]
    public async Task Activate_WithoutBody_KeepsTheMinutes()
    {
        var result = await new ActivateCalendarAlerts(_repository, _google, _diagnostics)
            .ExecuteAsync(UserId, null, CancellationToken.None);

        result.ShouldBeOfType<AlertConfigResult.Ok>().Message.ShouldBe("Alertas de tarefas ativados");
        await _repository.Received(1).UpdateAsync(UserId, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "014-31")]
    public async Task Activate_WithMinutes_UpdatesBoth_AndInvalidMinutesAreRejected()
    {
        var useCase = new ActivateCalendarAlerts(_repository, _google, _diagnostics);

        var ok = await useCase.ExecuteAsync(UserId, Body(("minutos_antes", RawValue.Whole(7))), CancellationToken.None);
        var bad = await useCase.ExecuteAsync(UserId, Body(("minutos_antes", RawValue.Whole(0))), CancellationToken.None);

        ok.ShouldBeOfType<AlertConfigResult.Ok>().Message
            .ShouldBe("Alertas de tarefas ativados e alerta configurado para 7 minuto(s) antes");
        RejectionOf(bad).ShouldBe(AlertRejection.InvalidMinutes);
    }

    // ---------------------------------------------------------------- desativar

    [Fact]
    [Trait("AC", "014-32")]
    public async Task Deactivate_WritesFalse_WithoutAskingGoogle()
    {
        var result = await new DeactivateCalendarAlerts(_repository, _diagnostics).ExecuteAsync(UserId, CancellationToken.None);

        result.ShouldBeOfType<AlertConfigResult.Ok>().Message.ShouldBe("Alertas de tarefas desativados");
        await _repository.Received(1).GetOrCreateAsync(UserId, Arg.Any<CancellationToken>());
        await _repository.Received(1).UpdateAsync(UserId, false, null, Arg.Any<CancellationToken>());
        await _google.DidNotReceiveWithAnyArgs().IsConnectedAsync(default, default);
    }
}
