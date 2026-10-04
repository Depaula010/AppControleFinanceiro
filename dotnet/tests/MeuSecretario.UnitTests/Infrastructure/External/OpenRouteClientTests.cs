using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.External.Routes;
using MeuSecretario.UnitTests.Infrastructure.Bot;
using MeuSecretario.UnitTests.TestSupport;

namespace MeuSecretario.UnitTests.Infrastructure.External;

public sealed class OpenRouteClientTests : IDisposable
{
    private const string GeoOk = "{\"features\":[{\"geometry\":{\"coordinates\":[-46.65,-23.56]},\"properties\":{\"label\":\"Av. Paulista, São Paulo\"}}]}";

    private const string RouteOk = "{\"routes\":[{\"summary\":{\"duration\":2759.4,\"distance\":12549.0},\"segments\":[{\"steps\":[{\"name\":\"Av. Paulista\"}]}]}]}";

    private static readonly Coordinates A = new(-23.5, -46.6);
    private static readonly Coordinates B = new(-23.6, -46.7);

    private readonly FakeHttpHandler _handler = new();
    private readonly FakeKeySelector _keys = new();
    private readonly FakeIntegrationSettings _settings = new() { OpenRouteBaseUrl = "https://ors.test/" };
    private readonly ListLogger<OpenRouteClient> _log = new();

    public void Dispose() => _handler.Dispose();

    private OpenRouteClient Client() => new(new StubHttpClientFactory(_handler), _keys, _settings, _log);

    [Fact]
    [Trait("AC", "010-117")]
    public async Task Geocode_SendsTheExpectedGet_AndReadsLonLatInTheRightOrder()
    {
        _handler.Enqueue(HttpStatusCode.OK, GeoOk);

        var result = await Client().GeocodeAsync("Av. Paulista, 1000", null, default);

        result.ShouldBe(new GeocodeResult(-23.56, -46.65, "Av. Paulista, São Paulo"));
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.GetLeftPart(UriPartial.Path).ShouldBe("https://ors.test/geocode/search");
        var query = Http.Query(request.Uri);
        query["api_key"].ShouldBe("chave-do-sistema");
        query["text"].ShouldBe("Av. Paulista, 1000");
        query["boundary.country"].ShouldBe("BR");
        query["size"].ShouldBe("1");
        _keys.Calls.ShouldBe([((int?)null, "openroute")]);
    }

    [Fact]
    [Trait("AC", "010-117")]
    public async Task Geocode_MissingLabel_FallsBackToTheAddress()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{\"features\":[{\"geometry\":{\"coordinates\":[-46.65,-23.56]}}]}");

