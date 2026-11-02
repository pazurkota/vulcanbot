using System.Text.Json.Serialization;

namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Single entry returned by <c>api/mobile/register/hebe</c> — one student available on the registered device.
/// </summary>
public record HebeAccount(
    string TopLevelPartition,
    string Partition,
    string ClassDisplay,
    HebeUnit Unit,
    HebeConstituentUnit ConstituentUnit,
    HebePupil Pupil,
    IReadOnlyList<HebePeriod> Periods
    );

public record HebeUnit(
    int Id,
    string Symbol,
    string Short,
    [property: JsonPropertyName("RestURL")] string RestUrl,
    string Name,
    string DisplayName,
    string? Address
    );

public record HebeConstituentUnit(
    int Id,
    string Short,
    string Name,
    string? Address
    );

public record HebePupil(
    int Id,
    int LoginId,
    string? LoginValue,
    string FirstName,
    string? SecondName,
    string Surname,
    bool Sex // true = male, false = female
    );

public record HebePeriod(
    int Id,
    int Level,
    int Number,
    bool Current,
    bool Last,
    HebeDateTime Start,
    HebeDateTime End
    );
