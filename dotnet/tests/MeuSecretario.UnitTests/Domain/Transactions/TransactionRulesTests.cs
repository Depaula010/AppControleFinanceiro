using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.UnitTests.Domain.Accounts;

namespace MeuSecretario.UnitTests.Domain.Transactions;

internal static class Tx
{
    public static RawValue F(double value) => RawValue.Fractional(value);

    public static RawValue B(bool value) => RawValue.Bool(value);

    /// <summary>Corpo válido do POST; as chaves repetidas sobrescrevem as do corpo-base.</summary>
    public static RawBody Valid(params (string Key, RawValue Value)[] overrides) =>
        Raw.Body(
        [
            ("descricao", Raw.T("Mercado")), ("valor", F(150.5)), ("tipo", Raw.T("Despesa")), ("data", Raw.T("2025-12-11")),
            ("subcategoria_id", Raw.N(7)), ("conta_id", Raw.N(3)), .. overrides,
        ]);

    public static RawBody WithoutKeys(params string[] keys) => Without(keys, []);

    public static RawBody Without(string key, params (string Key, RawValue Value)[] overrides) => Without([key], overrides);

    private static RawBody Without(string[] keys, (string Key, RawValue Value)[] overrides)
    {
        var all = new List<(string, RawValue)>
        {
            ("descricao", Raw.T("Mercado")), ("valor", F(150.5)), ("tipo", Raw.T("Despesa")), ("data", Raw.T("2025-12-11")),
            ("subcategoria_id", Raw.N(7)), ("conta_id", Raw.N(3)),
        };
        all.RemoveAll(item => keys.Contains(item.Item1));
        all.AddRange(overrides);
        return Raw.Body([.. all]);
    }
}

public class ListQueryParserTests
{
    private static ListFilter Ok(string? limit = null, string? offset = null, string? tipo = null, string? from = null, string? to = null) =>
        ListQueryParser.Parse(limit, offset, tipo, from, to).ShouldBeOfType<ListQueryResult.Ok>().Filter;

    private static ListQueryError Error(string? limit = null, string? offset = null, string? tipo = null, string? from = null, string? to = null) =>
        ListQueryParser.Parse(limit, offset, tipo, from, to).ShouldBeOfType<ListQueryResult.Rejected>().Error;

    [Fact]
    [Trait("AC", "004-02")]
    public void NoParameters_UseTheDefaults()
    {
        var filter = Ok();

        filter.Limit.ShouldBe(20);
        filter.Offset.ShouldBe(0);
        filter.Kind.ShouldBeNull();
        filter.From.ShouldBeNull();
        filter.To.ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "004-02")]
    [InlineData("100", 100)]
    [InlineData("1", 1)]
    [InlineData(" 5", 5)]
    [InlineData("+5", 5)]
    [InlineData("abc", 20)]
    [InlineData("", 20)]
    [InlineData("1.5", 20)]
    public void Limit_AcceptsPythonIntAndFallsBackOnText(string text, int expected)
    {
        Ok(limit: text).Limit.ShouldBe(expected);
    }

    [Theory]
    [Trait("AC", "004-02")]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("99999999999999999999")]
    public void LimitOutOfRange_IsAnError(string text)
    {
        Error(limit: text).ShouldBe(ListQueryError.LimitRange);
    }

    [Theory]
    [Trait("AC", "004-03")]
    [InlineData("-1", ListQueryError.OffsetNegative)]
    [InlineData("-99999999999999999999", ListQueryError.OffsetNegative)]
    public void NegativeOffset_IsAnError(string text, ListQueryError expected)
    {
        Error(offset: text).ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "004-03")]
    public void OffsetText_FallsBackAndHugeOffsetIsCapped()
    {
        Ok(offset: "abc").Offset.ShouldBe(0);
        Ok(offset: "99999999999999999999").Offset.ShouldBe(long.MaxValue);
        Ok(offset: "40").Offset.ShouldBe(40);
    }

    [Fact]
    [Trait("AC", "004-03")]
    public void ErrorOrder_IsLimitOffsetTypeDates()
    {
        Error("0", "-1", "x", "abc").ShouldBe(ListQueryError.LimitRange);
        Error(null, "-1", "x", "abc").ShouldBe(ListQueryError.OffsetNegative);
        Error(null, null, "x", "abc").ShouldBe(ListQueryError.TypeInvalid);
        Error(null, null, "Receita", "abc").ShouldBe(ListQueryError.DateFormat);
    }

    [Theory]
    [Trait("AC", "004-04")]
    [InlineData("Receita", TransactionKind.Income)]
    [InlineData("Despesa", TransactionKind.Expense)]
    public void Type_IsMapped(string text, TransactionKind expected)
    {
        Ok(tipo: text).Kind.ShouldBe(expected);
    }

    [Theory]
    [Trait("AC", "004-04")]
    [InlineData("Renda")]
    [InlineData("receita")]
    [InlineData("Transferência")]
    public void InvalidType_IsAnError(string text)
    {
        Error(tipo: text).ShouldBe(ListQueryError.TypeInvalid);
    }

    [Fact]
    [Trait("AC", "004-04")]
    public void EmptyType_MeansNoFilter()
    {
        Ok(tipo: "").Kind.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "004-05")]
    public void Dates_AreParsed_AndInvertedRangeIsNotAnError()
    {
        var filter = Ok(from: "2025-12-31", to: "2025-12-01");

        filter.From.ShouldBe(new DateOnly(2025, 12, 31));
        filter.To.ShouldBe(new DateOnly(2025, 12, 1));
        Ok(from: "2025-1-5").From.ShouldBe(new DateOnly(2025, 1, 5));
        Ok(from: "").From.ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "004-06")]
    [InlineData("2025-13-01")]
    [InlineData("2025-02-30")]
    [InlineData("01/12/2025")]
    [InlineData("2025-12-01T10:00")]
    [InlineData("abc")]
    public void InvalidDates_AreAnError_ForBothParameters(string text)
    {
        Error(from: text).ShouldBe(ListQueryError.DateFormat);
        Error(to: text).ShouldBe(ListQueryError.DateFormat);
    }
}

