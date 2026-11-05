using Microsoft.EntityFrameworkCore;
using vulcanbot.Data;
using vulcanbot.Dto.Hebe;
using vulcanbot.Model;
using vulcanbot.Service.Hebe;

namespace vulcanbot.Service;

/// <summary>
/// Fetches exams (tests, quizzes) from Hebe and upserts them into the database by (AccountId, VulcanId).
/// </summary>
public class ExamSyncService(HebeClient hebe, VulcanDbContext db)
{
    public async Task<SyncResult> SyncAsync(
        VulcanAccount account,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken
        )
    {
        var url = account.HebeByPupilUrl("mobile/exam/byPupil", from, to);

        var fetched = (await hebe.GetAsync<List<HebeExam>>(url, account.ToDeviceKey(), account.PupilId,
            cancellationToken) ?? []).DistinctBy(e => e.Id).ToList();

        var ids = fetched.Select(e => e.Id).ToList();
        var existing = await db.Exams
            .Where(e => e.AccountId == account.Id && ids.Contains(e.VulcanId))
            .ToDictionaryAsync(e => e.VulcanId, cancellationToken);

        int added = 0, updated = 0;
        foreach (var item in fetched)
        {
            if (!existing.TryGetValue(item.Id, out var exam))
            {
                exam = new Exam { AccountId = account.Id, VulcanId = item.Id };
                Apply(exam, item);
                db.Exams.Add(exam);
                added++;
                continue;
            }

            Apply(exam, item);
            var entry = db.Entry(exam);
            entry.DetectChanges();
            if (entry.State != EntityState.Modified) continue;

            // Changed in Vulcan — the calendar event needs to be updated on the next calendar sync.
            exam.CalendarSyncedAt = null;
            updated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SyncResult(fetched.Count, added, updated);
    }

    private static void Apply(Exam exam, HebeExam item)
    {
        exam.Type = item.Type;
        exam.Content = item.Content;
        exam.SubjectName = item.Subject.Name;
        exam.TeacherName = item.Creator.DisplayName;
        exam.Deadline = HebeDateTimeConverter.ToPolishDate(item.DeadlineAt);
        exam.CreatedAt = item.CreatedAt;
        exam.ModifiedAt = item.ModifiedAt;
    }
}
