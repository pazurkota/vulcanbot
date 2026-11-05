using System.Buffers.Text;
using System.Net;
using System.Text;
using vulcanbot.Service;

namespace vulcanbot.Test.Service;

public class EduVulcanApParserTests
{
    public static string Jwt(string tenant, string subject = "1") =>
        "eyJhbGciOiJSUzI1NiJ9." +
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($$"""{"tenant":"{{tenant}}","sub":"{{subject}}"}""")) +
        ".c2lnbmF0dXJl";

    public static string Ap(params string[] tokens) =>
        $$"""{"Success":true,"ErrorMessage":null,"IsConsentAccepted":true,"Tokens":[{{string.Join(",", tokens.Select(t => $"\"{t}\""))}}]}""";

    [Test]
    public void ParseTokensByTenant_GroupsTokensByJwtTenant()
    {
        var a1 = Jwt("szkolaa", "1");
        var a2 = Jwt("szkolaa", "2");
        var b1 = Jwt("szkolab", "3");

        var result = EduVulcanApParser.ParseTokensByTenant(Ap(a1, a2, b1));

        Assert.That(result, Is.EquivalentTo(new Dictionary<string, IReadOnlyList<string>>
        {
            ["szkolaa"] = [a1, a2],
            ["szkolab"] = [b1]
        }));
    }

    [Test]
    public void ParseTokensByTenant_AcceptsInputElementFromPageSource()
    {
        var token = Jwt("szkolaa");
        var html = $"""<input type="hidden" id="ap" value="{WebUtility.HtmlEncode(Ap(token))}" />""";

        var result = EduVulcanApParser.ParseTokensByTenant(html);

        Assert.That(result["szkolaa"], Is.EqualTo(new[] { token }));
    }

    [Test]
    public void ParseTokensByTenant_IgnoresSurroundingWhitespace()
    {
        var result = EduVulcanApParser.ParseTokensByTenant($"\n  {Ap(Jwt("szkolaa"))}  \n");

        Assert.That(result.Keys, Is.EqualTo(new[] { "szkolaa" }));
    }

    [Test]
    public void ParseTokensByTenant_SuccessFalse_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EduVulcanApParser.ParseTokensByTenant(
            """{"Success":false,"ErrorMessage":"Sesja wygasła","IsConsentAccepted":true,"Tokens":[]}"""));

        Assert.That(ex!.Message, Does.Contain("Sesja wygasła"));
    }

    [Test]
    public void ParseTokensByTenant_ConsentNotAccepted_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => EduVulcanApParser.ParseTokensByTenant(
            $$"""{"Success":true,"IsConsentAccepted":false,"Tokens":["{{Jwt("szkolaa")}}"]}"""));
    }

    [Test]
    public void ParseTokensByTenant_NoTokens_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => EduVulcanApParser.ParseTokensByTenant(Ap()));
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("<html><body>Zaloguj się</body></html>")]
    public void ParseTokensByTenant_NotApJson_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => EduVulcanApParser.ParseTokensByTenant(input));
    }

    [Test]
    public void ParseTokensByTenant_TokenWithoutTenant_ThrowsFormatException()
    {
        var token = "eyJhbGciOiJSUzI1NiJ9." + Base64Url.EncodeToString("""{"sub":"1"}"""u8) + ".c2ln";

        Assert.Throws<FormatException>(() => EduVulcanApParser.ParseTokensByTenant(Ap(token)));
    }
}
