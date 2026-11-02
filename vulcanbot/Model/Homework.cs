using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace vulcanbot.Model;

[Index(nameof(AccountId), nameof(VulcanId), IsUnique = true)]
public class Homework
{
    public int Id { get; set; }
    public int VulcanId { get; set; }

    public Guid AccountId { get; set; }
    public VulcanAccount Account { get; set; } = null!;

    public string Content { get; set; } = null!;
    [MaxLength(128)] public string SubjectName { get; set; } = null!;
    [MaxLength(256)] public string TeacherName { get; set; } = null!;
    public bool IsAnswerRequired { get; set; }

    public DateOnly Deadline { get; set; }
    public DateOnly? AnswerDeadline { get; set; }
    public DateTime CreatedAt { get; set; }

    [MaxLength(256)] public string? CalendarEventId { get; set; }
    public DateTime? CalendarSyncedAt { get; set; }
}
