using System.Security.Cryptography;
using System.Text;
using MeuSecretario.Application.Google;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Application.Google;

public class SignedTokenTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    private OAuthStateCodec State(string key = GoogleTestSupport.SecretKey) => new(GoogleTestSupport.Settings(secretKey: key), _time);

    private ConnectLinkTokenCodec Link(string key = GoogleTestSupport.SecretKey) => new(GoogleTestSupport.Settings(secretKey: key), _time);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    [Trait("AC", "010-75")]
    public void State_RoundTrip_ReturnsUserId()
    {
        var state = State().Create(42);

        state.Count(c => c == '.').ShouldBe(1);
        State().Verify(state).ShouldBe(42);
    }

    [Fact]
    [Trait("AC", "010-75")]
    public void State_PayloadHasTheDocumentedShape()
    {
        var state = State().Create(7);
        var payloadPart = state.Split('.')[0];
        var padded = payloadPart.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

        json.ShouldBe($$"""{"p":"gcal-oauth","u":7,"e":{{_time.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds()}}}""");
        var expectedSignature = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(GoogleTestSupport.SecretKey), Encoding.ASCII.GetBytes(payloadPart)));
        state.Split('.')[1].ShouldBe(expectedSignature);
    }

    [Fact]
    [Trait("AC", "010-75")]
    public void State_Tampered_IsInvalid()
    {
        var state = State().Create(42);
        var flipped = (state[0] == 'A' ? 'B' : 'A') + state[1..];

        State().Verify(flipped).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-75")]
    public void State_PayloadSwapped_IsInvalid()
    {
        var signedForSeven = State().Create(7);
        var other = State().Create(8);

        State().Verify(other.Split('.')[0] + "." + signedForSeven.Split('.')[1]).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-75")]
    public void State_SignedWithAnotherKey_IsInvalid()
    {
        var state = State("outra-chave-qualquer-com-32-bytes!!").Create(42);

        State().Verify(state).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-75")]
    public void State_Expired_IsInvalidAfterTenMinutesAndOneSecond()
    {
        var state = State().Create(42);

        _time.Advance(TimeSpan.FromMinutes(9));
        State().Verify(state).ShouldBe(42);

        _time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        State().Verify(state).ShouldBeNull();
    }

    [Theory]
    [InlineData("NDI=")] // formato antigo: base64 do id
    [InlineData("semponto")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(".")]
    [InlineData("a.b.c")]
    [InlineData("%%%.%%%")]
    [Trait("AC", "010-75")]
    public void State_OldFormatAndGarbage_AreInvalid(string? state)
    {
        State().Verify(state).ShouldBeNull();
    }

    [Fact]
    public void State_And_ConnectToken_AreNotInterchangeable()
    {
        State().Verify(Link().Create(5)).ShouldBeNull();
        Link().Verify(State().Create(5)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-53")]
    public void ConnectToken_RoundTripAndSevenDayExpiry()
    {
        var token = Link().Create(9);

        Link().Verify(token).ShouldBe(9);

        _time.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        Link().Verify(token).ShouldBe(9);

        _time.Advance(TimeSpan.FromSeconds(2));
        Link().Verify(token).ShouldBeNull();
    }
}
