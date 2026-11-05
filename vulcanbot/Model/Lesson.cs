using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace vulcanbot.Model;

// Date is part of the key in case Hebe reuses a schedule Id for the same slot every week.
[Index(nameof(AccountId), nameof(VulcanId), nameof(Date), IsUnique = true)]
public class Lesson
{
    public int Id { get; set; }
    public int VulcanId { get; set; }

    public Guid AccountId { get; set; }
    public VulcanAccount Account { get; set; } = null!;

    public DateOnly Date { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public int Position { get; set; }

    [MaxLength(128)] public string? SubjectName { get; set; }
    [MaxLength(256)] public string? TeacherName { get; set; }
    [MaxLength(64)] public string? Room { get; set; }
    public string? Event { get; set; }

    public LessonChangeType ChangeType { get; set; } = LessonChangeType.None;

    /// <summary>Reason / note for the change shown to pupils, e.g. "wycieczka klasowa".</summary>
    public string? ChangeReason { get; set; }

    /// <summary>For <see cref="LessonChangeType.MovedAway"/>: the day the lesson was moved to, when known.</summary>
    public DateOnly? MovedToDate { get; set; }

    /// <summary>
    /// Set when the lesson disappeared from the timetable but still has a calendar event to delete.
    /// Lessons without an event are deleted right away.
    /// </summary>
    public DateTime? RemovedAt { get; set; }

    [MaxLength(256)] public string? CalendarEventId { get; set; }
    public DateTime? CalendarSyncedAt { get; set; }
}

/// <summary>
/// Stored as text, so the numeric values don't have to match Hebe's <c>Change.Type</c>.
/// </summary>
public enum LessonChangeType
{
    None = 0,

    /// <summary>Substitution or merged classes — teacher, subject or room may differ.</summary>
    Changed = 1,
    Cancelled = 2,
    MovedAway = 3,
    MovedHere = 4
}
