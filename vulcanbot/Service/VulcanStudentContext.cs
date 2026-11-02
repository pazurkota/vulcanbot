using vulcanbot.Dto;
using vulcanbot.Dto.Hebe;

namespace vulcanbot.Service;

/// <summary>
/// Everything needed to request data for one student: device keys, the account's REST URL
/// and the student's unit, pupil and period ids.
/// </summary>
public record VulcanStudentContext(
    DeviceKey DeviceKey,
    string RestUrl,
    string UnitSymbol,
    int UnitId,
    int PupilId,
    int PeriodId
    )
{
    /// <summary>Base URL for data requests, e.g. <c>https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/123456/</c>.</summary>
    public string DataUrl => $"{RestUrl.TrimEnd('/')}/{UnitSymbol}/";

    /// <param name="today">Used to pick the period when Vulcan doesn't mark one as current.</param>
    public static VulcanStudentContext Create(DeviceKey deviceKey, string restUrl, HebeAccount student, DateOnly today)
    {
        var period = SelectPeriod(student.Periods, today)
            ?? throw new InvalidOperationException($"Student {student.Pupil.Id} has no periods.");

        return new VulcanStudentContext(deviceKey, restUrl, student.Unit.Symbol, student.Unit.Id, student.Pupil.Id, period.Id);
    }

    internal static HebePeriod? SelectPeriod(IReadOnlyList<HebePeriod> periods, DateOnly today) =>
        periods.FirstOrDefault(p => p.Current)
        ?? periods.FirstOrDefault(p => p.Start.ToDateOnly() <= today && today <= p.End.ToDateOnly())
        ?? periods.MaxBy(p => p.End.Timestamp);
}
