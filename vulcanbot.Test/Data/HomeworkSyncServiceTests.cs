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

public class HomeworkSyncServiceTests
{
    private const string RestUrl = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/000088/api";
    private static readonly DateOnly From = new(2026, 10, 22);
    private static readonly DateOnly To = new(2027, 1, 5);
    private const string CreatedAtLocal = "2026-11-01 09:00:00";
    private static readonly DateTime CreatedAt = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc); // 09:00 CET

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
    public async Task SyncAsync_RequestsHomeworkForPupilAndDateRange()
    {
        RespondWith();

        await SyncAsync();

        var request = _handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.Uri!.OriginalString, Is.EqualTo(
                $"{RestUrl}/mobile/homework/byPupil?unitId=1&pupilId=7&dateFrom=2026-10-22&dateTo=2027-01-05&pageSize=500"));
            Assert.That(request.Headers["vHint"], Is.EqualTo("7"));
            Assert.That(request.Headers["Signature"], Does.StartWith($"keyId=\"{_account.Fingerprint}\""));
        });
    }

    [TestCase("https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/000088")]
    [TestCase("https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/000088/api/")]
    public async Task SyncAsync_NormalizesRestUrlToApiBase(string restUrl)
    {
        _account.RestUrl = restUrl;
        RespondWith();

        await SyncAsync();

        Assert.That(_handler.Requests.Single().Uri!.AbsolutePath,
            Is.EqualTo("/powiatwulkanowy/000088/api/mobile/homework/byPupil"));
    }

    [Test]
    public async Task SyncAsync_SavesNewHomeworkWithMappedFields()
    {
        // Midnight in Poland is 23:00 UTC the day before — the stored date must stay the Polish one.
        RespondWith(Homework(201, "Zad. 1-5 str. 42", new DateOnly(2026, 11, 12), answerAt: "2026-11-11 00:00:00"));

        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        var homework = await context.Homeworks.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new HomeworkSyncResult(Fetched: 1, Added: 1, Updated: 0)));
            Assert.That(homework.AccountId, Is.EqualTo(_account.Id));
            Assert.That(homework.VulcanId, Is.EqualTo(201));
            Assert.That(homework.Content, Is.EqualTo("Zad. 1-5 str. 42"));
            Assert.That(homework.SubjectName, Is.EqualTo("Fizyka"));
            Assert.That(homework.TeacherName, Is.EqualTo("Anna Nowak"));
            Assert.That(homework.IsAnswerRequired, Is.True);
            Assert.That(homework.Deadline, Is.EqualTo(new DateOnly(2026, 11, 12)));
            Assert.That(homework.AnswerDeadline, Is.EqualTo(new DateOnly(2026, 11, 11)));
            Assert.That(homework.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(homework.CalendarEventId, Is.Null);
        });
    }

    [Test]
    public async Task SyncAsync_ChangedHomework_UpdatesAndMarksForCalendarResync()
    {
        RespondWith(Homework(201, "Zad. 1-5", new DateOnly(2026, 11, 12)));
        await SyncAsync();
        await MarkCalendarSyncedAsync(201);

        RespondWith(Homework(201, "Zad. 1-10", new DateOnly(2026, 11, 13)));
        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        var homework = await context.Homeworks.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new HomeworkSyncResult(Fetched: 1, Added: 0, Updated: 1)));
            Assert.That(homework.Content, Is.EqualTo("Zad. 1-10"));
            Assert.That(homework.Deadline, Is.EqualTo(new DateOnly(2026, 11, 13)));
            Assert.That(homework.CalendarEventId, Is.EqualTo("event-1"), "Event id is kept so the event gets updated.");
            Assert.That(homework.CalendarSyncedAt, Is.Null);
        });
    }

    [Test]
    public async Task SyncAsync_UnchangedHomework_KeepsCalendarSyncState()
    {
        RespondWith(Homework(201, "Zad. 1-5", new DateOnly(2026, 11, 12)));
        await SyncAsync();
        await MarkCalendarSyncedAsync(201);

        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        var homework = await context.Homeworks.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new HomeworkSyncResult(Fetched: 1, Added: 0, Updated: 0)));
            Assert.That(homework.CalendarSyncedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task SyncAsync_SameVulcanIdInOtherAccount_IsNotTouched()
    {
        var other = await SaveAccountAsync(pupilId: 8);
        RespondWith(Homework(201, "Other pupil", new DateOnly(2026, 11, 12)));
        await SyncAsync(other);

        RespondWith(Homework(201, "This pupil", new DateOnly(2026, 11, 12)));
        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        var contents = await context.Homeworks
            .OrderBy(h => h.Content)
            .Select(h => new { h.AccountId, h.Content })
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
    public async Task SyncAsync_DuplicateIdsInResponse_AreSavedOnce()
    {
        RespondWith(Homework(201, "a", new DateOnly(2026, 11, 12)), Homework(201, "a", new DateOnly(2026, 11, 12)));

        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        Assert.Multiple(async () =>
        {
            Assert.That(result.Fetched, Is.EqualTo(1));
            Assert.That(await context.Homeworks.CountAsync(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SyncAsync_HebeError_ThrowsAndSavesNothing()
    {
        _handler.Respond("homework/byPupil", """{"Status":{"Code":201,"Message":"Brak uprawnień"},"Envelope":null}""");

        Assert.ThrowsAsync<HebeException>(() => SyncAsync());

        await using var context = PostgresFixture.CreateContext();
        Assert.That(await context.Homeworks.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task SyncAsync_MissingRequiredField_ThrowsHebeExceptionNamingIt()
    {
        var homework = Homework(201, "a", new DateOnly(2026, 11, 12));
        homework.Remove("DeadlineAt");
        RespondWith(homework);

        var ex = Assert.ThrowsAsync<HebeException>(() => SyncAsync());

        Assert.That(ex!.Message, Does.Contain("DeadlineAt"));
        await using var context = PostgresFixture.CreateContext();
        Assert.That(await context.Homeworks.CountAsync(), Is.Zero);
    }

    [Test]
    public void SyncAsync_HttpError_ThrowsHebeException()
    {
        _handler.Respond("homework/byPupil", "Unauthorized", HttpStatusCode.Unauthorized);

        Assert.ThrowsAsync<HebeException>(() => SyncAsync());
    }

    private async Task<HomeworkSyncResult> SyncAsync(VulcanAccount? account = null)
    {
        await using var context = PostgresFixture.CreateContext();
        var service = new HomeworkSyncService(new HebeClient(_client), context);
        return await service.SyncAsync(account ?? _account, From, To, CancellationToken.None);
    }

    private void RespondWith(params object[] homework) =>
        _handler.Respond("homework/byPupil", JsonSerializer.Serialize(new
        {
            Status = new { Code = 0, Message = "OK" },
            Envelope = homework
        }));

    private static async Task MarkCalendarSyncedAsync(int vulcanId)
    {
        await using var context = PostgresFixture.CreateContext();
        await context.Homeworks.Where(h => h.VulcanId == vulcanId).ExecuteUpdateAsync(s => s
            .SetProperty(h => h.CalendarEventId, "event-1")
            .SetProperty(h => h.CalendarSyncedAt, DateTime.UtcNow));
    }

    /// <summary>
    /// Homework in the Hebe CE shape; dates are Polish local time without an offset.
    /// </summary>
    private static Dictionary<string, object?> Homework(
        int id,
        string content,
        DateOnly deadline,
        string? answerAt = null) => new()
    {
        ["Id"] = id,
        ["Key"] = $"key-{id}",
        ["IdPupil"] = 7,
        ["IdHomework"] = id * 10,
        ["Content"] = content,
        ["IsAnswerRequired"] = answerAt is not null,
        ["CreatedAt"] = CreatedAtLocal,
        ["ModifiedAt"] = CreatedAtLocal,
        ["DateAt"] = "2026-11-01",
        ["AnswerAt"] = answerAt,
        ["DeadlineAt"] = deadline.ToString("yyyy-MM-dd"),
        ["Creator"] = new { Id = 3, Name = "Anna", Surname = "Nowak", DisplayName = "Anna Nowak" },
        ["Subject"] = new { Id = 4, Key = "fiz", Name = "Fizyka", Kod = "fiz", Position = 1 },
        ["Attachments"] = Array.Empty<object>(),
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
