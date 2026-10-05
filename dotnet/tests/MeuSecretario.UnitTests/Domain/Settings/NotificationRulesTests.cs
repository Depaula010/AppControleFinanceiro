using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Settings;

namespace MeuSecretario.UnitTests.Domain.Settings;

/// <summary>Spec 007 §3.7 e §3.6: horários, PUT total, tipos (084), minutos e resumo matinal (085), leitura (0 vira 3, 5 caracteres).</summary>
public class NotificationRulesTests
{
    private static RawBody WithEvening(RawValue time) =>
        SettingsBodies.Of(("eveningCheckIn", SettingsBodies.Obj(("time", time))));

    private static NotificationError Rejected(RawBody? body) =>
        NotificationRules.Validate(body).ShouldBeOfType<NotificationValidation.Rejected>().Error;

    private static NotificationInput Ok(RawBody? body) =>
        NotificationRules.Validate(body).ShouldBeOfType<NotificationValidation.Ok>().Input;

    [Theory]
    [Trait("AC", "007-43")]
    [InlineData("17:59")]
    [InlineData("24:00")]
    [InlineData("7:00")]
    [InlineData("00:00")]
    public void EveningHourOutsideTheRange_IsRejected(string time)
    {
        Rejected(WithEvening(RawValue.Text(time))).ShouldBe(NotificationError.EveningHourRange);
    }

    [Theory]
    [Trait("AC", "007-43")]
    [InlineData("abc")]
    [InlineData("19")]
    [InlineData("19:00:00")]
    [InlineData("")]
    [InlineData("19:")]
    [InlineData(":30")]
    [InlineData("19:75")]
    [InlineData("18:75")]
    [InlineData("19:-1")]
    public void EveningTimeWithWrongFormat_IsRejected(string time)
    {
        Rejected(WithEvening(RawValue.Text(time))).ShouldBe(NotificationError.InvalidTimeFormat);
    }

    [Fact]
    [Trait("AC", "007-43")]
    public void EveningTimeThatIsNotText_IsInvalidFormat()
    {
        Rejected(WithEvening(RawValue.Null)).ShouldBe(NotificationError.InvalidTimeFormat);
        Rejected(WithEvening(RawValue.Whole(1900))).ShouldBe(NotificationError.InvalidTimeFormat);
        Rejected(WithEvening(RawValue.Bool(true))).ShouldBe(NotificationError.InvalidTimeFormat);
    }

    [Theory]
    [Trait("AC", "007-43")]
    [InlineData("18:00", "18:00")]
    [InlineData("23:30", "23:30")]
    [InlineData("19:5", "19:05")]
    [InlineData(" 20 : 15", "20:15")]
    public void EveningTime_IsStoredAsCanonicalHhMm(string time, string expected)
    {
        Ok(WithEvening(RawValue.Text(time))).EveningTime.ShouldBe(expected);
    }

    [Theory]
    [Trait("AC", "007-45")]
    [InlineData("25:00")]
    [InlineData("abc")]
    [InlineData("07:75")]
    [InlineData("07:00:15")]
    [InlineData("")]
    public void MorningTimeInvalid_IsRejected_Desvio085(string time)
    {
        Rejected(SettingsBodies.Of(("morningBriefing", SettingsBodies.Obj(("time", RawValue.Text(time))))))
            .ShouldBe(NotificationError.InvalidTimeFormat);
    }

    [Fact]
    [Trait("AC", "007-45")]
    public void MorningTimeThatIsNotText_IsRejected()
    {
        Rejected(SettingsBodies.Of(("morningBriefing", SettingsBodies.Obj(("time", RawValue.Null)))))
            .ShouldBe(NotificationError.InvalidTimeFormat);
        Rejected(SettingsBodies.Of(("morningBriefing", SettingsBodies.Obj(("time", RawValue.Whole(7))))))
            .ShouldBe(NotificationError.InvalidTimeFormat);
    }

    [Fact]
    [Trait("AC", "007-45")]
    public void MorningTime_AcceptsAnyValidHourAndMinute()
    {
        Ok(SettingsBodies.Of(("morningBriefing", SettingsBodies.Obj(("time", RawValue.Text("7:30")))))).MorningTime.ShouldBe("07:30");
        Ok(SettingsBodies.Of(("morningBriefing", SettingsBodies.Obj(("time", RawValue.Text("00:00")))))).MorningTime.ShouldBe("00:00");
        Ok(SettingsBodies.Of(("morningBriefing", SettingsBodies.Obj(("time", RawValue.Text("23:59")))))).MorningTime.ShouldBe("23:59");
    }