public class CreateTransactionRulesTests
{
    private static TransactionError? ErrorOf(RawBody? body) =>
        CreateTransactionRules.Validate(body) is CreateTransactionValidation.Rejected rejected ? rejected.Error : null;

    private static NewTransactionData Ok(RawBody body) =>
        CreateTransactionRules.Validate(body).ShouldBeOfType<CreateTransactionValidation.Ok>().Data;

    [Fact]
    [Trait("AC", "004-10")]
    public void ValidBody_BecomesTheNewTransaction()
    {
        var data = Ok(Tx.Valid());

        data.Description.ShouldBe("Mercado");
        data.Amount.ShouldBe(150.5m);
        data.Kind.ShouldBe(TransactionKind.Expense);
        data.DateText.ShouldBe("2025-12-11");
        data.Date.ShouldBe(new DateOnly(2025, 12, 11));
        data.SubcategoryId.ShouldBe(7);
        data.AccountId.ShouldBe(3);
        data.Notes.ShouldBeNull();
        data.Consolidated.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "004-12")]
    public void NullOrEmptyBody_IsDescriptionRequired()
    {
        ErrorOf(null).ShouldBe(TransactionError.DescriptionRequired);
        ErrorOf(Raw.Body()).ShouldBe(TransactionError.DescriptionRequired);
    }

    [Fact]
    [Trait("AC", "004-13")]
    public void WithTwoErrors_TheFirstOfTheOrderWins()
    {
        ErrorOf(Raw.Body()).ShouldBe(TransactionError.DescriptionRequired);
        ErrorOf(Tx.Without("valor", ("tipo", Raw.T("x")))).ShouldBe(TransactionError.ValueRequired);
        ErrorOf(Tx.Valid(("valor", Raw.T("abc")), ("tipo", Raw.T("x")))).ShouldBe(TransactionError.ValueInvalid);
        ErrorOf(Tx.Valid(("valor", Raw.N(-1)), ("tipo", Raw.T("x")))).ShouldBe(TransactionError.ValueNegative);
        ErrorOf(Tx.WithoutKeys("data", "conta_id")).ShouldBe(TransactionError.DateRequired);
        ErrorOf(Raw.Body(
            ("descricao", Raw.T("a")), ("valor", Raw.N(1)), ("tipo", Raw.T("Despesa")), ("data", Raw.T("2025-01-01"))))
            .ShouldBe(TransactionError.SubcategoryRequired);
    }

    [Theory]
    [Trait("AC", "004-14")]
    [InlineData("  ")]
    [InlineData("")]
    public void BlankDescription_IsRequired(string text)
    {
        ErrorOf(Tx.Valid(("descricao", Raw.T(text)))).ShouldBe(TransactionError.DescriptionRequired);
    }

    [Fact]
    [Trait("AC", "004-14")]
    public void Description_IsTrimmed_AndNonTextIsRequiredToo()
    {
        Ok(Tx.Valid(("descricao", Raw.T("  Mercado  ")))).Description.ShouldBe("Mercado");
        ErrorOf(Tx.Valid(("descricao", RawValue.Null))).ShouldBe(TransactionError.DescriptionRequired);
        ErrorOf(Tx.Valid(("descricao", Raw.N(5)))).ShouldBe(TransactionError.DescriptionRequired);
    }

    [Fact]
    [Trait("AC", "004-14")]
    public void DescriptionLongerThan255Characters_IsTooLong()
    {
        Ok(Tx.Valid(("descricao", Raw.T(new string('a', 255))))).Description.Length.ShouldBe(255);
        ErrorOf(Tx.Valid(("descricao", Raw.T(new string('a', 256))))).ShouldBe(TransactionError.DescriptionTooLong);
    }

    [Theory]
    [Trait("AC", "004-15")]
    [InlineData("null")]
    [InlineData("absent")]
    public void MissingOrNullValue_IsRequired(string kind)
    {
        var body = kind == "null" ? Tx.Valid(("valor", RawValue.Null)) : Tx.Without("valor");

        ErrorOf(body).ShouldBe(TransactionError.ValueRequired);
    }

    [Fact]
    [Trait("AC", "004-15")]
    public void ZeroAndFalseAreNotMissingValues()
    {
        Ok(Tx.Valid(("valor", Raw.N(0)))).Amount.ShouldBe(0m);
        Ok(Tx.Valid(("valor", Tx.B(false)))).Amount.ShouldBe(0m);
        Ok(Tx.Valid(("valor", Tx.B(true)))).Amount.ShouldBe(1m);
        Ok(Tx.Valid(("valor", Raw.T("12.5")))).Amount.ShouldBe(12.5m);
        Ok(Tx.Valid(("valor", Raw.T(" 7 ")))).Amount.ShouldBe(7m);
        Ok(Tx.Valid(("valor", Tx.F(-0.0)))).Amount.ShouldBe(0m);
    }

    [Theory]
    [Trait("AC", "004-15")]
    [InlineData("1,5")]
    [InlineData("abc")]
    [InlineData("nan")]
    [InlineData("inf")]
    [InlineData("")]
    public void NonNumericText_IsInvalid(string text)
    {
        ErrorOf(Tx.Valid(("valor", Raw.T(text)))).ShouldBe(TransactionError.ValueInvalid);
    }

    [Fact]
    [Trait("AC", "004-15")]
    public void ListObjectAndOverflow_AreInvalid_AndNegativeIsNegative()
    {
        ErrorOf(Tx.Valid(("valor", RawValue.Other))).ShouldBe(TransactionError.ValueInvalid);
        ErrorOf(Tx.Valid(("valor", Raw.T("1e20")))).ShouldBe(TransactionError.ValueInvalid);
        ErrorOf(Tx.Valid(("valor", Tx.F(-0.01)))).ShouldBe(TransactionError.ValueNegative);
        ErrorOf(Tx.Valid(("valor", Raw.T("-5")))).ShouldBe(TransactionError.ValueNegative);
    }

    [Theory]
    [Trait("AC", "004-17")]
    [InlineData("receita")]
    [InlineData("Renda")]
    [InlineData("Transferência")]
    public void InvalidType_IsRejected(string text)
    {
        ErrorOf(Tx.Valid(("tipo", Raw.T(text)))).ShouldBe(TransactionError.TypeInvalid);
    }

    [Fact]
    [Trait("AC", "004-17")]
    public void NullOrMissingType_IsRejected()
    {
        ErrorOf(Tx.Valid(("tipo", RawValue.Null))).ShouldBe(TransactionError.TypeInvalid);
        ErrorOf(Tx.Without("tipo")).ShouldBe(TransactionError.TypeInvalid);
        Ok(Tx.Valid(("tipo", Raw.T("Receita")))).Kind.ShouldBe(TransactionKind.Income);
    }

    [Fact]
    [Trait("AC", "004-18")]
    public void FalsyDate_IsRequired_AndBadFormatIsAFormatError()
    {
        foreach (var falsy in new[] { RawValue.Null, Raw.T(""), Raw.N(0), Tx.B(false) })
        {
            ErrorOf(Tx.Valid(("data", falsy))).ShouldBe(TransactionError.DateRequired);
        }
        ErrorOf(Tx.Without("data")).ShouldBe(TransactionError.DateRequired);

        foreach (var bad in new[] { "2025-13-01", "2025-02-30", "11/12/2025", "2025-12-11T10:00" })
        {
            ErrorOf(Tx.Valid(("data", Raw.T(bad)))).ShouldBe(TransactionError.DateFormat);
        }
        ErrorOf(Tx.Valid(("data", Raw.N(5)))).ShouldBe(TransactionError.DateFormat);
    }

    [Fact]
    [Trait("AC", "004-18")]
    public void DateText_IsKeptAsReceivedForTheEcho()
    {
        var data = Ok(Tx.Valid(("data", Raw.T("2025-1-5"))));

        data.DateText.ShouldBe("2025-1-5");
        data.Date.ShouldBe(new DateOnly(2025, 1, 5));
    }

    [Fact]
    [Trait("AC", "004-19")]
    public void FalsyIds_AreRequired()
    {
        foreach (var falsy in new[] { RawValue.Null, Raw.N(0), Raw.T(""), Tx.B(false) })
        {
            ErrorOf(Tx.Valid(("subcategoria_id", falsy))).ShouldBe(TransactionError.SubcategoryRequired);
            ErrorOf(Tx.Valid(("conta_id", falsy))).ShouldBe(TransactionError.AccountRequired);
        }
        ErrorOf(Tx.Without("subcategoria_id")).ShouldBe(TransactionError.SubcategoryRequired);
        ErrorOf(Tx.Without("conta_id")).ShouldBe(TransactionError.AccountRequired);
    }

    [Fact]
    [Trait("AC", "004-19")]
    public void NonIntegerIds_BecomeAnIdThatDoesNotExist_TextIntegersAreAccepted()
    {
        var data = Ok(Tx.Valid(("subcategoria_id", Raw.T("5")), ("conta_id", Tx.F(2.5))));

        data.SubcategoryId.ShouldBe(5);
        data.AccountId.ShouldBe(0);
        Ok(Tx.Valid(("conta_id", Tx.B(true)))).AccountId.ShouldBe(0);
        Ok(Tx.Valid(("conta_id", Raw.T("abc")))).AccountId.ShouldBe(0);
        Ok(Tx.Valid(("conta_id", RawValue.Whole(long.MaxValue)))).AccountId.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-21")]
    public void Notes_AreTrimmed_AndBlankBecomesNull()
    {
        Ok(Tx.Valid(("observacoes", Raw.T("  Compras  ")))).Notes.ShouldBe("Compras");
        Ok(Tx.Valid(("observacoes", Raw.T("")))).Notes.ShouldBeNull();
        Ok(Tx.Valid(("observacoes", Raw.T("   ")))).Notes.ShouldBeNull();
        Ok(Tx.Valid(("observacoes", RawValue.Null))).Notes.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "004-21")]
    public void NonTextNotes_AreRejected()
    {
        ErrorOf(Tx.Valid(("observacoes", Raw.N(5)))).ShouldBe(TransactionError.NotesInvalid);
        ErrorOf(Tx.Valid(("observacoes", RawValue.Other))).ShouldBe(TransactionError.NotesInvalid);
    }

    [Fact]
    [Trait("AC", "004-22")]
    public void Consolidated_DefaultsToTrue_AndOnlyBooleansAreAccepted()
    {
        Ok(Tx.Valid(("consolidada", Tx.B(false)))).Consolidated.ShouldBeFalse();
        Ok(Tx.Valid(("consolidada", Tx.B(true)))).Consolidated.ShouldBeTrue();
        Ok(Tx.Valid(("consolidada", RawValue.Null))).Consolidated.ShouldBeTrue();
        ErrorOf(Tx.Valid(("consolidada", Raw.T("false")))).ShouldBe(TransactionError.ConsolidatedInvalid);
        ErrorOf(Tx.Valid(("consolidada", Raw.N(1)))).ShouldBe(TransactionError.ConsolidatedInvalid);
    }

    [Fact]
    [Trait("AC", "004-27")]
    public void UnknownKeys_AreIgnored()
    {
        var data = Ok(Tx.Valid(("usuario_id", Raw.N(99)), ("origem", Raw.T("x")), ("fatura_id", Raw.N(9)), ("id", Raw.N(1))));

        data.Description.ShouldBe("Mercado");
    }
}

