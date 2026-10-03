using System.Net;
using System.Text;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.TestSupport;

/// <summary>Os fakes compartilhados têm teste próprio (plan 010 §1.6).</summary>
public class TestSupportTests
{
    [Fact]
    public async Task FakeHttpHandler_RecordsMethodUriHeadersAndBody_AndRepeatsTheLastResponse()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.Created, "{\"a\":1}").Enqueue(HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://x.test/p?q=1")
        {
            Content = new StringContent("{\"numero\":\"1\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-API-Key", "k");

        var first = await client.SendAsync(request);
        var second = await client.GetAsync("https://x.test/other");
        var third = await client.GetAsync("https://x.test/other");

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        third.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        handler.Requests.Count.ShouldBe(3);
        var recorded = handler.Requests[0];
        recorded.Method.ShouldBe(HttpMethod.Post);
        recorded.Uri.ToString().ShouldBe("https://x.test/p?q=1");
        recorded.Headers["x-api-key"].ShouldBe("k");
        recorded.Headers["Content-Type"].ShouldContain("application/json");
        FakeHttpHandler.AssertJsonBody(recorded, "{ \"numero\" : \"1\" }");
    }

    [Fact]
    public void FakeHttpHandler_AssertJsonBody_FailsOnDifferentPayload()
    {
        var recorded = new RecordedRequest(HttpMethod.Post, new Uri("https://x.test"), new Dictionary<string, string>(), "{\"a\":1}", null);

        Should.Throw<ShouldAssertException>(() => FakeHttpHandler.AssertJsonBody(recorded, "{\"a\":2}"));
    }

    [Fact]
    public async Task FakeHttpHandler_CanThrowAndDelay()
    {
        var time = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var handler = new FakeHttpHandler()
            .EnqueueException(new HttpRequestException("rede"))
            .EnqueueDelayed(TimeSpan.FromSeconds(30), HttpStatusCode.OK);
        using var client = new HttpClient(handler);

        await Should.ThrowAsync<HttpRequestException>(() => client.GetAsync("https://x.test"));
        await Should.ThrowAsync<OperationCanceledException>(() => client.GetAsync("https://x.test", time.Token));
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task InMemoryCache_FollowsTheRedisContract()
    {
        var time = new FakeTimeProvider();
        var cache = new InMemoryCache(time);

        (await cache.SetJsonAsync("k", "abc", TimeSpan.FromSeconds(60))).ShouldBeTrue();
        cache.RawValue("k").ShouldBe("\"abc\"");
        (await cache.GetJsonAsync<string>("k")).ShouldBe("abc");

        (await cache.SetIfNotExistsAsync("lock", "running", TimeSpan.FromSeconds(10))).ShouldBeTrue();
        (await cache.SetIfNotExistsAsync("lock", "running", TimeSpan.FromSeconds(10))).ShouldBeFalse();
        cache.RawValue("lock").ShouldBe("running");
        (await cache.GetJsonAsync<string>("lock")).ShouldBeNull(); // cru, não-JSON

        (await cache.SetJsonAsync("pending_event:55:1", 1, TimeSpan.FromSeconds(60))).ShouldBeTrue();
        (await cache.SetJsonAsync("pending_event:66:1", 1, TimeSpan.FromSeconds(60))).ShouldBeTrue();
        (await cache.KeysByPatternAsync("pending_event:55:*")).ShouldBe(["pending_event:55:1"]);

        time.Advance(TimeSpan.FromSeconds(11));
        (await cache.ExistsAsync("lock")).ShouldBeFalse();
        (await cache.ExistsAsync("k")).ShouldBeTrue();
        (await cache.DeleteAsync("nao-existe")).ShouldBeTrue();
    }

    [Fact]
    public async Task InMemoryCache_Unavailable_ReturnsNeutralValuesWithoutThrowing()
    {
        var cache = new InMemoryCache { Unavailable = true };

        (await cache.IsAvailableAsync()).ShouldBeFalse();
        (await cache.SetJsonAsync("k", 1, TimeSpan.FromSeconds(1))).ShouldBeFalse();
        (await cache.GetJsonAsync<int?>("k")).ShouldBeNull();
        (await cache.SetIfNotExistsAsync("k", "x", TimeSpan.FromSeconds(1))).ShouldBeFalse();
        (await cache.ExistsAsync("k")).ShouldBeFalse();
        (await cache.DeleteAsync("k")).ShouldBeFalse();
        (await cache.KeysByPatternAsync("*")).ShouldBeEmpty();
    }

    [Fact]
    public async Task FakeGoogleHttpClientFactory_RoutesTheSdkThroughTheFakeHandler()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.OK, "{\"items\":[]}");
        using var service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientFactory = new FakeGoogleHttpClientFactory(handler),
            ApplicationName = "MeuSecretario",
        });

        var list = await service.CalendarList.List().ExecuteAsync();

        list.Items.ShouldBeEmpty();
        handler.Requests.Count.ShouldBe(1);
        handler.Requests[0].Uri.Host.ShouldBe("www.googleapis.com");
        handler.Requests[0].Uri.AbsolutePath.ShouldEndWith("/users/me/calendarList");
    }
}
