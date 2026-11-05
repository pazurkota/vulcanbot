using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using vulcanbot.Model;
using vulcanbot.Service;
using vulcanbot.Service.Hebe;
using vulcanbot.Test.Service;

namespace vulcanbot.Test.Data;

public class ExamSyncServiceTests
{
    private const string RestUrl = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/000088/api";
    private static readonly DateOnly From = new(2026, 10, 22);
    private static readonly DateOnly To = new(2027, 1, 5);

    private FakeHttpMessageHandler _handler = null!;
    private HttpClient _client = null!;
    private VulcanAccount _account = null!;

    [SetUp]
    public async Task Setup()
    {
        await using (var context = PostgresFixture.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """TRUNCATE "Exams", "Homeworks", "Lessons", "Accounts" CASCADE""");
        }

        _handler = new FakeHttpMessageHandler();
        _client = new HttpClient(_handler);
        _account = await SaveAccountAsync(pupilId: 7);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Test]
    public async Task SyncAsync_RequestsExamsForPupilAndDateRange()
    {
        RespondWith();

        await SyncAsync();

        var request = _handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.Uri!.OriginalString, Is.EqualTo(
                $"{RestUrl}/mobile/exam/byPupil?unitId=1&pupilId=7&dateFrom=2026-10-22&dateTo=2027-01-05&pageSize=500"));
            Assert.That(request.Headers["vHint"], Is.EqualTo("7"));
            Assert.That(request.Headers["Signature"], Does.StartWith($"keyId=\"{_account.Fingerprint}\""));
        });
    }

    [Test]
    public async Task SyncAsync_SavesNewExamWithMappedFields()
    {
        RespondWith(Exam(101, "Funkcje kwadratowe", deadlineAt: "2026-11-10 08:00:00"));

        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        var exam = await context.Exams.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SyncResult(Fetched: 1, Added: 1, Updated: 0)));
            Assert.That(exam.AccountId, Is.EqualTo(_account.Id));
            Assert.That(exam.VulcanId, Is.EqualTo(101));
            Assert.That(exam.Type, Is.EqualTo("Sprawdzian"));
            Assert.That(exam.Content, Is.EqualTo("Funkcje kwadratowe"));
            Assert.That(exam.SubjectName, Is.EqualTo("Matematyka"));
            Assert.That(exam.TeacherName, Is.EqualTo("Jan Kowalski"));
            Assert.That(exam.Deadline, Is.EqualTo(new DateOnly(2026, 11, 10)));
            Assert.That(exam.CreatedAt, Is.EqualTo(new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc)));
            Assert.That(exam.ModifiedAt, Is.EqualTo(new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc)));
            Assert.That(exam.CalendarEventId, Is.Null);
        });
    }

    [Test]
    public async Task SyncAsync_DeadlineAtPolishMidnight_KeepsPolishDate()
    {
        // 00:00 in Poland is 23:00 UTC the day before.
        RespondWith(Exam(101, "x", deadlineAt: "2026-11-10 00:00:00"));

        await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        Assert.That((await context.Exams.SingleAsync()).Deadline, Is.EqualTo(new DateOnly(2026, 11, 10)));
    }

    [Test]
    public async Task SyncAsync_ChangedExam_UpdatesAndMarksForCalendarResync()
    {
        RespondWith(Exam(101, "Funkcje", deadlineAt: "2026-11-10 08:00:00"));
        await SyncAsync();
        await MarkCalendarSyncedAsync(101);

        RespondWith(Exam(101, "Funkcje kwadratowe", deadlineAt: "2026-11-12 08:00:00", type: "Kartkówka"));
        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        var exam = await context.Exams.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SyncResult(Fetched: 1, Added: 0, Updated: 1)));
            Assert.That(exam.Type, Is.EqualTo("Kartkówka"));
            Assert.That(exam.Content, Is.EqualTo("Funkcje kwadratowe"));
            Assert.That(exam.Deadline, Is.EqualTo(new DateOnly(2026, 11, 12)));
            Assert.That(exam.CalendarEventId, Is.EqualTo("event-1"), "Event id is kept so the event gets updated.");
            Assert.That(exam.CalendarSyncedAt, Is.Null);
        });
    }

    [Test]
    public async Task SyncAsync_UnchangedExam_KeepsCalendarSyncState()
    {
        RespondWith(Exam(101, "Funkcje", deadlineAt: "2026-11-10 08:00:00"));
        await SyncAsync();
        await MarkCalendarSyncedAsync(101);

        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(new SyncResult(Fetched: 1, Added: 0, Updated: 0)));
            Assert.That((await context.Exams.SingleAsync()).CalendarSyncedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task SyncAsync_SameVulcanIdInOtherAccount_IsNotTouched()
    {
        var other = await SaveAccountAsync(pupilId: 8);
        RespondWith(Exam(101, "Other pupil", deadlineAt: "2026-11-10 08:00:00"));
        await SyncAsync(other);

        RespondWith(Exam(101, "This pupil", deadlineAt: "2026-11-10 08:00:00"));
        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        var contents = await context.Exams
            .OrderBy(e => e.Content)
            .Select(e => new { e.AccountId, e.Content })
            .ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result.Added, Is.EqualTo(1));
            Assert.That(contents, Is.EqualTo(new[]
            {
                new { AccountId = other.Id, Content = "Other pupil" },
                new { AccountId = _account.Id, Content = "This pupil" }
            }));
        });
    }

    [Test]
    public async Task SyncAsync_MissingRequiredField_ThrowsHebeExceptionNamingIt()
    {
        var exam = Exam(101, "x", deadlineAt: "2026-11-10 08:00:00");
        exam.Remove("DeadlineAt");
        RespondWith(exam);

        var ex = Assert.ThrowsAsync<HebeException>(() => SyncAsync());

        Assert.That(ex!.Message, Does.Contain("DeadlineAt"));
        await using var context = PostgresFixture.CreateContext();
        Assert.That(await context.Exams.CountAsync(), Is.Zero);
    }

    [Test]
    public void SyncAsync_HttpError_ThrowsHebeException()
    {
        _handler.Respond("exam/byPupil", "Unauthorized", HttpStatusCode.Unauthorized);

        Assert.ThrowsAsync<HebeException>(() => SyncAsync());
    }

    private async Task<SyncResult> SyncAsync(VulcanAccount? account = null)
    {
        await using var context = PostgresFixture.CreateContext();
        var service = new ExamSyncService(new HebeClient(_client), context);
        return await service.SyncAsync(account ?? _account, From, To, CancellationToken.None);
    }

    private void RespondWith(params object[] exams) =>
        _handler.Respond("exam/byPupil", JsonSerializer.Serialize(new
        {
            Status = new { Code = 0, Message = "OK" },
            Envelope = exams
        }));

    private static async Task MarkCalendarSyncedAsync(int vulcanId)
    {
        await using var context = PostgresFixture.CreateContext();
        await context.Exams.Where(e => e.VulcanId == vulcanId).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.CalendarEventId, "event-1")
            .SetProperty(e => e.CalendarSyncedAt, DateTime.UtcNow));
    }

    /// <summary>
    /// Exam in the Hebe CE shape; dates are Polish local time without an offset.
    /// </summary>
    private static Dictionary<string, object?> Exam(
        int id,
        string content,
        string deadlineAt,
        string type = "Sprawdzian") => new()
    {
        ["Id"] = id,
        ["Key"] = $"key-{id}",
        ["Type"] = type,
        ["TypeId"] = 1,
        ["Content"] = content,
        ["CreatedAt"] = "2026-11-01 09:00:00",
        ["ModifiedAt"] = "2026-11-02 09:00:00",
        ["DeadlineAt"] = deadlineAt,
        ["Creator"] = new { Id = 3, Name = "Jan", Surname = "Kowalski", DisplayName = "Jan Kowalski" },
        ["Subject"] = new { Id = 4, Key = "mat", Name = "Matematyka", Kod = "mat", Position = 1 },
        ["PupilId"] = 7,
        ["Didactics"] = null
    };

    private static async Task<VulcanAccount> SaveAccountAsync(int pupilId)
    {
        using var rsa = RSA.Create(2048);
        using var cert = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));

        var account = new VulcanAccount
        {
            PrivatePemKey = rsa.ExportPkcs8PrivateKeyPem(),
            CertificatePem = cert.ExportCertificatePem(),
            Fingerprint = cert.Thumbprint.ToLowerInvariant(),
            Symbol = "powiatwulkanowy",
            RestUrl = RestUrl,
            UnitId = 1,
            UnitName = "Szkoła Podstawowa nr 1",
            PupilId = pupilId,
            LoginId = pupilId * 10,
            FirstName = "Jan",
            Surname = "Uczeń",
            ClassDisplay = "8A"
        };

        await using var context = PostgresFixture.CreateContext();
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account;
    }
}
