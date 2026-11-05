using Microsoft.EntityFrameworkCore;
using vulcanbot.Data;
using vulcanbot.Dto.Hebe;
using vulcanbot.Model;
using vulcanbot.Service.Hebe;

namespace vulcanbot.Service;

/// <summary>
/// Fetches the timetable together with substitutions and cancellations and upserts it into the database
/// by (AccountId, VulcanId, Date).
/// </summary>
public class LessonSyncService(HebeClient hebe, VulcanDbContext db)
{
    private const int Cancelled = 1;
    private const int Moved = 3;

    public async Task<SyncResult> SyncAsync(
        VulcanAccount account,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken
        )
    {
        var key = account.ToDeviceKey();

        var fetched = (await hebe.GetAsync<List<HebeLesson>>(
            account.HebeByPupilUrl("mobile/schedule/withchanges/byPupil", from, to), key, account.PupilId,
            cancellationToken) ?? []).DistinctBy(l => (l.Id, l.Date)).ToList();

        // eduVULCAN publishes substitutions/cancellations here rather than in withchanges.
        var changes = (await hebe.GetAsync<List<HebeLessonSubstitution>>(
            account.HebeByPupilUrl("mobile/schedule/changes/byPupil", from, to), key, account.PupilId,
            cancellationToken) ?? [])
            .Where(c => c.ScheduleId is not null)
            .DistinctBy(c => (c.ScheduleId, c.Date))
            .ToDictionary(c => (ScheduleId: c.ScheduleId!.Value, c.Date));

        var ids = fetched.Select(l => l.Id).Distinct().ToList();
        var existing = await db.Lessons
            .Where(l => l.AccountId == account.Id && ids.Contains(l.VulcanId))
            .ToDictionaryAsync(l => (l.VulcanId, l.Date), cancellationToken);

        int added = 0, updated = 0;
        foreach (var item in fetched)
        {
            var change = changes.GetValueOrDefault((item.Id, item.Date)) ?? item.Substitution;

            if (!existing.TryGetValue((item.Id, item.Date), out var lesson))
            {
                lesson = new Lesson { AccountId = account.Id, VulcanId = item.Id };
                Apply(lesson, item, change);
                db.Lessons.Add(lesson);
                added++;
                continue;
            }

            Apply(lesson, item, change);
            var entry = db.Entry(lesson);
            entry.DetectChanges();
            if (entry.State != EntityState.Modified) continue;

            // Changed in Vulcan — the calendar event needs to be updated on the next calendar sync.
            lesson.CalendarSyncedAt = null;
            updated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SyncResult(fetched.Count, added, updated);
    }

    private static void Apply(Lesson lesson, HebeLesson item, HebeLessonSubstitution? change)
    {
        var changeType = ToChangeType(change);

        // A substitution replaces teacher/subject/room/time; a cancelled or moved lesson keeps the original
        // details so it is clear which lesson isn't happening.
        var replacement = changeType == LessonChangeType.Changed ? change : null;
        var slot = replacement?.TimeSlot ?? item.TimeSlot;

        lesson.Date = item.Date;
        lesson.Start = slot.Start;
        lesson.End = slot.End;
        lesson.Position = slot.Position;
        lesson.SubjectName = (replacement?.Subject ?? item.Subject)?.Name;
        lesson.TeacherName = (replacement?.TeacherPrimary ?? item.TeacherPrimary)?.DisplayName;
        lesson.Room = replacement is { NoRoom: true } ? null : (replacement?.Room ?? item.Room)?.Code;
        lesson.Event = change?.Event ?? item.Event;
        lesson.ChangeType = changeType;
        lesson.ChangeReason = change is null
            ? null
            : new[] { change.Reason, change.PupilNote, change.Description }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        lesson.MovedToDate = changeType == LessonChangeType.MovedAway ? change!.ChangeDate : null;
    }

    private static LessonChangeType ToChangeType(HebeLessonSubstitution? change) => change switch
    {
        null => LessonChangeType.None,
        { ClassAbsence: true } or { Change.Type: Cancelled } => LessonChangeType.Cancelled,
        { Change.Type: Moved } => LessonChangeType.MovedAway,
        _ => LessonChangeType.Changed
    };
}
