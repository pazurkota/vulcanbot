using System.Text.Json;
using vulcanbot.Service.Hebe;

namespace vulcanbot.Test.Service;

public class HebeDateConvertersTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new HebeDateTimeConverter(), new HebeDateOnlyConverter() }
    };

    [TestCase("\"2026-11-01 09:00:00\"", "2026-11-01T08:00:00Z", Description = "Polish winter time, space")]
    [TestCase("\"2026-11-01T09:00:00\"", "2026-11-01T08:00:00Z", Description = "Polish winter time, T")]
    [TestCase("\"2026-07-01T10:00:00\"", "2026-07-01T08:00:00Z", Description = "Polish summer time")]
    [TestCase("\"2026-11-01T08:00:00Z\"", "2026-11-01T08:00:00Z", Description = "UTC")]
    [TestCase("\"2026-11-01T10:00:00+02:00\"", "2026-11-01T08:00:00Z", Description = "explicit offset")]
    [TestCase("1793520000000", "2026-11-01T08:00:00Z", Description = "Unix milliseconds")]
    public void DateTime_IsReadAsUtc(string json, string expectedUtc)
    {
        var value = JsonSerializer.Deserialize<DateTime>(json, Options);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(DateTime.Parse(expectedUtc).ToUniversalTime()));
            Assert.That(value.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [TestCase("\"2026-11-12\"")]
    [TestCase("\"2026-11-12T00:00:00\"")]
    [TestCase("\"2026-11-12 00:00:00\"")]
    public void DateOnly_IgnoresTimePart(string json)
    {
        Assert.That(JsonSerializer.Deserialize<DateOnly>(json, Options), Is.EqualTo(new DateOnly(2026, 11, 12)));
    }

    [TestCase("\"jutro\"")]
    [TestCase("\"\"")]
    public void InvalidValues_ThrowJsonException(string json)
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>(json, Options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateOnly>(json, Options));
        });
    }

    [Test]
    public void ToPolishDate_UsesPolishCalendarDay()
    {
        var utc = new DateTime(2026, 11, 10, 23, 0, 0, DateTimeKind.Utc); // 00:00 on 11 Nov in Poland

        Assert.That(HebeDateTimeConverter.ToPolishDate(utc), Is.EqualTo(new DateOnly(2026, 11, 11)));
    }
}
