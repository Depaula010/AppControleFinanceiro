using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.External;
using MeuSecretario.UnitTests.Infrastructure.Bot;
using MeuSecretario.UnitTests.TestSupport;

namespace MeuSecretario.UnitTests.Infrastructure.External;

public sealed class ProviderKeyValidatorTests : IDisposable
{
    private readonly FakeHttpHandler _handler = new();
    private readonly ListLogger<ProviderKeyValidator> _log = new();

    public void Dispose() => _handler.Dispose();

    private ProviderKeyValidator Validator() => new(new StubHttpClientFactory(_handler), _log);

    [Theory]
    [InlineData(HttpStatusCode.OK, KeyValidation.Valid)]
    [InlineData(HttpStatusCode.Unauthorized, KeyValidation.Invalid)]
    [InlineData(HttpStatusCode.Forbidden, KeyValidation.Invalid)]
    [InlineData(HttpStatusCode.BadRequest, KeyValidation.Invalid)]
    [Trait("AC", "010-116")]
    public async Task Gemini_ValidOnlyOn200_KeyInHeaderNotInUrl(HttpStatusCode status, KeyValidation expected)
    {
        _handler.Enqueue(status, "{}");

        (await Validator().ValidateAsync("gemini", "k-gem", default)).ShouldBe(expected);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.AbsoluteUri.ShouldBe("https://generativelanguage.googleapis.com/v1beta/models");
        request.Headers["x-goog-api-key"].ShouldBe("k-gem");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, KeyValidation.Valid)]
    [InlineData(HttpStatusCode.Forbidden, KeyValidation.Valid)]
    [InlineData(HttpStatusCode.Unauthorized, KeyValidation.Invalid)]
    [Trait("AC", "010-116")]
    public async Task Weather_ValidUnlessUnauthorized_AgainstWeatherApi(HttpStatusCode status, KeyValidation expected)
    {
        _handler.Enqueue(status, "{}");

        (await Validator().ValidateAsync("weather", "k-w", default)).ShouldBe(expected);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Uri.Scheme.ShouldBe("https");
        request.Uri.Host.ShouldBe("api.weatherapi.com");
        request.Uri.AbsolutePath.ShouldBe("/v1/current.json");
        var query = Http.Query(request.Uri);
        query["key"].ShouldBe("k-w");
        query["q"].ShouldBe("London");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, KeyValidation.Valid)]
    [InlineData(HttpStatusCode.Unauthorized, KeyValidation.Valid)]
    [InlineData(HttpStatusCode.Forbidden, KeyValidation.Invalid)]
    [Trait("AC", "010-116")]
    public async Task OpenRoute_ValidUnlessForbidden_KeyInAuthorizationHeader(HttpStatusCode status, KeyValidation expected)
    {
        _handler.Enqueue(status, "{}");

        (await Validator().ValidateAsync("openroute", "k-ors", default)).ShouldBe(expected);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Uri.AbsoluteUri.ShouldBe("https://api.openrouteservice.org/v2/directions/driving-car");
        request.Headers["Authorization"].ShouldBe("k-ors");
    }

    [Fact]
    [Trait("AC", "010-116")]
    public async Task UnknownProvider_IsNotSupported_WithoutRequest()
    {
        (await Validator().ValidateAsync("openai", "k", default)).ShouldBe(KeyValidation.NotSupported);

        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-116")]
    public async Task NetworkErrorAndTimeout_ThrowProviderUnavailable_WithoutLeakingTheKey()
    {
        _handler.EnqueueException(new HttpRequestException("falha https://x?key=k-segredo"));
        _handler.EnqueueException(new TaskCanceledException("timeout"));
        var validator = Validator();

        var first = await Should.ThrowAsync<ProviderUnavailableException>(() => validator.ValidateAsync("weather", "k-segredo", default));
        await Should.ThrowAsync<ProviderUnavailableException>(() => validator.ValidateAsync("gemini", "k-segredo", default));

        first.Message.ShouldNotContain("k-segredo");
        _log.All.ShouldNotContain("k-segredo");
    }
}
