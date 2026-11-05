using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using vulcanbot.Model;
using vulcanbot.Service;
using vulcanbot.Service.Hebe;
using vulcanbot.Test.Service;

namespace vulcanbot.Test.Data;

public class LessonSyncServiceTests
{
    private const string RestUrl = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/000088/api";
    private static readonly DateOnly From = new(2026, 10, 29);
    private static readonly DateOnly To = new(2026, 11, 26);
    private static readonly DateOnly Monday = new(2026, 11, 9);

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
        _account = await SaveAccountAsync();
        RespondWithChanges();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Test]
    public async Task SyncAsync_RequestsTimetableAndChangesForPupilAndDateRange()
    {
        RespondWithLessons();

        await SyncAsync();

        const string query = "?unitId=1&pupilId=7&dateFrom=2026-10-29&dateTo=2026-11-26&pageSize=500";
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Requests.Select(r => r.Uri!.OriginalString), Is.EqualTo(new[]
            {
                $"{RestUrl}/mobile/schedule/withchanges/byPupil{query}",
                $"{RestUrl}/mobile/schedule/changes/byPupil{query}"
            }));
            Assert.That(_handler.Requests.Select(r => r.Headers["vHint"]), Is.All.EqualTo("7"));
        });
    }

    [Test]
    public async Task SyncAsync_RegularLesson_IsSavedWithoutChange()
    {
        RespondWithLessons(Lesson(301, Monday));

        var result = await SyncAsync();

        var lesson = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SyncResult(Fetched: 1, Added: 1, Updated: 0)));
            Assert.That(lesson.VulcanId, Is.EqualTo(301));
            Assert.That(lesson.Date, Is.EqualTo(Monday));
            Assert.That(lesson.Start, Is.EqualTo(new TimeOnly(8, 0)));
            Assert.That(lesson.End, Is.EqualTo(new TimeOnly(8, 45)));
            Assert.That(lesson.Position, Is.EqualTo(1));
            Assert.That(lesson.SubjectName, Is.EqualTo("Chemia"));
            Assert.That(lesson.TeacherName, Is.EqualTo("Anna Nowak"));
            Assert.That(lesson.Room, Is.EqualTo("12"));
            Assert.That(lesson.ChangeType, Is.EqualTo(LessonChangeType.None));
            Assert.That(lesson.ChangeReason, Is.Null);
        });
    }

    [Test]
    public async Task SyncAsync_TimesWithSeconds_AreParsed()
    {
        var lesson = Lesson(301, Monday);
        lesson["TimeSlot"] = TimeSlot("10:50:00", "11:35:00", position: 4);
        RespondWithLessons(lesson);

        await SyncAsync();

        var saved = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Start, Is.EqualTo(new TimeOnly(10, 50)));
            Assert.That(saved.End, Is.EqualTo(new TimeOnly(11, 35)));
            Assert.That(saved.Position, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task SyncAsync_CancellationFromChangesEndpoint_MarksCancelledAndKeepsOriginalLesson()
    {
        RespondWithLessons(Lesson(301, Monday));
        RespondWithChanges(Change(301, Monday, type: 1, reason: "Wycieczka klasowa",
            teacher: Teacher("Piotr", "Zieliński")));

        await SyncAsync();

        var lesson = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(lesson.ChangeType, Is.EqualTo(LessonChangeType.Cancelled));
            Assert.That(lesson.ChangeReason, Is.EqualTo("Wycieczka klasowa"));
            Assert.That(lesson.SubjectName, Is.EqualTo("Chemia"));
            Assert.That(lesson.TeacherName, Is.EqualTo("Anna Nowak"));
        });
    }

    [Test]
    public async Task SyncAsync_ClassAbsence_MarksCancelled()
    {
        RespondWithLessons(Lesson(301, Monday));
        var change = Change(301, Monday, type: 2);
        change["ClassAbsence"] = true;
        RespondWithChanges(change);

        await SyncAsync();

        Assert.That((await SingleLessonAsync()).ChangeType, Is.EqualTo(LessonChangeType.Cancelled));
    }

    [Test]
    public async Task SyncAsync_Substitution_ReplacesTeacherSubjectRoomAndTime()
    {
        RespondWithLessons(Lesson(301, Monday));
        RespondWithChanges(Change(301, Monday, type: 2, reason: "Nieobecność nauczyciela",
            teacher: Teacher("Piotr", "Zieliński"),
            subject: Subject("Fizyka"),
            room: new { Id = 2, Code = "Aula" },
            timeSlot: TimeSlot("08:55", "09:40", position: 2)));

        await SyncAsync();

        var lesson = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(lesson.ChangeType, Is.EqualTo(LessonChangeType.Changed));
            Assert.That(lesson.TeacherName, Is.EqualTo("Piotr Zieliński"));
            Assert.That(lesson.SubjectName, Is.EqualTo("Fizyka"));
            Assert.That(lesson.Room, Is.EqualTo("Aula"));
            Assert.That(lesson.Start, Is.EqualTo(new TimeOnly(8, 55)));
            Assert.That(lesson.Position, Is.EqualTo(2));
            Assert.That(lesson.ChangeReason, Is.EqualTo("Nieobecność nauczyciela"));
        });
    }

    [Test]
    public async Task SyncAsync_SubstitutionWithOnlyNewTeacher_KeepsOtherDetails()
    {
        RespondWithLessons(Lesson(301, Monday));
        RespondWithChanges(Change(301, Monday, type: 2, teacher: Teacher("Piotr", "Zieliński")));

        await SyncAsync();

        var lesson = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(lesson.TeacherName, Is.EqualTo("Piotr Zieliński"));
            Assert.That(lesson.SubjectName, Is.EqualTo("Chemia"));
            Assert.That(lesson.Room, Is.EqualTo("12"));
            Assert.That(lesson.Start, Is.EqualTo(new TimeOnly(8, 0)));
        });
    }

    [Test]
    public async Task SyncAsync_SubstitutionWithNoRoom_ClearsRoom()
    {
        RespondWithLessons(Lesson(301, Monday));
        var change = Change(301, Monday, type: 2);
        change["NoRoom"] = true;
        RespondWithChanges(change);

        await SyncAsync();

        Assert.That((await SingleLessonAsync()).Room, Is.Null);
    }

    [Test]
    public async Task SyncAsync_MovedLesson_MarksMovedAwayWithTargetDate()
    {
        RespondWithLessons(Lesson(301, Monday));
        var change = Change(301, Monday, type: 3, timeSlot: TimeSlot("12:00", "12:45", position: 6));
        change["ChangeDateAt"] = "2026-11-12";
        RespondWithChanges(change);

        await SyncAsync();

        var lesson = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(lesson.ChangeType, Is.EqualTo(LessonChangeType.MovedAway));
            Assert.That(lesson.MovedToDate, Is.EqualTo(new DateOnly(2026, 11, 12)));
            Assert.That(lesson.Start, Is.EqualTo(new TimeOnly(8, 0)), "Keeps the original slot.");
        });
    }

    [Test]
    public async Task SyncAsync_SubstitutionEmbeddedInLesson_IsUsedWhenChangesEndpointHasNone()
    {
        var lesson = Lesson(301, Monday);
        lesson["Substitution"] = Change(301, Monday, type: 1, reason: "Rekolekcje");
        RespondWithLessons(lesson);

        await SyncAsync();

        var saved = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(saved.ChangeType, Is.EqualTo(LessonChangeType.Cancelled));
            Assert.That(saved.ChangeReason, Is.EqualTo("Rekolekcje"));
        });
    }

    [Test]
    public async Task SyncAsync_ChangeForSameScheduleOnAnotherDay_IsNotApplied()
    {
        RespondWithLessons(Lesson(301, Monday));
        RespondWithChanges(Change(301, Monday.AddDays(7), type: 1));

        await SyncAsync();

        Assert.That((await SingleLessonAsync()).ChangeType, Is.EqualTo(LessonChangeType.None));
    }

    [Test]
    public async Task SyncAsync_SameIdOnDifferentDays_SavesBothLessons()
    {
        RespondWithLessons(Lesson(301, Monday), Lesson(301, Monday.AddDays(7)));
        RespondWithChanges(Change(301, Monday.AddDays(7), type: 1));

        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        var lessons = await context.Lessons.OrderBy(l => l.Date).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result.Added, Is.EqualTo(2));
            Assert.That(lessons.Select(l => (l.Date, l.ChangeType)), Is.EqualTo(new[]
            {
                (Monday, LessonChangeType.None),
                (Monday.AddDays(7), LessonChangeType.Cancelled)
            }));
        });
    }

    [Test]
    public async Task SyncAsync_LessonCancelledLater_UpdatesAndMarksForCalendarResync()
    {
        RespondWithLessons(Lesson(301, Monday));
        await SyncAsync();
        await MarkCalendarSyncedAsync();

        RespondWithChanges(Change(301, Monday, type: 1, reason: "Choroba nauczyciela"));
        var result = await SyncAsync();

        var lesson = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SyncResult(Fetched: 1, Added: 0, Updated: 1)));
            Assert.That(lesson.ChangeType, Is.EqualTo(LessonChangeType.Cancelled));
            Assert.That(lesson.CalendarEventId, Is.EqualTo("event-1"), "Event id is kept so the event gets updated.");
            Assert.That(lesson.CalendarSyncedAt, Is.Null);
        });
    }

    [Test]
    public async Task SyncAsync_ChangeRemovedLater_RevertsToRegularLesson()
    {
        RespondWithLessons(Lesson(301, Monday));
        RespondWithChanges(Change(301, Monday, type: 2, teacher: Teacher("Piotr", "Zieliński"), reason: "x"));
        await SyncAsync();

        RespondWithChanges();
        await SyncAsync();

        var lesson = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(lesson.ChangeType, Is.EqualTo(LessonChangeType.None));
            Assert.That(lesson.TeacherName, Is.EqualTo("Anna Nowak"));
            Assert.That(lesson.ChangeReason, Is.Null);
        });
    }

    [Test]
    public async Task SyncAsync_UnchangedLesson_KeepsCalendarSyncState()
    {
        RespondWithLessons(Lesson(301, Monday));
        await SyncAsync();
        await MarkCalendarSyncedAsync();

        var result = await SyncAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(new SyncResult(Fetched: 1, Added: 0, Updated: 0)));
            Assert.That((await SingleLessonAsync()).CalendarSyncedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task SyncAsync_LessonWithoutTimeSlot_ThrowsHebeExceptionAndSavesNothing()
    {
        var lesson = Lesson(301, Monday);
        lesson.Remove("TimeSlot");
        RespondWithLessons(lesson);

        var ex = Assert.ThrowsAsync<HebeException>(() => SyncAsync());

        Assert.That(ex!.Message, Does.Contain("TimeSlot"));
        await using var context = PostgresFixture.CreateContext();
        Assert.That(await context.Lessons.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task SyncAsync_LessonMissingFromTimetable_IsDeleted()
    {
        RespondWithLessons(Lesson(301, Monday), Lesson(302, Monday));
        await SyncAsync();

        RespondWithLessons(Lesson(301, Monday));
        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(new SyncResult(Fetched: 1, Added: 0, Updated: 0, Removed: 1)));
            Assert.That(await context.Lessons.Select(l => l.VulcanId).ToListAsync(), Is.EqualTo(new[] { 301 }));
        });
    }

    [Test]
    public async Task SyncAsync_EmptyTimetable_DeletesAllLessonsInWindow()
    {
        RespondWithLessons(Lesson(301, Monday), Lesson(302, Monday.AddDays(1)));
        await SyncAsync();

        RespondWithLessons();
        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        Assert.Multiple(async () =>
        {
            Assert.That(result.Removed, Is.EqualTo(2));
            Assert.That(await context.Lessons.CountAsync(), Is.Zero);
        });
    }

    [Test]
    public async Task SyncAsync_LessonsOutsideWindow_AreKept()
    {
        await AddLessonAsync(_account, 301, From.AddDays(-1));
        await AddLessonAsync(_account, 302, To.AddDays(1));
        RespondWithLessons();

        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        Assert.Multiple(async () =>
        {
            Assert.That(result.Removed, Is.Zero);
            Assert.That(await context.Lessons.CountAsync(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task SyncAsync_WindowBoundaries_AreInclusive()
    {
        await AddLessonAsync(_account, 301, From);
        await AddLessonAsync(_account, 302, To);
        RespondWithLessons();

        var result = await SyncAsync();

        Assert.That(result.Removed, Is.EqualTo(2));
    }

    [Test]
    public async Task SyncAsync_OtherAccountsLessons_AreKept()
    {
        var other = await SaveAccountAsync(pupilId: 8);
        await AddLessonAsync(other, 301, Monday);
        RespondWithLessons();

        await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        Assert.That(await context.Lessons.CountAsync(l => l.AccountId == other.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task SyncAsync_MissingLessonWithCalendarEvent_IsMarkedRemovedInsteadOfDeleted()
    {
        RespondWithLessons(Lesson(301, Monday));
        await SyncAsync();
        await MarkCalendarSyncedAsync();

        RespondWithLessons();
        var result = await SyncAsync();

        var lesson = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result.Removed, Is.EqualTo(1));
            Assert.That(lesson.RemovedAt, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
            Assert.That(lesson.CalendarEventId, Is.EqualTo("event-1"));
            Assert.That(lesson.CalendarSyncedAt, Is.Null, "Calendar sync has to delete the event.");
        });
    }

    [Test]
    public async Task SyncAsync_AlreadyRemovedLesson_IsNotCountedAgain()
    {
        RespondWithLessons(Lesson(301, Monday));
        await SyncAsync();
        await MarkCalendarSyncedAsync();
        RespondWithLessons();
        await SyncAsync();

        var result = await SyncAsync();

        Assert.That(result.Removed, Is.Zero);
    }

    [Test]
    public async Task SyncAsync_RemovedLessonBackInTimetable_IsRestored()
    {
        RespondWithLessons(Lesson(301, Monday));
        await SyncAsync();
        await MarkCalendarSyncedAsync();
        RespondWithLessons();
        await SyncAsync();

        RespondWithLessons(Lesson(301, Monday));
        var result = await SyncAsync();

        var lesson = await SingleLessonAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SyncResult(Fetched: 1, Added: 0, Updated: 1)));
            Assert.That(lesson.RemovedAt, Is.Null);
            Assert.That(lesson.CalendarEventId, Is.EqualTo("event-1"));
        });
    }

    [Test]
    public async Task SyncAsync_FullPage_DoesNotRemoveAnything()
    {
        await AddLessonAsync(_account, 1, Monday.AddDays(1));
        RespondWithLessons(Enumerable.Range(1000, HebeClient.PageSize)
            .Select(id => (object)Lesson(id, Monday))
            .ToArray());

        var result = await SyncAsync();

        await using var context = PostgresFixture.CreateContext();
        Assert.Multiple(async () =>
        {
            Assert.That(result.Removed, Is.Zero);
            Assert.That(await context.Lessons.AnyAsync(l => l.VulcanId == 1), Is.True);
        });
    }

    [Test]
    public async Task SyncAsync_HebeError_DoesNotRemoveAnything()
    {
        RespondWithLessons(Lesson(301, Monday));
        await SyncAsync();

        _handler.Respond("schedule/changes/byPupil", """{"Status":{"Code":500,"Message":"Błąd"},"Envelope":null}""");
        RespondWithLessons();
        Assert.ThrowsAsync<HebeException>(() => SyncAsync());

        await using var context = PostgresFixture.CreateContext();
        Assert.That(await context.Lessons.CountAsync(), Is.EqualTo(1));
    }

    private async Task<SyncResult> SyncAsync()
    {
        await using var context = PostgresFixture.CreateContext();
        var service = new LessonSyncService(new HebeClient(_client), context);
        return await service.SyncAsync(_account, From, To, CancellationToken.None);
    }

    private static async Task AddLessonAsync(VulcanAccount account, int vulcanId, DateOnly date)
    {
        await using var context = PostgresFixture.CreateContext();
        context.Lessons.Add(new Lesson
        {
            AccountId = account.Id, VulcanId = vulcanId, Date = date,
            Start = new TimeOnly(8, 0), End = new TimeOnly(8, 45), Position = 1
        });
        await context.SaveChangesAsync();
    }

    private static async Task<Lesson> SingleLessonAsync()
    {
        await using var context = PostgresFixture.CreateContext();
        return await context.Lessons.SingleAsync();
    }

    private void RespondWithLessons(params object[] lessons) => Respond("schedule/withchanges/byPupil", lessons);

    private void RespondWithChanges(params object[] changes) => Respond("schedule/changes/byPupil", changes);

    private void Respond(string urlFragment, object[] envelope) =>
        _handler.Respond(urlFragment, JsonSerializer.Serialize(new
        {
            Status = new { Code = 0, Message = "OK" },
            Envelope = envelope
        }));

    private static async Task MarkCalendarSyncedAsync()
    {
        await using var context = PostgresFixture.CreateContext();
        await context.Lessons.ExecuteUpdateAsync(s => s
            .SetProperty(l => l.CalendarEventId, "event-1")
            .SetProperty(l => l.CalendarSyncedAt, DateTime.UtcNow));
    }

    /// <summary>
    /// Timetable entry in the Hebe CE shape.
    /// </summary>
    private static Dictionary<string, object?> Lesson(int id, DateOnly date) => new()
    {
        ["Id"] = id,
        ["MergeChangeId"] = null,
        ["Event"] = null,
        ["DateAt"] = date.ToString("yyyy-MM-dd"),
        ["Room"] = new { Id = 1, Code = "12" },
        ["TimeSlot"] = TimeSlot("08:00", "08:45", position: 1),
        ["Subject"] = Subject("Chemia"),
        ["TeacherPrimary"] = Teacher("Anna", "Nowak"),
        ["TeacherSecondary"] = null,
        ["TeacherSecondary2"] = null,
        ["Clazz"] = new { Id = 5, Key = "8a", DisplayName = "8A", Symbol = "8A" },
        ["Distribution"] = null,
        ["PupilAlias"] = null,
        ["Substitution"] = null,
        ["Parent"] = null
    };

    /// <summary>
    /// Entry from schedule/changes/byPupil; only the fields that change are filled.
    /// </summary>
    private static Dictionary<string, object?> Change(
        int scheduleId,
        DateOnly date,
        int type,
        string? reason = null,
        object? teacher = null,
        object? subject = null,
        object? room = null,
        object? timeSlot = null) => new()
    {
        ["Id"] = 900 + scheduleId,
        ["UnitId"] = 1,
        ["ScheduleId"] = scheduleId,
        ["DateAt"] = date.ToString("yyyy-MM-dd"),
        ["ChangeDateAt"] = null,
        ["PupilNote"] = null,
        ["Reason"] = reason,
        ["Event"] = null,
        ["Room"] = room,
        ["TimeSlot"] = timeSlot,
        ["Subject"] = subject,
        ["TeacherPrimary"] = teacher,
        ["TeacherSecondary"] = null,
        ["Change"] = new { Id = 1, Type = type, IsMerge = false, Separation = false },
        ["Clazz"] = null,
        ["Distribution"] = null,
        ["ClassAbsence"] = false,
        ["NoRoom"] = false,
        ["ModifiedAt"] = "2026-11-08 18:00:00",
        ["Description"] = null
    };

    private static object TimeSlot(string start, string end, int position) =>
        new { Id = position, Start = start, End = end, Display = $"{start}-{end}", Position = position };

    private static object Subject(string name) =>
        new { Id = name.Length, Key = name.ToLowerInvariant(), Name = name, Kod = name[..3], Position = 1 };

    private static object Teacher(string name, string surname) =>
        new { Id = name.Length, Name = name, Surname = surname, DisplayName = $"{name} {surname}" };

    private static async Task<VulcanAccount> SaveAccountAsync(int pupilId = 7)
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
