using System.Globalization;
using Microsoft.Extensions.Logging;
using vulcanbot.Dto;
using vulcanbot.Dto.Hebe;
using vulcanbot.Service.Hebe;

namespace vulcanbot.Service;

/// <summary>
/// Reads students, exams, homework and the timetable from the Hebe API.
/// </summary>
public class VulcanDataService(HebeApiClient api, ILogger<VulcanDataService> logger)
{
    public const string StudentListPath = "api/mobile/register/hebe";
    public const int PageSize = 500;

    // Values used by both reference clients; Vulcan expects them even without real paging.
    private const string LastId = "-2147483648";
    private const string LastSyncDate = "1970-01-01 01:00:00";

    /// <summary>Students available on the registered device.</summary>
    public async Task<IReadOnlyList<HebeAccount>> GetStudentsAsync(
        DeviceKey deviceKey,
        string restUrl,
        CancellationToken cancellationToken)
    {
        var url = $"{restUrl.TrimEnd('/')}/{StudentListPath}";
        return await api.GetAsync<List<HebeAccount>>(url, deviceKey, cancellationToken);
    }

    /// <summary>Exams whose deadline falls within <paramref name="from"/>..<paramref name="to"/> (inclusive).</summary>
    public async Task<IReadOnlyList<HebeExam>> GetExamsAsync(
        VulcanStudentContext student, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var exams = await GetByPupilAsync<HebeExam>(student, "exam", from, to, cancellationToken);
        return exams.Where(e => InRange(e.Deadline.ToDateOnly(), from, to)).ToList();
    }

    /// <summary>Homework whose deadline falls within <paramref name="from"/>..<paramref name="to"/> (inclusive).</summary>
    public async Task<IReadOnlyList<HebeHomework>> GetHomeworkAsync(
        VulcanStudentContext student, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var homework = await GetByPupilAsync<HebeHomework>(student, "homework", from, to, cancellationToken);
        return homework.Where(h => InRange(h.Deadline.ToDateOnly(), from, to)).ToList();
    }

    /// <summary>Timetable entries (including cancellations/substitutions, see <see cref="HebeLesson.Change"/>).</summary>
    public async Task<IReadOnlyList<HebeLesson>> GetLessonsAsync(
        VulcanStudentContext student, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var lessons = await GetByPupilAsync<HebeLesson>(student, "schedule", from, to, cancellationToken);
        return lessons.Where(l => InRange(l.Date.ToDateOnly(), from, to)).ToList();
    }

    private async Task<List<T>> GetByPupilAsync<T>(
        VulcanStudentContext student, string resource, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from) throw new ArgumentException("'to' must not be earlier than 'from'.", nameof(to));

        var url = $"{student.DataUrl}api/mobile/{resource}/byPupil?" + BuildQuery(
            ("unitId", student.UnitId.ToString(CultureInfo.InvariantCulture)),
            ("pupilId", student.PupilId.ToString(CultureInfo.InvariantCulture)),
            ("periodId", student.PeriodId.ToString(CultureInfo.InvariantCulture)),
            ("dateFrom", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("dateTo", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("lastId", LastId),
            ("pageSize", PageSize.ToString(CultureInfo.InvariantCulture)),
            ("lastSyncDate", LastSyncDate));

        var items = await api.GetAsync<List<T>>(url, student.DeviceKey, cancellationToken);

        if (items.Count >= PageSize)
            logger.LogWarning("Vulcan returned {Count} {Resource} items (page size limit) for pupil {PupilId}; some may be missing",
                items.Count, resource, student.PupilId);

        return items;
    }

    private static string BuildQuery(params (string Key, string Value)[] parameters) =>
        string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

    private static bool InRange(DateOnly date, DateOnly from, DateOnly to) => date >= from && date <= to;
}