    [Fact]
    [Trait("AC", "007-44")]
    public void EmptyBodyOrNull_UsesTheDefaults_TotalPut()
    {
        var expected = new NotificationInput(true, "07:00", true, "19:00", true, 3);

        Ok(SettingsBodies.Empty).ShouldBe(expected);
        Ok(null).ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "007-42")]
    public void AngularBody_IsReadInFull()
    {
        var body = SettingsBodies.Of(
            ("morningBriefing", SettingsBodies.Obj(("enabled", RawValue.Bool(false)), ("time", RawValue.Text("08:30")))),
            ("eveningCheckIn", SettingsBodies.Obj(("enabled", RawValue.Bool(true)), ("time", RawValue.Text("21:15")))),
            ("financialAlerts", SettingsBodies.Obj(("enabled", RawValue.Bool(false)), ("daysBeforeDue", RawValue.Whole(7)))));

        Ok(body).ShouldBe(new NotificationInput(false, "08:30", true, "21:15", false, 7));
    }

    [Theory]
    [Trait("AC", "007-46")]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(45)]
    public void DaysBeforeDue_HasNoRange(int days)
    {
        var body = SettingsBodies.Of(("financialAlerts", SettingsBodies.Obj(("daysBeforeDue", RawValue.Whole(days)))));

        Ok(body).DaysBeforeDue.ShouldBe(days);
    }

    [Fact]
    [Trait("AC", "007-46")]
    public void DaysBeforeDue_AcceptsTextOfIntegerAndRoundsFractions()
    {
        Ok(SettingsBodies.Of(("financialAlerts", SettingsBodies.Obj(("daysBeforeDue", RawValue.Text(" 5 ")))))).DaysBeforeDue.ShouldBe(5);
        Ok(SettingsBodies.Of(("financialAlerts", SettingsBodies.Obj(("daysBeforeDue", RawValue.Fractional(2.5)))))).DaysBeforeDue.ShouldBe(3);
    }

    public static TheoryData<RawBody> InvalidTypes => new()
    {
        SettingsBodies.Of(("morningBriefing", RawValue.Null)),
        SettingsBodies.Of(("eveningCheckIn", RawValue.Text("x"))),
        SettingsBodies.Of(("financialAlerts", new ListValue(1))),
        SettingsBodies.Of(("financialAlerts", RawValue.Whole(1))),
        SettingsBodies.Of(("morningBriefing", SettingsBodies.Obj(("enabled", RawValue.Text("x"))))),
        SettingsBodies.Of(("eveningCheckIn", SettingsBodies.Obj(("enabled", RawValue.Whole(1))))),
        SettingsBodies.Of(("financialAlerts", SettingsBodies.Obj(("enabled", RawValue.Null)))),
        SettingsBodies.Of(("financialAlerts", SettingsBodies.Obj(("daysBeforeDue", RawValue.Null)))),
        SettingsBodies.Of(("financialAlerts", SettingsBodies.Obj(("daysBeforeDue", RawValue.Bool(true))))),
        SettingsBodies.Of(("financialAlerts", SettingsBodies.Obj(("daysBeforeDue", RawValue.Text("abc"))))),
        SettingsBodies.Of(("financialAlerts", SettingsBodies.Obj(("daysBeforeDue", new ListValue(0))))),
    };

    [Theory]
    [MemberData(nameof(InvalidTypes))]
    [Trait("AC", "007-47")]
    public void WrongTypes_AreInvalidData_Desvio084(RawBody body)
    {
        Rejected(body).ShouldBe(NotificationError.InvalidData);
    }

    [Fact]
    [Trait("AC", "007-43")]
    public void EveningFormat_ComesBeforeTypeErrors()
    {
        var body = SettingsBodies.Of(
            ("eveningCheckIn", SettingsBodies.Obj(("time", RawValue.Text("abc")))),
            ("financialAlerts", SettingsBodies.Obj(("enabled", RawValue.Text("x")))));

        Rejected(body).ShouldBe(NotificationError.InvalidTimeFormat);
    }

    [Fact]
    [Trait("AC", "007-41")]
    public void Defaults_ReadZeroAsThree_AndFirstFiveCharacters()
    {
        NotificationDefaults.ReadDaysBeforeDue(0).ShouldBe(3);
        NotificationDefaults.ReadDaysBeforeDue(7).ShouldBe(7);
        NotificationDefaults.ReadDaysBeforeDue(-5).ShouldBe(-5);
        NotificationDefaults.First5("07:00:00").ShouldBe("07:00");
        NotificationDefaults.First5("19:30:00.5").ShouldBe("19:30");
        NotificationDefaults.First5("08:30").ShouldBe("08:30");
        NotificationDefaults.First5("9:5").ShouldBe("9:5");
    }
}
