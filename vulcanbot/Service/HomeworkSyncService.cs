using System.Globalization;
using Microsoft.EntityFrameworkCore;
using vulcanbot.Data;
using vulcanbot.Dto.Hebe;
using vulcanbot.Model;
using vulcanbot.Service.Hebe;

namespace vulcanbot.Service;

public record HomeworkSyncResult(int Fetched, int Added, int Updated);

/// <summary>
/// Fetches homework from Hebe and upserts it into the database by (AccountId, VulcanId).
/// </summary>
public class HomeworkSyncService(HebeClient hebe, VulcanDbContext db)
{
    private const int PageSize = 500;

    public async Task<HomeworkSyncResult> SyncAsync(
        VulcanAccount account,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken
        )
    {
        var url = account.HebeUrl("mobile/homework/byPupil") +
                  $"?unitId={account.UnitId}" +
                  $"&pupilId={account.PupilId}" +
                  $"&dateFrom={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                  $"&dateTo={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
                  $"&pageSize={PageSize}";

        var fetched = (await hebe.GetAsync<List<HebeHomework>>(url, account.ToDeviceKey(), account.PupilId,
            cancellationToken) ?? []).DistinctBy(h => h.Id).ToList();

        var ids = fetched.Select(h => h.Id).ToList();
        var existing = await db.Homeworks
            .Where(h => h.AccountId == account.Id && ids.Contains(h.VulcanId))
            .ToDictionaryAsync(h => h.VulcanId, cancellationToken);

        int added = 0, updated = 0;
        foreach (var item in fetched)
        {
            if (!existing.TryGetValue(item.Id, out var homework))
            {
                homework = new Homework { AccountId = account.Id, VulcanId = item.Id };
                Apply(homework, item);
                db.Homeworks.Add(homework);
                added++;
                continue;
            }

            Apply(homework, item);
            var entry = db.Entry(homework);
            entry.DetectChanges();
            if (entry.State != EntityState.Modified) continue;

            // Changed in Vulcan — the calendar event needs to be updated on the next calendar sync.
            homework.CalendarSyncedAt = null;
            updated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new HomeworkSyncResult(fetched.Count, added, updated);
    }

    private static void Apply(Homework homework, HebeHomework item)
    {
        homework.Content = item.Content;
        homework.SubjectName = item.Subject.Name;
        homework.TeacherName = item.Creator.DisplayName;
        homework.IsAnswerRequired = item.IsAnswerRequired;
        homework.Deadline = item.DeadlineAt;
        homework.AnswerDeadline = item.AnswerAt is { } answerAt ? HebeDateTimeConverter.ToPolishDate(answerAt) : null;
        homework.CreatedAt = item.CreatedAt;
    }
}
