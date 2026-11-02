namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Date/time object as returned by Hebe, e.g.
/// { "Timestamp": 1730505600000, "Date": "2024-11-02", "DateDisplay": "02.11.2024", "Time": "00:00:00" }
/// </summary>
public record HebeDateTime(
    long Timestamp,
    string Date,
    string DateDisplay,
    string Time
    )
{
    public DateTime ToUtcDateTime() => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).UtcDateTime;

    public DateOnly ToDateOnly() => DateOnly.ParseExact(Date, "yyyy-MM-dd");
}
