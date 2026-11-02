using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace vulcanbot.Model;

[Index(nameof(AccountId), nameof(VulcanId), IsUnique = true)]
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

    [MaxLength(256)] public string? CalendarEventId { get; set; }
    public DateTime? CalendarSyncedAt { get; set; }
}

public enum LessonChangeType
{
    None = 0,
    Changed = 1,
    Cancelled = 2,
    MovedAway = 3,
    MovedHere = 4
}