public class UpdateTransactionRulesTests
{
    private static HeadChanges Head(RawBody body, string current = "Despesa", decimal currentAmount = -50m) =>
        UpdateTransactionRules.ReadHead(body, current, currentAmount).ShouldBeOfType<HeadResult.Ok>().Changes;

    private static TransactionError HeadError(RawBody body, string current = "Despesa", decimal currentAmount = -50m) =>
        UpdateTransactionRules.ReadHead(body, current, currentAmount).ShouldBeOfType<HeadResult.Rejected>().Error;

    [Fact]
    [Trait("AC", "004-42")]
    public void Description_IsTrimmed_AndFalsyIsIgnored()
    {
        Head(Raw.Body(("descricao", Raw.T("  Novo ")))).Description.Value.ShouldBe("Novo");
        foreach (var falsy in new[] { Raw.T(""), RawValue.Null, Raw.N(0), Tx.B(false) })
        {
            Head(Raw.Body(("descricao", falsy))).Description.IsSet.ShouldBeFalse();
        }
    }

    [Fact]
    [Trait("AC", "004-42")]
    public void NonTextDescription_AndOversizeDescription_AreRejected()
    {
        HeadError(Raw.Body(("descricao", Raw.N(5)))).ShouldBe(TransactionError.DescriptionInvalid);
        HeadError(Raw.Body(("descricao", Raw.T(new string('a', 256))))).ShouldBe(TransactionError.DescriptionTooLong);
    }

