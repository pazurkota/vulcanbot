using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using vulcanbot.Dto;

namespace vulcanbot.Model;

/// <summary>
/// Registered Vulcan device + the student it gives access to.
/// </summary>
[Index(nameof(Symbol), nameof(PupilId), IsUnique = true)]
public class VulcanAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Device keys (from RegistrationResult)
    [Required] public string PrivatePemKey { get; set; } = null!;
    [Required] public string CertificatePem { get; set; } = null!;
    [MaxLength(64)] public string Fingerprint { get; set; } = null!;

    // School / unit
    [MaxLength(64)] public string Symbol { get; set; } = null!;
    [MaxLength(256)] public string RestUrl { get; set; } = null!;
    public int UnitId { get; set; }
    [MaxLength(256)] public string UnitName { get; set; } = null!;

    // Student
    public int PupilId { get; set; }
    public int LoginId { get; set; }
    [MaxLength(128)] public string FirstName { get; set; } = null!;
    [MaxLength(128)] public string Surname { get; set; } = null!;
    [MaxLength(32)] public string ClassDisplay { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncedAt { get; set; }

    public List<Exam> Exams { get; set; } = [];
    public List<Homework> Homeworks { get; set; } = [];
    public List<Lesson> Lessons { get; set; } = [];

    public DeviceKey ToDeviceKey() => new(PrivatePemKey, CertificatePem, Fingerprint);

    /// <summary>
    /// Builds a Hebe data URL, e.g. <c>mobile/homework/byPupil</c>, from the unit's RestURL.
    /// </summary>
    public string HebeUrl(string endpoint)
    {
        var restUrl = RestUrl.TrimEnd('/');
        if (!restUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) restUrl += "/api";

        return $"{restUrl}/{endpoint}";
    }

    /// <summary>
    /// URL of a <c>.../byPupil</c> endpoint filtered by date range, e.g. <c>mobile/exam/byPupil</c>.
    /// </summary>
    public string HebeByPupilUrl(string endpoint, DateOnly from, DateOnly to, int pageSize = 500) =>
        HebeUrl(endpoint) +
        $"?unitId={UnitId}" +
        $"&pupilId={PupilId}" +
        $"&dateFrom={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
        $"&dateTo={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" +
        $"&pageSize={pageSize}";
}
