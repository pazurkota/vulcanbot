using System.Text.Json.Serialization;

namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Timetable entry returned by <c>/api/mobile/schedule/withchanges/byPupil</c>.
/// </summary>
public record HebeLesson(
    int Id,
    [property: JsonPropertyName("DateAt")] DateOnly Date,
    HebeTimeSlot TimeSlot,
    HebeRoom? Room = null,
    HebeTeacher? TeacherPrimary = null,
    HebeTeacher? TeacherSecondary = null,
    HebeSubject? Subject = null,
    string? Event = null,
    HebeLessonSubstitution? Substitution = null,
    [property: JsonPropertyName("Clazz")] HebeClass? Class = null,
    string? PupilAlias = null
    );

/// <summary>
/// Substitution / cancellation of a lesson. eduVULCAN returns these from
/// <c>/api/mobile/schedule/changes/byPupil</c> (linked by <see cref="ScheduleId"/>);
/// some schools embed them in <see cref="HebeLesson.Substitution"/> instead.
/// </summary>
public record HebeLessonSubstitution(
    int Id,
    [property: JsonPropertyName("DateAt")] DateOnly Date,
    int? ScheduleId = null,
    [property: JsonPropertyName("ChangeDateAt")] DateOnly? ChangeDate = null,
    string? PupilNote = null,
    string? Reason = null,
    string? Event = null,
    HebeRoom? Room = null,
    HebeTimeSlot? TimeSlot = null,
    HebeSubject? Subject = null,
    HebeTeacher? TeacherPrimary = null,
    HebeTeacher? TeacherSecondary = null,
    HebeLessonChange? Change = null,
    bool ClassAbsence = false,
    bool NoRoom = false,
    string? Description = null
    );

public record HebeTimeSlot(
    int Id,
    TimeOnly Start,
    TimeOnly End,
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

/// <param name="Type">1 = cancelled, 2 = substitution, 3 = moved to another day/slot, 4 = classes merged.</param>
public record HebeLessonChange(
    int Id,
    int Type,
    bool IsMerge,
    bool Separation
    );