    [Theory]
    [Trait("AC", "004-43")]
    [InlineData("Despesa", 180, -180)]
    [InlineData("Renda", 180, 180)]
    public void Value_TakesTheSignOfTheCurrentType(string current, int value, int expected)
    {
        Head(Raw.Body(("valor", Raw.N(value))), current).Amount.Value.ShouldBe(expected);
    }

    [Theory]
    [Trait("AC", "004-44")]
    [InlineData("Despesa", "Receita", 10, 10)]
    [InlineData("Renda", "Despesa", 10, -10)]
    public void ValueWithType_UsesTheBodyType_RegardlessOfKeyOrder(string current, string tipo, int value, int expected)
    {
        var inOrder = Head(Raw.Body(("valor", Raw.N(value)), ("tipo", Raw.T(tipo))), current);
        var reversed = Head(Raw.Body(("tipo", Raw.T(tipo)), ("valor", Raw.N(value))), current);

        inOrder.Amount.Value.ShouldBe(expected);
        reversed.Amount.Value.ShouldBe(expected);
        inOrder.Type.Value.ShouldBe(tipo == "Receita" ? "Renda" : "Despesa");
    }

    [Theory]
    [Trait("AC", "004-44")]
    [InlineData("Renda", "Despesa", 50, -50)]
    [InlineData("Despesa", "Receita", -50, 50)]
    public void TypeAlone_RewritesTheCurrentMagnitudeWithTheNewSign(string current, string tipo, int currentAmount, int expected)
    {
        Head(Raw.Body(("tipo", Raw.T(tipo))), current, currentAmount).Amount.Value.ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "004-45")]
    public void InvalidValues_AreRejected_ZeroIsAccepted()
    {
        HeadError(Raw.Body(("valor", Raw.T("abc")))).ShouldBe(TransactionError.ValueInvalid);
        HeadError(Raw.Body(("valor", RawValue.Null))).ShouldBe(TransactionError.ValueInvalid);
        HeadError(Raw.Body(("valor", RawValue.Other))).ShouldBe(TransactionError.ValueInvalid);
        HeadError(Raw.Body(("valor", Raw.N(-5)))).ShouldBe(TransactionError.ValueNegative);
        Head(Raw.Body(("valor", Raw.N(0)))).Amount.IsSet.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "004-46")]
    public void InvalidType_IsRejected_AfterValueErrors()
    {
        foreach (var bad in new[] { Raw.T("X"), RawValue.Null, Raw.T("Renda") })
        {
            HeadError(Raw.Body(("tipo", bad))).ShouldBe(TransactionError.TypeInvalid);
        }
        HeadError(Raw.Body(("tipo", Raw.T("X")), ("valor", Raw.T("abc")))).ShouldBe(TransactionError.ValueInvalid);
    }

    [Fact]
    [Trait("AC", "004-47")]
    public void Date_IsParsed_OrRejected()
    {
        Head(Raw.Body(("data", Raw.T("2025-12-12")))).Date.Value.ShouldBe(new DateOnly(2025, 12, 12));
        HeadError(Raw.Body(("data", Raw.T("12/12/2025")))).ShouldBe(TransactionError.DateFormat);
        HeadError(Raw.Body(("data", RawValue.Null))).ShouldBe(TransactionError.DateFormat);
        HeadError(Raw.Body(("data", Raw.N(3)))).ShouldBe(TransactionError.DateFormat);
    }

    [Fact]
    [Trait("AC", "004-48")]
    public void References_AreReadWhenPresent_NullAndFractionalAreIdZero()
    {
        var references = UpdateTransactionRules.ReadReferences(Raw.Body(("subcategoria_id", RawValue.Null), ("conta_id", Raw.N(4))));

        references.SubcategoryId.ShouldBe(0);
        references.AccountId.ShouldBe(4);
        UpdateTransactionRules.ReadReferences(Raw.Body(("descricao", Raw.T("x")))).ShouldBe(new References(null, null));
        UpdateTransactionRules.ReadReferences(Raw.Body(("conta_id", Tx.F(1.5)))).AccountId.ShouldBe(0);
        UpdateTransactionRules.ReadReferences(Raw.Body(("conta_id", Raw.T("8")))).AccountId.ShouldBe(8);
    }

    [Fact]
    [Trait("AC", "004-50")]
    public void Notes_AreTrimmed_AndBlankNullAndFalsyClearThem()
    {
        Tail(Raw.Body(("observacoes", Raw.T("  nota  ")))).Notes.Value.ShouldBe("nota");
        foreach (var clearing in new[] { Raw.T(""), RawValue.Null, Raw.T("   "), Raw.N(0) })
        {
            var notes = Tail(Raw.Body(("observacoes", clearing))).Notes;
            notes.IsSet.ShouldBeTrue();
            notes.Value.ShouldBeNull();
        }
        UpdateTransactionRules.ReadTail(Raw.Body(("observacoes", Raw.N(5)))).ShouldBeOfType<TailResult.Rejected>()
            .Error.ShouldBe(TransactionError.NotesInvalid);
    }

    [Fact]
    [Trait("AC", "004-51")]
    public void Consolidated_OnlyBooleans()
    {
        Tail(Raw.Body(("consolidada", Tx.B(false)))).Consolidated.Value.ShouldBeFalse();
        Tail(Raw.Body(("consolidada", Tx.B(true)))).Consolidated.Value.ShouldBeTrue();
        Tail(Raw.Body(("descricao", Raw.T("x")))).Consolidated.IsSet.ShouldBeFalse();
        UpdateTransactionRules.ReadTail(Raw.Body(("consolidada", Raw.T("false")))).ShouldBeOfType<TailResult.Rejected>()
            .Error.ShouldBe(TransactionError.ConsolidatedInvalid);
        UpdateTransactionRules.ReadTail(Raw.Body(("consolidada", RawValue.Null))).ShouldBeOfType<TailResult.Rejected>();
    }

    [Fact]
    [Trait("AC", "004-52")]
    public void NothingSet_IsEmpty()
    {
        foreach (var body in new[] { Raw.Body(("foo", Raw.N(1))), Raw.Body(("descricao", Raw.T(""))), Raw.Body(("descricao", RawValue.Null)) })
        {
            Combine(body).IsEmpty.ShouldBeTrue();
        }
    }

    [Fact]
    [Trait("AC", "004-56")]
    public void AffectsInvoice_WhenAccountDateOrTypeChange()
    {
        Combine(Raw.Body(("conta_id", Raw.N(2)))).AffectsInvoice.ShouldBeTrue();
        Combine(Raw.Body(("data", Raw.T("2025-12-12")))).AffectsInvoice.ShouldBeTrue();
        Combine(Raw.Body(("tipo", Raw.T("Receita")))).AffectsInvoice.ShouldBeTrue();
        Combine(Raw.Body(("descricao", Raw.T("x")), ("valor", Raw.N(3)), ("consolidada", Tx.B(true)))).AffectsInvoice.ShouldBeFalse();
    }

    private static TailChanges Tail(RawBody body) =>
        UpdateTransactionRules.ReadTail(body).ShouldBeOfType<TailResult.Ok>().Changes;

    private static TransactionChanges Combine(RawBody body) =>
        UpdateTransactionRules.Combine(Head(body), UpdateTransactionRules.ReadReferences(body), Tail(body));
}
