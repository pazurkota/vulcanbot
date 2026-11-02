using vulcanbot.Service.Hebe;
using vulcanbot.Test.Helpers;

namespace vulcanbot.Test.Service.Hebe;

public class VulcanRoutingServiceTests
{
    // Excerpt of the real file served at RoutingRulesUrl.
    private const string Rules = "3S1,https://lekcjaplus.vulcan.net.pl\r\nTA1,https://uonetplus-komunikacja.umt.tarnow.pl\r\nKA1,https://uonetplus-komunikacja.mcuw.katowice.eu/\r\n";

    private FakeHttpMessageHandler _handler = null!;
    private HttpClient _client = null!;
    private VulcanRoutingService _service = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeHttpMessageHandler();
        _handler.RespondTo(VulcanRoutingService.RoutingRulesUrl, Rules, contentType: "text/plain");
        _client = new HttpClient(_handler);
        _service = new VulcanRoutingService(_client);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [TestCase("3S1ABCD", "https://lekcjaplus.vulcan.net.pl")]
    [TestCase("ta1xyz", "https://uonetplus-komunikacja.umt.tarnow.pl")]
    [TestCase("KA1QQQ", "https://uonetplus-komunikacja.mcuw.katowice.eu")]
    public async Task GetBaseUrlAsync_ResolvesByTokenPrefix(string token, string expected)
    {
        Assert.That(await _service.GetBaseUrlAsync(token, CancellationToken.None), Is.EqualTo(expected));
    }

    [Test]
    public void GetBaseUrlAsync_UnknownPrefix_ThrowsInvalidToken()
    {
        var ex = Assert.ThrowsAsync<VulcanApiException>(() => _service.GetBaseUrlAsync("ZZZ123", CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(VulcanErrorKind.InvalidToken));
    }

    [Test]
    public void GetBaseUrlAsync_TooShortToken_ThrowsWithoutDownloadingRules()
    {
        var ex = Assert.ThrowsAsync<VulcanApiException>(() => _service.GetBaseUrlAsync("3S", CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(VulcanErrorKind.InvalidToken));
        Assert.That(_handler.Requests, Is.Empty);
    }

    [Test]
    public async Task GetBaseUrlAsync_DownloadsRulesOnlyOnce()
    {
        await _service.GetBaseUrlAsync("3S1A", CancellationToken.None);
        await _service.GetBaseUrlAsync("TA1A", CancellationToken.None);

        Assert.That(_handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseRules_IgnoresBlankAndMalformedLines()
    {
        var rules = VulcanRoutingService.ParseRules("\n3S1,https://a.pl\nbroken\n,https://b.pl\nXX1,\n");

        Assert.That(rules, Is.EquivalentTo(new Dictionary<string, string> { ["3S1"] = "https://a.pl" }));
    }
}
