using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Admin;
using MeuSecretario.Application.Security;
using MeuSecretario.Domain.Security;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Admin;

public class SecurityAdminUseCasesTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 10, 0, 0);

    private readonly ISecurityStore _store = Substitute.For<ISecurityStore>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public SecurityAdminUseCasesTests() => _clock.Now.Returns(Now);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Unblock_BlankIp_IsMissing_WithoutTouchingTheStore(string? ip)
    {
        var field = ip is null ? AdminField.Absent : AdminField.OfText(ip);

        var result = await new UnblockIp(_store).ExecuteAsync(field, CancellationToken.None);

        result.ShouldBeOfType<UnblockIpResult.BadInput>().Problem.ShouldBe(IpInputProblem.Missing);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("number")]
    [InlineData("bool")]
    public async Task Unblock_NonTextIp_IsMissing(string kind)
    {
        var field = kind switch
        {
            "null" => AdminField.NullValue,
            "number" => AdminField.OfWhole(5),
            _ => AdminField.OfBoolean(true),
        };

        var result = await new UnblockIp(_store).ExecuteAsync(field, CancellationToken.None);

        result.ShouldBeOfType<UnblockIpResult.BadInput>().Problem.ShouldBe(IpInputProblem.Missing);
    }

    [Fact]
    [Trait("Desvio", "078")]
    public async Task Unblock_GarbageIp_IsInvalid()
    {
        var result = await new UnblockIp(_store).ExecuteAsync(AdminField.OfText("não-é-ip"), CancellationToken.None);

        result.ShouldBeOfType<UnblockIpResult.BadInput>().Problem.ShouldBe(IpInputProblem.Invalid);
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-22")]
    public async Task Unblock_TrimsAndClearsBothTempBlockAndBlacklist_IgnoringTheOutcome()
    {
        _store.RemoveTempBlockAsync("1.2.3.4", Arg.Any<CancellationToken>()).Returns(false);
        _store.RemoveFromBlacklistAsync("1.2.3.4", Arg.Any<CancellationToken>()).Returns(false);

        var result = await new UnblockIp(_store).ExecuteAsync(AdminField.OfText("  1.2.3.4 "), CancellationToken.None);

        var done = result.ShouldBeOfType<UnblockIpResult.Done>();
        done.Ip.ShouldBe("1.2.3.4");
        done.TempBlockRemoved.ShouldBeFalse();
        done.BlacklistRemoved.ShouldBeFalse();
        await _store.Received(1).RemoveTempBlockAsync("1.2.3.4", Arg.Any<CancellationToken>());
        await _store.Received(1).RemoveFromBlacklistAsync("1.2.3.4", Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Desvio", "078")]
    public async Task Unblock_NormalizesIpv6()
    {
        var result = await new UnblockIp(_store).ExecuteAsync(AdminField.OfText("0:0:0:0:0:0:0:1"), CancellationToken.None);

        result.ShouldBeOfType<UnblockIpResult.Done>().Ip.ShouldBe("::1");
    }

    [Fact]
    public async Task Stats_UsesTheSaoPauloClock()
    {
        _store.GetStatsAsync(Now, Arg.Any<CancellationToken>()).Returns(SecurityStats.Unavailable);

        var stats = await new GetSecurityStats(_store, _clock).ExecuteAsync(CancellationToken.None);

        stats.RedisAvailable.ShouldBeFalse();
        await _store.Received(1).GetStatsAsync(Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlacklistAdd_FalsyOrNonTextIp_IsMissing(bool nonText)
    {
        var field = nonText ? AdminField.OfWhole(7) : AdminField.OfText(string.Empty);

        var result = await new BlacklistIp(_store, _clock).ExecuteAsync(field, AdminField.Absent, CancellationToken.None);

        result.ShouldBeOfType<BlacklistIpResult.BadIp>().Problem.ShouldBe(IpInputProblem.Missing);
    }

    [Fact]
    [Trait("Desvio", "078")]
    public async Task BlacklistAdd_InvalidIp_IsRejectedBeforeTheReason()
    {
        var result = await new BlacklistIp(_store, _clock).ExecuteAsync(
            AdminField.OfText("999.1.1.1"), AdminField.OfWhole(1), CancellationToken.None);

        result.ShouldBeOfType<BlacklistIpResult.BadIp>().Problem.ShouldBe(IpInputProblem.Invalid);
    }

    [Fact]
    [Trait("Desvio", "079")]
    public async Task BlacklistAdd_NonTextReason_IsRejected()
    {
        var result = await new BlacklistIp(_store, _clock).ExecuteAsync(
            AdminField.OfText("9.9.9.9"), AdminField.OfWhole(1), CancellationToken.None);

        result.ShouldBeOfType<BlacklistIpResult.ReasonNotText>();
        await _store.DidNotReceiveWithAnyArgs().BlacklistAsync(default!, default, default, default);
    }

    [Fact]
    [Trait("AC", "009-24")]
    public async Task BlacklistAdd_AbsentReason_UsesTheDefault()
    {
        _store.BlacklistAsync("9.9.9.9", "Manual block via API", Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new BlacklistIp(_store, _clock).ExecuteAsync(
            AdminField.OfText("9.9.9.9"), AdminField.Absent, CancellationToken.None);

        result.ShouldBeOfType<BlacklistIpResult.Added>().Reason.ShouldBe("Manual block via API");
    }

    [Fact]
    public async Task BlacklistAdd_ExplicitNullReason_IsKeptAsNull()
    {
        _store.BlacklistAsync("9.9.9.9", null, Now, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new BlacklistIp(_store, _clock).ExecuteAsync(
            AdminField.OfText("9.9.9.9"), AdminField.NullValue, CancellationToken.None);

        result.ShouldBeOfType<BlacklistIpResult.Added>().Reason.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "009-24")]
    public async Task BlacklistAdd_StoreNotConfirming_MeansRedisUnavailable()
    {
        _store.BlacklistAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await new BlacklistIp(_store, _clock).ExecuteAsync(
            AdminField.OfText("9.9.9.9"), AdminField.OfText("scanner"), CancellationToken.None);

        result.ShouldBeOfType<BlacklistIpResult.RedisUnavailable>();
    }

    [Fact]
    [Trait("AC", "009-24")]
    public async Task BlacklistRemove_Validates_ThenRemovesWithoutTrim()
    {
        _store.RemoveFromBlacklistAsync("9.9.9.9", Arg.Any<CancellationToken>()).Returns(true);
        var useCase = new RemoveBlacklistedIp(_store);

        (await useCase.ExecuteAsync(AdminField.Absent, CancellationToken.None))
            .ShouldBeOfType<RemoveBlacklistedIpResult.BadIp>().Problem.ShouldBe(IpInputProblem.Missing);
        (await useCase.ExecuteAsync(AdminField.OfText("x"), CancellationToken.None))
            .ShouldBeOfType<RemoveBlacklistedIpResult.BadIp>().Problem.ShouldBe(IpInputProblem.Invalid);
        (await useCase.ExecuteAsync(AdminField.OfText("9.9.9.9"), CancellationToken.None))
            .ShouldBeOfType<RemoveBlacklistedIpResult.Removed>().Ip.ShouldBe("9.9.9.9");
    }

    [Fact]
    [Trait("AC", "009-24")]
    public async Task BlacklistRemove_WithoutRedis_IsUnavailable()
    {
        _store.RemoveFromBlacklistAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        (await new RemoveBlacklistedIp(_store).ExecuteAsync(AdminField.OfText("9.9.9.9"), CancellationToken.None))
            .ShouldBeOfType<RemoveBlacklistedIpResult.RedisUnavailable>();
    }
}

public class CacheAdminUseCasesTests
{
    private readonly ILlmCacheAdmin _cache = Substitute.For<ILlmCacheAdmin>();

    private Task<ClearLlmCacheResult> Clear(AdminField userId, AdminField pattern) =>
        new ClearLlmCache(_cache).ExecuteAsync(userId, pattern, CancellationToken.None);

    [Fact]
    [Trait("AC", "009-25")]
    public async Task UserBranch_PassesThePureTypeAsPattern()
    {
        _cache.InvalidateUserAsync(7, "category", Arg.Any<CancellationToken>()).Returns(1);

        var cleared = (await Clear(AdminField.OfWhole(7), AdminField.OfText("category"))).ShouldBeOfType<ClearLlmCacheResult.Cleared>();

        cleared.Deleted.ShouldBe(1);
        cleared.Plan.ShouldBe(new CacheClearPlan.ForUser(7, "category"));
        await _cache.DidNotReceiveWithAnyArgs().InvalidatePatternAsync(default!, default);
    }

    [Fact]
    public async Task UserBranch_WithoutPattern_PassesNull()
    {
        _cache.InvalidateUserAsync(7, null, Arg.Any<CancellationToken>()).Returns(2);

        (await Clear(AdminField.OfWhole(7), AdminField.Absent)).ShouldBeOfType<ClearLlmCacheResult.Cleared>().Deleted.ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "009-25")]
    public async Task PatternBranch_IsUsedWhenThereIsNoUser_EvenWithZeroUser()
    {
        _cache.InvalidatePatternAsync("intent:*", Arg.Any<CancellationToken>()).Returns(2);

        var cleared = (await Clear(AdminField.OfWhole(0), AdminField.OfText("intent:*"))).ShouldBeOfType<ClearLlmCacheResult.Cleared>();

        cleared.Plan.ShouldBe(new CacheClearPlan.ForPattern("intent:*"));
        cleared.Deleted.ShouldBe(2);
        await _cache.DidNotReceiveWithAnyArgs().InvalidateUserAsync(default, default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("AC", "009-25")]
    public async Task AllBranch_ClearsEverything(bool explicitNulls)
    {
        _cache.InvalidatePatternAsync("*", Arg.Any<CancellationToken>()).Returns(3);
        var user = explicitNulls ? AdminField.NullValue : AdminField.Absent;
        var pattern = explicitNulls ? AdminField.OfText(string.Empty) : AdminField.Absent;

        var cleared = (await Clear(user, pattern)).ShouldBeOfType<ClearLlmCacheResult.Cleared>();

        cleared.Plan.ShouldBeOfType<CacheClearPlan.Everything>();
        cleared.Deleted.ShouldBe(3);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("negative")]
    [InlineData("big")]
    [InlineData("true")]
    [InlineData("other")]
    [Trait("Desvio", "079")]
    public async Task TruthyUserIdThatIsNotAPositiveInteger_IsRejected(string kind)
    {
        var field = kind switch
        {
            "text" => AdminField.OfText("5"),
            "negative" => AdminField.OfWhole(-3),
            "big" => AdminField.OfWhole(long.MaxValue),
            "true" => AdminField.OfBoolean(true),
            _ => AdminField.OtherValue(truthy: true),
        };

        (await Clear(field, AdminField.Absent)).ShouldBeOfType<ClearLlmCacheResult.InvalidUserId>();
        _cache.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("empty-text")]
    [InlineData("empty-list")]
    public async Task FalsyUserIdOfAnyType_IsTreatedAsAbsent(string kind)
    {
        var field = kind switch
        {
            "false" => AdminField.OfBoolean(false),
            "empty-text" => AdminField.OfText(string.Empty),
            _ => AdminField.OtherValue(truthy: false),
        };
        _cache.InvalidatePatternAsync("*", Arg.Any<CancellationToken>()).Returns(0);

        (await Clear(field, AdminField.Absent)).ShouldBeOfType<ClearLlmCacheResult.Cleared>();
    }

    [Fact]
    public async Task NonTextPattern_IsRejected_InsteadOfClearingEverything()
    {
        (await Clear(AdminField.Absent, AdminField.OfWhole(5))).ShouldBeOfType<ClearLlmCacheResult.InvalidPattern>();
        _cache.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void Stats_ReturnsTheInstanceMetrics()
    {
        var metrics = new LlmCacheMetrics(0, 0, 0, 0, 0, "0.0%", new Dictionary<string, LlmCacheTypeMetrics>(), new LlmCacheSavings(0, "0.0%"));
        _cache.GetMetrics().Returns(metrics);

        new GetLlmCacheStats(_cache).Execute().ShouldBeSameAs(metrics);
    }
}

public class NotificationConfigUseCasesTests
{
    private readonly IAdminNotificationConfigRepository _repository = Substitute.For<IAdminNotificationConfigRepository>();

    [Fact]
    [Trait("AC", "009-28")]
    public async Task Get_ReturnsTheRow_OrUserNotFound()
    {
        var row = new NotificationConfigRow(true, "07:00", null);
        _repository.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(row);
        _repository.GetOrCreateAsync(2, Arg.Any<CancellationToken>()).Returns((NotificationConfigRow?)null);
        var useCase = new GetNotificationConfig(_repository);

        (await useCase.ExecuteAsync(1, CancellationToken.None)).ShouldBeOfType<GetNotificationConfigResult.Found>().Row.ShouldBe(row);
        (await useCase.ExecuteAsync(2, CancellationToken.None)).ShouldBeOfType<GetNotificationConfigResult.UserNotFound>();
    }

    private Task<SetFinancialAlertsResult> Set(AdminField userId, AdminField active) =>
        new SetFinancialAlerts(_repository).ExecuteAsync(userId, active, CancellationToken.None);

    [Theory]
    [InlineData("absent")]
    [InlineData("null")]
    [InlineData("zero")]
    [InlineData("empty-text")]
    [InlineData("false")]
    [InlineData("text-number")]
    [InlineData("decimal")]
    [Trait("AC", "009-29")]
    public async Task Set_FalsyOrNonIntegerUserId_IsMissing_BeforeCheckingActive(string kind)
    {
        var field = kind switch
        {
            "absent" => AdminField.Absent,
            "null" => AdminField.NullValue,
            "zero" => AdminField.OfWhole(0),
            "empty-text" => AdminField.OfText(string.Empty),
            "false" => AdminField.OfBoolean(false),
            "text-number" => AdminField.OfText("1"),
            _ => AdminField.OtherValue(truthy: true),
        };

        (await Set(field, AdminField.Absent)).ShouldBeOfType<SetFinancialAlertsResult.MissingUserId>();
        _repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("null")]
    [InlineData("text")]
    [InlineData("number")]
    [Trait("AC", "009-29")]
    public async Task Set_ActiveThatIsNotABoolean_IsRequired(string kind)
    {
        var field = kind switch
        {
            "absent" => AdminField.Absent,
            "null" => AdminField.NullValue,
            "text" => AdminField.OfText("false"),
            _ => AdminField.OfWhole(1),
        };

        (await Set(AdminField.OfWhole(1), field)).ShouldBeOfType<SetFinancialAlertsResult.MissingActive>();
        _repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-29")]
    public async Task Set_FalseIsValid_AndReturnsTheStoredValue()
    {
        _repository.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new NotificationConfigRow(true, "07:00", true));
        _repository.SetFinancialAlertsAsync(1, false, Arg.Any<CancellationToken>()).Returns(false);

        (await Set(AdminField.OfWhole(1), AdminField.OfBoolean(false))).ShouldBeOfType<SetFinancialAlertsResult.Updated>().Active.ShouldBeFalse();
    }

    [Theory]
    [InlineData(-4L)]
    [InlineData(2147483648L)]
    public async Task Set_IdOutsideTheIntRange_IsUserNotFound_WithoutTheDatabase(long id)
    {
        (await Set(AdminField.OfWhole(id), AdminField.OfBoolean(true))).ShouldBeOfType<SetFinancialAlertsResult.UserNotFound>();
        _repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("Desvio", "080")]
    public async Task Set_UnknownUser_IsNotFound_AtEitherStep()
    {
        _repository.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns((NotificationConfigRow?)null);
        (await Set(AdminField.OfWhole(1), AdminField.OfBoolean(true))).ShouldBeOfType<SetFinancialAlertsResult.UserNotFound>();
        await _repository.DidNotReceiveWithAnyArgs().SetFinancialAlertsAsync(default, default, default);

        _repository.GetOrCreateAsync(2, Arg.Any<CancellationToken>()).Returns(new NotificationConfigRow(true, "07:00", true));
        _repository.SetFinancialAlertsAsync(2, true, Arg.Any<CancellationToken>()).Returns((bool?)null);
        (await Set(AdminField.OfWhole(2), AdminField.OfBoolean(true))).ShouldBeOfType<SetFinancialAlertsResult.UserNotFound>();
    }

    [Fact]
    public async Task ClearBotSession_ReturnsTheDeletedRows()
    {
        var sessions = Substitute.For<IBotSessionRepository>();
        sessions.ClearAsync(Arg.Any<CancellationToken>()).Returns(3);

        (await new ClearBotSession(sessions).ExecuteAsync(CancellationToken.None)).ShouldBe(3);
    }
}

public class CacheClearPlanTests
{
    [Theory]
    [InlineData(7, "category", "ForUser")]
    [InlineData(7, null, "ForUser")]
    [InlineData(null, "intent", "ForPattern")]
    [InlineData(0, "intent", "ForPattern")]
    [InlineData(null, null, "Everything")]
    [InlineData(0, "", "Everything")]
    [Trait("AC", "009-25")]
    public void From_PicksUserThenPatternThenAll(int? userId, string? pattern, string expected)
    {
        CacheClearPlan.From(userId, pattern).GetType().Name.ShouldBe(expected);
    }

    [Fact]
    public void From_EmptyPatternWithUser_BecomesNull()
    {
        CacheClearPlan.From(3, string.Empty).ShouldBe(new CacheClearPlan.ForUser(3, null));
    }
}
