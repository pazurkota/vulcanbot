using vulcanbot.Dto.Hebe;
using vulcanbot.Model;

namespace vulcanbot.Dto;

public record RegistrationResult(
    DeviceKey DeviceKeys,
    IReadOnlyList<HebeAccount> Accounts
    )
{
    /// <summary>
    /// One <see cref="VulcanAccount"/> per student, all sharing the registered device keys.
    /// </summary>
    public IEnumerable<VulcanAccount> ToAccounts() => Accounts.Select(a => new VulcanAccount
    {
        PrivatePemKey = DeviceKeys.PrivatePemKey,
        CertificatePem = DeviceKeys.CertificatePem,
        Fingerprint = DeviceKeys.Fingerprint,
        Symbol = a.TopLevelPartition,
        RestUrl = a.Unit.RestUrl,
        UnitId = a.Unit.Id,
        UnitName = a.Unit.Name,
        PupilId = a.Pupil.Id,
        LoginId = a.Pupil.LoginId,
        FirstName = a.Pupil.FirstName,
        Surname = a.Pupil.Surname,
        ClassDisplay = a.ClassDisplay
    });
}
