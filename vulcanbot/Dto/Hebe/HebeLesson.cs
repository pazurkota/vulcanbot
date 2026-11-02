using System.Text.Json.Serialization;

namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Timetable entry returned by <c>/api/mobile/schedule/withchanges/byPupil</c>.
/// </summary>
public record HebeLesson(
    int Id,
    HebeDateTime Date,
    HebeTimeSlot TimeSlot,
    HebeRoom? Room,
    HebeTeacher? TeacherPrimary,
    HebeTeacher? TeacherSecondary,
    HebeSubject? Subject,
    string? Event,
    HebeLessonChange? Change,
    [property: JsonPropertyName("Clazz")] HebeClass? Class,
    string? PupilAlias,
    bool Visible
    );

public record HebeTimeSlot(
    int Id,
    string Start,
    string End,
    string Display,
    int Position
    );

public record HebeRoom(
    int Id,
    string Code
    );

public record HebeClass(
    int Id,
    string Key,
    string DisplayName,
    string Symbol
    );

/// <param name="Type">1 = substitution/change, 2 = cancelled, 3 = moved, 4 = moved here.</param>
public record HebeLessonChange(
    int Id,
    int Type,
    bool IsMerge,
    bool Separation
    );
