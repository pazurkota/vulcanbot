using Microsoft.EntityFrameworkCore;
using Npgsql;
using vulcanbot.Data;
using vulcanbot.Model;

namespace vulcanbot.Test.Data;

public class VulcanDbContextTests
{
    private const string UniqueViolation = "23505";

    [SetUp]
    public async Task ClearDatabase()
    {
        await using var context = PostgresFixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE "Exams", "Homeworks", "Lessons", "Accounts" CASCADE""");
    }

    [Test]
    public async Task Migrations_AreAllApplied()
    {
        await using var context = PostgresFixture.CreateContext();

        var pending = await context.Database.GetPendingMigrationsAsync();

        Assert.That(pending, Is.Empty);
    }

    [Test]
    public void Model_HasNoChangesMissingFromMigrations()
    {
        using var context = PostgresFixture.CreateContext();

        Assert.That(context.Database.HasPendingModelChanges(), Is.False,
            "Model differs from the latest migration — run 'dotnet ef migrations add <Name>'.");
    }

    [Test]
    public async Task Account_WithChildren_RoundTripsAllValues()
    {
        var createdAt = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);
        var account = NewAccount();
        account.Exams.Add(new Exam
        {
            VulcanId = 101, Type = "Sprawdzian", Content = "Funkcje kwadratowe",
            SubjectName = "Matematyka", TeacherName = "Jan Kowalski",
            Deadline = new DateOnly(2026, 11, 10), CreatedAt = createdAt, ModifiedAt = createdAt
        });
        account.Homeworks.Add(new Homework
        {
            VulcanId = 201, Content = "Zad. 1-5 str. 42", SubjectName = "Fizyka", TeacherName = "Anna Nowak",
            IsAnswerRequired = true, Deadline = new DateOnly(2026, 11, 5),
            AnswerDeadline = new DateOnly(2026, 11, 4), CreatedAt = createdAt
        });
        account.Lessons.Add(new Lesson
        {
            VulcanId = 301, Date = new DateOnly(2026, 11, 3), Start = new TimeOnly(8, 0), End = new TimeOnly(8, 45),
            Position = 1, SubjectName = "Chemia", Room = "12", ChangeType = LessonChangeType.Cancelled
        });

        await using (var context = PostgresFixture.CreateContext())
        {
            context.Accounts.Add(account);
            await context.SaveChangesAsync();
        }

        await using (var context = PostgresFixture.CreateContext())
        {
            var loaded = await context.Accounts
                .Include(a => a.Exams)
                .Include(a => a.Homeworks)
                .Include(a => a.Lessons)
                .SingleAsync(a => a.Id == account.Id);

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Symbol, Is.EqualTo(account.Symbol));
                Assert.That(loaded.PrivatePemKey, Is.EqualTo(account.PrivatePemKey));

                var exam = loaded.Exams.Single();
                Assert.That(exam.Deadline, Is.EqualTo(new DateOnly(2026, 11, 10)));
                Assert.That(exam.CreatedAt, Is.EqualTo(createdAt));
                Assert.That(exam.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(exam.CalendarEventId, Is.Null);

                var homework = loaded.Homeworks.Single();
                Assert.That(homework.IsAnswerRequired, Is.True);
                Assert.That(homework.AnswerDeadline, Is.EqualTo(new DateOnly(2026, 11, 4)));

                var lesson = loaded.Lessons.Single();
                Assert.That(lesson.Start, Is.EqualTo(new TimeOnly(8, 0)));
                Assert.That(lesson.End, Is.EqualTo(new TimeOnly(8, 45)));
                Assert.That(lesson.ChangeType, Is.EqualTo(LessonChangeType.Cancelled));
            });
        }
    }

    [Test]
    public async Task LessonChangeType_IsStoredAsText()
    {
        var account = NewAccount();
        account.Lessons.Add(NewLesson(1, LessonChangeType.MovedHere));
        await SaveAsync(account);

        await using var context = PostgresFixture.CreateContext();
        var stored = await context.Database
            .SqlQueryRaw<string>("""SELECT "ChangeType" AS "Value" FROM "Lessons" """)
            .SingleAsync();

        Assert.That(stored, Is.EqualTo("MovedHere"));
    }

    [Test]
    public async Task Account_DuplicateSymbolAndPupil_IsRejected()
    {
        await SaveAsync(NewAccount(pupilId: 7));

        var ex = Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(NewAccount(pupilId: 7)));

        AssertUniqueViolation(ex);
    }

    [Test]
    public async Task Account_SamePupilIdInDifferentSchool_IsAllowed()
    {
        await SaveAsync(NewAccount(symbol: "szkolaa", pupilId: 7));

        Assert.DoesNotThrowAsync(() => SaveAsync(NewAccount(symbol: "szkolab", pupilId: 7)));
    }

    [Test]
    public async Task Exam_DuplicateVulcanIdForSameAccount_IsRejected()
    {
        var account = NewAccount();
        account.Exams.Add(NewExam(101));
        await SaveAsync(account);

        await using var context = PostgresFixture.CreateContext();
        var duplicate = NewExam(101);
        duplicate.AccountId = account.Id;
        context.Exams.Add(duplicate);

        var ex = Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        AssertUniqueViolation(ex);
    }

    [Test]
    public async Task Exam_SameVulcanIdForDifferentAccounts_IsAllowed()
    {
        var first = NewAccount(pupilId: 1);
        first.Exams.Add(NewExam(101));
        await SaveAsync(first);

        var second = NewAccount(pupilId: 2);
        second.Exams.Add(NewExam(101));

        Assert.DoesNotThrowAsync(() => SaveAsync(second));
    }

    [Test]
    public async Task DeletingAccount_CascadesToExamsHomeworksAndLessons()
    {
        var account = NewAccount();
        account.Exams.Add(NewExam(101));
        account.Homeworks.Add(new Homework
        {
            VulcanId = 201, Content = "x", SubjectName = "x", TeacherName = "x",
            Deadline = new DateOnly(2026, 11, 5), CreatedAt = DateTime.UtcNow
        });
        account.Lessons.Add(NewLesson(301));
        await SaveAsync(account);

        await using (var context = PostgresFixture.CreateContext())
        {
            await context.Accounts.Where(a => a.Id == account.Id).ExecuteDeleteAsync();
        }

        await using (var context = PostgresFixture.CreateContext())
        {
            Assert.Multiple(async () =>
            {
                Assert.That(await context.Exams.CountAsync(), Is.Zero);
                Assert.That(await context.Homeworks.CountAsync(), Is.Zero);
                Assert.That(await context.Lessons.CountAsync(), Is.Zero);
            });
        }
    }

    [Test]
    public async Task Lessons_CanBeQueriedByDateRange()
    {
        var account = NewAccount();
        account.Lessons.Add(NewLesson(1, date: new DateOnly(2026, 11, 2)));
        account.Lessons.Add(NewLesson(2, date: new DateOnly(2026, 11, 4)));
        account.Lessons.Add(NewLesson(3, date: new DateOnly(2026, 11, 9)));
        await SaveAsync(account);

        await using var context = PostgresFixture.CreateContext();
        var from = new DateOnly(2026, 11, 2);
        var to = new DateOnly(2026, 11, 8);
        var ids = await context.Lessons
            .Where(l => l.AccountId == account.Id && l.Date >= from && l.Date <= to)
            .OrderBy(l => l.Date)
            .Select(l => l.VulcanId)
            .ToListAsync();

        Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
    }

    private static async Task SaveAsync(VulcanAccount account)
    {
        await using var context = PostgresFixture.CreateContext();
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
    }

    private static void AssertUniqueViolation(DbUpdateException? ex)
    {
        Assert.That(ex?.InnerException, Is.TypeOf<PostgresException>()
            .With.Property(nameof(PostgresException.SqlState)).EqualTo(UniqueViolation));
    }

    private static VulcanAccount NewAccount(string symbol = "powiatwulkanowy", int pupilId = 1) => new()
    {
        PrivatePemKey = "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
        CertificatePem = "-----BEGIN CERTIFICATE-----\nabc\n-----END CERTIFICATE-----",
        Fingerprint = "0123456789abcdef0123456789abcdef01234567",
        Symbol = symbol,
        RestUrl = $"https://uonetplus-komunikacja.vulcan.net.pl/{symbol}/000088",
        UnitId = 1,
        UnitName = "Szkoła Podstawowa nr 1",
        PupilId = pupilId,
        LoginId = pupilId,
        FirstName = "Jan",
        Surname = "Uczeń",
        ClassDisplay = "8A"
    };

    private static Exam NewExam(int vulcanId) => new()
    {
        VulcanId = vulcanId, Type = "Kartkówka", Content = "x", SubjectName = "x", TeacherName = "x",
        Deadline = new DateOnly(2026, 11, 10), CreatedAt = DateTime.UtcNow, ModifiedAt = DateTime.UtcNow
    };

    private static Lesson NewLesson(
        int vulcanId,
        LessonChangeType changeType = LessonChangeType.None,
        DateOnly? date = null) => new()
    {
        VulcanId = vulcanId, Date = date ?? new DateOnly(2026, 11, 3),
        Start = new TimeOnly(8, 0), End = new TimeOnly(8, 45), Position = 1, ChangeType = changeType
    };
}