        (await Client().GeocodeAsync("Rua X", null, default))!.Label.ShouldBe("Rua X");
    }

    [Theory]
    [InlineData("{\"features\":[]}")]
    [InlineData("{}")]
    [InlineData("{\"features\":[{\"geometry\":{\"coordinates\":[-74.0,40.7]}}]}")]
    [InlineData("{\"features\":[{\"geometry\":{}}]}")]
    [Trait("AC", "010-117")]
    public async Task Geocode_NoResultOutOfBrazilOrBadShape_ReturnsNullWithASingleRequest(string body)
    {
        _handler.Enqueue(HttpStatusCode.OK, body);

        (await Client().GeocodeAsync("Rua X", null, default)).ShouldBeNull();

        _handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-118")]
    public async Task Directions_SendsTheExactBody_AndTruncatesDuration()
    {
        _handler.Enqueue(HttpStatusCode.OK, RouteOk);

        var result = await Client().CalculateTravelTimeAsync(A, B, null, default);

        result.ShouldBe(new TravelResult(45, 12.5, "Av. Paulista"));
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsoluteUri.ShouldBe("https://ors.test/v2/directions/driving-car");
        request.Headers["Authorization"].ShouldBe("chave-do-sistema");
        request.Headers["Content-Type"].ShouldStartWith("application/json");
        FakeHttpHandler.AssertJsonBody(request, "{\"coordinates\":[[-46.6,-23.5],[-46.7,-23.6]]}");
        request.BodyText.ShouldBe("{\"coordinates\":[[-46.6,-23.5],[-46.7,-23.6]]}");
    }

    [Theory]
    [InlineData(12450.0, 12.4)]
    [InlineData(12550.0, 12.6)]
    [InlineData(12549.0, 12.5)]
    [Trait("AC", "010-118")]
    public async Task Directions_DistanceRoundsHalfToEven(double meters, double km)
    {
        _handler.Enqueue(HttpStatusCode.OK, $"{{\"routes\":[{{\"summary\":{{\"duration\":60,\"distance\":{meters.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}}}}}]}}");

        (await Client().CalculateTravelTimeAsync(A, B, null, default))!.DistanceKm.ShouldBe(km);
    }

    [Theory]
    [InlineData("{\"routes\":[{\"summary\":{\"duration\":60,\"distance\":1000}}]}", null)]
    [InlineData("{\"routes\":[{\"summary\":{\"duration\":60,\"distance\":1000},\"segments\":[]}]}", null)]
    [InlineData("{\"routes\":[{\"summary\":{\"duration\":60,\"distance\":1000},\"segments\":[{\"steps\":[]}]}]}", null)]
    [InlineData("{\"routes\":[{\"summary\":{\"duration\":60,\"distance\":1000},\"segments\":[{\"steps\":[{\"distance\":3}]}]}]}", "")]
    [Trait("AC", "010-118")]
    public async Task Directions_RouteSummaryRules(string body, string? expected)
    {
        _handler.Enqueue(HttpStatusCode.OK, body);

        (await Client().CalculateTravelTimeAsync(A, B, null, default))!.RouteSummary.ShouldBe(expected);
    }

    [Theory]
    [InlineData("{\"routes\":[]}")]
    [InlineData("{}")]
    [Trait("AC", "010-118")]
    public async Task Directions_NoRoutes_ReturnsNullWithASingleRequest(string body)
    {
        _handler.Enqueue(HttpStatusCode.OK, body);

        (await Client().CalculateTravelTimeAsync(A, B, null, default)).ShouldBeNull();

        _handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-119")]
    public async Task Retry_5xxThenOk_SucceedsWithTwoRequests()
    {
        _handler.Enqueue(HttpStatusCode.InternalServerError, "{}").Enqueue(HttpStatusCode.OK, RouteOk);

        (await Client().CalculateTravelTimeAsync(A, B, null, default)).ShouldNotBeNull();

        _handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-119")]
    public async Task Retry_5xxTwice_ReturnsNull()
    {
        _handler.Enqueue(HttpStatusCode.BadGateway, "{}").Enqueue(HttpStatusCode.ServiceUnavailable, "{}");

        (await Client().GeocodeAsync("Rua X", null, default)).ShouldBeNull();

        _handler.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [Trait("AC", "010-119")]
    public async Task NoRetry_On4xx_Deviation064(HttpStatusCode status)
    {
        _handler.Enqueue(status, "{}");

        (await Client().CalculateTravelTimeAsync(A, B, null, default)).ShouldBeNull();
        (await Client().GeocodeAsync("Rua X", null, default)).ShouldBeNull();

        _handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-119")]
    public async Task Retry_TimeoutAndNetworkError_AreRepeated_ThenNull()
    {
        _handler.EnqueueException(new TaskCanceledException("t")).EnqueueException(new TaskCanceledException("t"));
        (await Client().CalculateTravelTimeAsync(A, B, null, default)).ShouldBeNull();
        _handler.Requests.Count.ShouldBe(2);

        _handler.EnqueueException(new HttpRequestException("falha https://x?api_key=chave-do-sistema")).Enqueue(HttpStatusCode.OK, GeoOk);
        (await Client().GeocodeAsync("Rua secreta 99", null, default)).ShouldNotBeNull();
        _handler.Requests.Count.ShouldBe(4);

        _log.All.ShouldNotContain("chave-do-sistema");
        _log.All.ShouldNotContain("secreta");
    }

    [Fact]
    [Trait("AC", "010-119")]
    public async Task InvalidJson_ReturnsNullWithoutRetry()
    {
        _handler.Enqueue(HttpStatusCode.OK, "<html>");

        (await Client().GeocodeAsync("Rua X", null, default)).ShouldBeNull();

        _handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-119")]
    public async Task UserKey_Resolved_IsUsed_AndFailedThrowsWithoutRequest()
    {
        _keys.Users[7] = ProviderKeyResult.WithKey("chave-do-usuario");
        _keys.Users[8] = FakeKeySelector.Failure("Limite diário atingido");
        _handler.Enqueue(HttpStatusCode.OK, RouteOk);
        var client = Client();

        await client.CalculateTravelTimeAsync(A, B, 7, default);
        _handler.Requests[0].Headers["Authorization"].ShouldBe("chave-do-usuario");

        var geo = await Should.ThrowAsync<ApiKeyUnavailableException>(() => client.GeocodeAsync("Rua X", 8, default));
        geo.Message.ShouldBe("Limite diário atingido");
        await Should.ThrowAsync<ApiKeyUnavailableException>(() => client.CalculateTravelTimeAsync(A, B, 8, default));
        _handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task NoSystemKey_ReturnsNullWithoutRequest()
    {
        _keys.SystemKey = null;

        (await Client().GeocodeAsync("Rua X", null, default)).ShouldBeNull();
        (await Client().CalculateTravelTimeAsync(A, B, null, default)).ShouldBeNull();

        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-122")]
    public async Task Logs_NeverContainAddressCoordinatesOrKey()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{\"features\":[{\"geometry\":{\"coordinates\":[-74.0,40.7]}}]}");
        _handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        var client = Client();

        await client.GeocodeAsync("Rua Confidencial 123", null, default);
        await client.CalculateTravelTimeAsync(A, B, null, default);

        _log.All.ShouldNotContain("Confidencial");
        _log.All.ShouldNotContain("40.7");
        _log.All.ShouldNotContain("-23.5");
        _log.All.ShouldNotContain("chave-do-sistema");
        _log.All.ShouldNotContain("api_key");
    }
}
