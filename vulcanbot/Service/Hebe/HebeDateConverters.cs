using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vulcanbot.Service.Hebe;

/// <summary>
/// Hebe CE sends dates as strings (<c>CreatedAt</c>, <c>DeadlineAt</c>, …). Accepts ISO 8601 with a <c>T</c>
/// or a space, with or without an offset, and Unix milliseconds. Times without an offset are Polish local time.
/// </summary>
public class HebeDateTimeConverter : JsonConverter<DateTime>
{
    private static readonly TimeZoneInfo? Poland = FindPoland();

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64()).UtcDateTime;

        var text = reader.GetString();
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            throw new JsonException($"'{text}' is not a date/time.");

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => Poland is null
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : TimeZoneInfo.ConvertTimeToUtc(value, Poland)
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    /// <summary>
    /// Calendar day in Poland of a UTC time read by this converter.
    /// </summary>
    public static DateOnly ToPolishDate(DateTime utc) =>
        DateOnly.FromDateTime(Poland is null ? utc : TimeZoneInfo.ConvertTimeFromUtc(utc, Poland));

    private static TimeZoneInfo? FindPoland()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        }
        catch (TimeZoneNotFoundException)
        {
            // No tzdata in the container — fall back to treating times as UTC.
            return null;
        }
    }
}

/// <summary>
/// Date-only Hebe CE fields (<c>DateAt</c>, <c>DeadlineAt</c>, <c>StartAt</c>) — accepts a plain date
/// or a date with a time part, which is ignored.
/// </summary>
public class HebeDateOnlyConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is { Length: >= 10 } &&
            DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var value))
            return value;

        throw new JsonException($"'{text}' is not a date.");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}

/// <summary>
/// Lesson times (<c>TimeSlot.Start</c>/<c>End</c>) — accepts <c>08:00</c> and <c>08:00:00</c>.
/// </summary>
public class HebeTimeOnlyConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = ["HH:mm", "HH:mm:ss", "H:mm", "H:mm:ss"];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (TimeOnly.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            return value;

        throw new JsonException($"'{text}' is not a time.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
}
