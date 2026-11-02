using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace vulcanbot.Model;

[Index(nameof(AccountId), nameof(VulcanId), IsUnique = true)]
public class Exam
{
    public int Id { get; set; }
    public int VulcanId { get; set; }

    public Guid AccountId { get; set; }
    public VulcanAccount Account { get; set; } = null!;

    [MaxLength(64)] public string Type { get; set; } = null!;
    public string Content { get; set; } = null!;
    [MaxLength(128)] public string SubjectName { get; set; } = null!;
    [MaxLength(256)] public string TeacherName { get; set; } = null!;

    public DateOnly Deadline { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    /// <summary>Id of the event in the external calendar; null = not synced yet.</summary>
    [MaxLength(256)] public string? CalendarEventId { get; set; }
    public DateTime? CalendarSyncedAt { get; set; }
}
