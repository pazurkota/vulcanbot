using System.Web;
using Microsoft.Extensions.Logging.Abstractions;
using vulcanbot.Dto;
using vulcanbot.Dto.Hebe;
using vulcanbot.Service;
using vulcanbot.Service.Hebe;
using vulcanbot.Test.Helpers;

namespace vulcanbot.Test.Service;

/// <summary>
/// Response payloads below follow the field names confirmed by kapi2289/vulcan-api and wulkanowy/sdk;
/// they are not captured from a live Vulcan server.
/// </summary>
public class VulcanDataServiceTests
{
    private const string RestUrl = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/";
    private const string DataUrl = "https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/123456/";
    private static readonly DeviceKey Key = VulcanRegistrationService.GenerateDeviceKey().Key;
    private static readonly DateOnly From = new(2026, 11, 2);
    private static readonly DateOnly To = new(2026, 11, 8);

    private static readonly VulcanStudentContext Student = new(Key, RestUrl, "123456", UnitId: 2, PupilId: 111, PeriodId: 501);

    private FakeHttpMessageHandler _handler = null!;
    private HttpClient _client = null!;
    private VulcanDataService _service = null!;

    [SetUp]
    public void Setup()
    {
        _handler = new FakeHttpMessageHandler();
        _client = new HttpClient(_handler);
        _service = new VulcanDataService(new HebeApiClient(_client, TimeProvider.System), NullLogger<VulcanDataService>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    private static string Ok(string envelope) =>
        $$"""{"Envelope":{{envelope}},"Status":{"Code":0,"Message":"OK"},"RequestId":"r","Timestamp":0}""";

    private static string Date(string date, string time = "00:00:00")
    {
        var d = DateOnly.Parse(date);
        var ts = new DateTimeOffset(d.ToDateTime(TimeOnly.Parse(time)), TimeSpan.FromHours(1)).ToUnixTimeMilliseconds();
        return $$"""{"Timestamp":{{ts}},"Date":"{{date}}","DateDisplay":"{{d:dd.MM.yyyy}}","Time":"{{time}}"}""";
    }

    private const string Teacher = """{"Id":7,"Name":"Jan","Surname":"Kowalski","DisplayName":"Jan Kowalski"}""";
    private const string Subject = """{"Id":3,"Key":"k3","Name":"Matematyka","Kod":"mat","Position":1}""";

    private void RespondToAny(string body) => _handler.DefaultBody = body;

    private Dictionary<string, string?> SentQuery()
    {
        var uri = _handler.Requests.Single().Uri!;
        var q = HttpUtility.ParseQueryString(uri.Query);
        return q.AllKeys.ToDictionary(k => k!, k => q[k]);
    }

    // ---------- students ----------

    private static readonly string StudentJson = $$"""
        {
          "TopLevelPartition": "powiatwulkanowy",
          "Partition": "powiatwulkanowy-123456",
          "ClassDisplay": "8A",
          "Unit": {"Id":2,"Symbol":"123456","Short":"SP1","RestURL":"https://lekcjaplus.vulcan.net.pl/powiatwulkanowy/123456/api","Name":"Szkoła Podstawowa nr 1","DisplayName":"SP1","Address":"ul. Szkolna 1"},
          "ConstituentUnit": {"Id":4,"Short":"SP1","Name":"Szkoła Podstawowa nr 1","Address":null},
          "Pupil": {"Id":111,"LoginId":2137,"LoginValue":"jan@example.com","FirstName":"Jan","SecondName":"","Surname":"Uczeń","Sex":true},
          "Periods": [
            {"Id":500,"Level":8,"Number":1,"Current":false,"Last":false,"Start":{{Date("2026-09-01")}},"End":{{Date("2027-01-31")}}},
            {"Id":501,"Level":8,"Number":2,"Current":true,"Last":true,"Start":{{Date("2027-02-01")}},"End":{{Date("2027-06-26")}}}
          ],
          "MessageBox": {"Id":1,"GlobalKey":"g","Name":"Jan Uczeń"}
        }
        """;

    [Test]
    public async Task GetStudentsAsync_GetsStudentListFromRestUrl()
    {
        RespondToAny(Ok($"[{StudentJson}]"));

        var students = await _service.GetStudentsAsync(Key, RestUrl, CancellationToken.None);

        var request = _handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.Uri!.ToString(), Is.EqualTo(RestUrl + "api/mobile/register/hebe"));
            Assert.That(request.Headers["vCanonicalUrl"], Is.EqualTo("api%2fmobile%2fregister%2fhebe"));

            var s = students.Single();
            Assert.That(s.ClassDisplay, Is.EqualTo("8A"));
            Assert.That(s.Unit.Symbol, Is.EqualTo("123456"));
            Assert.That(s.Unit.RestUrl, Does.StartWith("https://"));
            Assert.That(s.Pupil.Id, Is.EqualTo(111));
            Assert.That(s.Pupil.Surname, Is.EqualTo("Uczeń"));
            Assert.That(s.Periods, Has.Count.EqualTo(2));
            Assert.That(s.Periods[1].Start.ToDateOnly(), Is.EqualTo(new DateOnly(2027, 2, 1)));
        });
    }

    [Test]
    public async Task GetStudentsAsync_RestUrlWithoutTrailingSlash_BuildsSameUrl()
    {
        RespondToAny(Ok("[]"));

        await _service.GetStudentsAsync(Key, RestUrl.TrimEnd('/'), CancellationToken.None);

        Assert.That(_handler.Requests.Single().Uri!.ToString(), Is.EqualTo(RestUrl + "api/mobile/register/hebe"));
    }

    // ---------- exams ----------

    private static string ExamJson(int id, string deadline) => $$"""
        {"Id":{{id}},"Key":"e{{id}}","Type":"Sprawdzian","Content":"Funkcje kwadratowe","PupilId":111,
         "DateCreated":{{Date("2026-10-20", "12:30:00")}},"DateModify":{{Date("2026-10-21", "08:00:00")}},"Deadline":{{Date(deadline)}},
         "Creator":{{Teacher}},"Subject":{{Subject}},"Class":{"Id":1,"Key":"c","DisplayName":"8A","Symbol":"8A"},"Distribution":null}
        """;

    [Test]
    public async Task GetExamsAsync_SendsByPupilQueryAndSignsFullPath()
    {
        RespondToAny(Ok("[]"));

        await _service.GetExamsAsync(Student, From, To, CancellationToken.None);

        var request = _handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Uri!.GetLeftPart(UriPartial.Path), Is.EqualTo(DataUrl + "api/mobile/exam/byPupil"));
            Assert.That(SentQuery(), Is.EquivalentTo(new Dictionary<string, string?>
            {
                ["unitId"] = "2", ["pupilId"] = "111", ["periodId"] = "501",
                ["dateFrom"] = "2026-11-02", ["dateTo"] = "2026-11-08",
                ["lastId"] = "-2147483648", ["pageSize"] = "500", ["lastSyncDate"] = "1970-01-01 01:00:00"
            }));
            Assert.That(request.Headers["vCanonicalUrl"], Does.StartWith("api%2fmobile%2fexam%2fbypupil%3funitid%3d2%26"));
            Assert.That(request.Headers["vCanonicalUrl"], Does.EndWith("lastsyncdate%3d1970-01-01%252001%253a00%253a00"));
        });
    }

    [Test]
    public async Task GetExamsAsync_DeserializesAndFiltersByDeadline()
    {
        RespondToAny(Ok($"[{ExamJson(1, "2026-11-01")},{ExamJson(2, "2026-11-02")},{ExamJson(3, "2026-11-08")},{ExamJson(4, "2026-11-09")}]"));

        var exams = await _service.GetExamsAsync(Student, From, To, CancellationToken.None);

        Assert.That(exams.Select(e => e.Id), Is.EqualTo(new[] { 2, 3 }));
        var exam = exams[0];
        Assert.Multiple(() =>
        {
            Assert.That(exam.Type, Is.EqualTo("Sprawdzian"));
            Assert.That(exam.Content, Is.EqualTo("Funkcje kwadratowe"));
            Assert.That(exam.Subject.Name, Is.EqualTo("Matematyka"));
            Assert.That(exam.Creator.DisplayName, Is.EqualTo("Jan Kowalski"));
            Assert.That(exam.PupilId, Is.EqualTo(111));
            Assert.That(exam.Deadline.ToDateOnly(), Is.EqualTo(new DateOnly(2026, 11, 2)));
            Assert.That(exam.DateCreated.ToUtcDateTime(), Is.EqualTo(new DateTime(2026, 10, 20, 11, 30, 0, DateTimeKind.Utc)));
        });
    }

    // ---------- homework ----------

    private static string HomeworkJson(int id, string deadline, string idHomework = "77") => $$"""
        {"Id":{{id}},"Key":"h{{id}}","IdHomework":{{idHomework}},"Content":"Zad. 1-5 str. 42","IsAnswerRequired":true,
         "DateCreated":{{Date("2026-10-28")}},"Deadline":{{Date(deadline)}},"AnswerDate":null,"AnswerDeadline":{{Date("2026-11-04")}},
         "Creator":{{Teacher}},"Subject":{{Subject}},"Attachments":[{"Name":"karta.pdf","Link":"https://example.com/karta.pdf"}]}
        """;

    [Test]
    public async Task GetHomeworkAsync_DeserializesAndFiltersByDeadline()
    {
        RespondToAny(Ok($"[{HomeworkJson(1, "2026-11-05")},{HomeworkJson(2, "2026-12-01")}]"));

        var homework = await _service.GetHomeworkAsync(Student, From, To, CancellationToken.None);

        Assert.That(_handler.Requests.Single().Uri!.AbsolutePath, Does.EndWith("/api/mobile/homework/byPupil"));
        var h = homework.Single();
        Assert.Multiple(() =>
        {
            Assert.That(h.Id, Is.EqualTo(1));
            Assert.That(h.IdHomework, Is.EqualTo(77));
            Assert.That(h.IsAnswerRequired, Is.True);
            Assert.That(h.Deadline.ToDateOnly(), Is.EqualTo(new DateOnly(2026, 11, 5)));
            Assert.That(h.AnswerDeadline!.ToDateOnly(), Is.EqualTo(new DateOnly(2026, 11, 4)));
            Assert.That(h.AnswerDate, Is.Null);
            Assert.That(h.Attachments!.Single().Name, Is.EqualTo("karta.pdf"));
        });
    }

    [Test]
    public async Task GetHomeworkAsync_IdHomeworkAsString_IsParsed()
    {
        RespondToAny(Ok($"[{HomeworkJson(1, "2026-11-05", idHomework: "\"77\"")}]"));

        var homework = await _service.GetHomeworkAsync(Student, From, To, CancellationToken.None);

        Assert.That(homework.Single().IdHomework, Is.EqualTo(77));
    }

    // ---------- timetable ----------

    private static string LessonJson(int id, string date, string? change = null) => $$"""
        {"Id":{{id}},"Date":{{Date(date)}},
         "TimeSlot":{"Id":1,"Start":"08:00","End":"08:45","Display":"08:00-08:45","Position":1},
         "Room":{"Id":12,"Code":"12"},"TeacherPrimary":{{Teacher}},"TeacherSecondary":null,"Subject":{{Subject}},
         "Event":null,"Change":{{change ?? "null"}},"Clazz":{"Id":1,"Key":"c","DisplayName":"8A","Symbol":"8A"},
         "PupilAlias":null,"Distribution":null,"Visible":true}
        """;

    [Test]
    public async Task GetLessonsAsync_DeserializesScheduleWithChanges()
    {
        RespondToAny(Ok($$"""[{{LessonJson(1, "2026-11-03")}},{{LessonJson(2, "2026-11-04", """{"Id":9,"Type":2,"Separation":false}""")}}]"""));

        var lessons = await _service.GetLessonsAsync(Student, From, To, CancellationToken.None);

        Assert.That(_handler.Requests.Single().Uri!.AbsolutePath, Does.EndWith("/api/mobile/schedule/byPupil"));
        Assert.Multiple(() =>
        {
            Assert.That(lessons, Has.Count.EqualTo(2));
            Assert.That(lessons[0].TimeSlot.Start, Is.EqualTo("08:00"));
            Assert.That(lessons[0].Room!.Code, Is.EqualTo("12"));
            Assert.That(lessons[0].Class!.DisplayName, Is.EqualTo("8A"));
            Assert.That(lessons[0].Change, Is.Null);
            Assert.That(lessons[1].Change!.Type, Is.EqualTo(2));
            Assert.That(lessons[1].Date.ToDateOnly(), Is.EqualTo(new DateOnly(2026, 11, 4)));
        });
    }

    // ---------- errors / validation ----------

    [Test]
    public void GetExamsAsync_VulcanError_PropagatesVulcanApiException()
    {
        RespondToAny("""{"Envelope":null,"Status":{"Code":108,"Message":"Certyfikat nie istnieje"}}""");

        var ex = Assert.ThrowsAsync<VulcanApiException>(() => _service.GetExamsAsync(Student, From, To, CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(VulcanErrorKind.UnauthorizedCertificate));
    }

    [Test]
    public void GetLessonsAsync_ToBeforeFrom_ThrowsWithoutRequest()
    {
        Assert.ThrowsAsync<ArgumentException>(() => _service.GetLessonsAsync(Student, To, From, CancellationToken.None));
        Assert.That(_handler.Requests, Is.Empty);
    }

    // ---------- student context ----------

    [Test]
    public void StudentContext_Create_UsesUnitSymbolAndCurrentPeriod()
    {
        var student = System.Text.Json.JsonSerializer.Deserialize<HebeAccount>(StudentJson)!;

        var context = VulcanStudentContext.Create(Key, RestUrl, student, new DateOnly(2026, 11, 2));

        Assert.Multiple(() =>
        {
            Assert.That(context.DataUrl, Is.EqualTo(DataUrl));
            Assert.That(context.UnitId, Is.EqualTo(2));
            Assert.That(context.PupilId, Is.EqualTo(111));
            Assert.That(context.PeriodId, Is.EqualTo(501), "period flagged Current wins");
        });
    }

    [Test]
    public void StudentContext_SelectPeriod_WithoutCurrentFlag_UsesPeriodContainingToday()
    {
        var periods = new[]
        {
            Period(500, "2026-09-01", "2027-01-31"),
            Period(501, "2027-02-01", "2027-06-26"),
        };

        Assert.Multiple(() =>
        {
            Assert.That(VulcanStudentContext.SelectPeriod(periods, new DateOnly(2026, 11, 2))!.Id, Is.EqualTo(500));
            Assert.That(VulcanStudentContext.SelectPeriod(periods, new DateOnly(2027, 3, 1))!.Id, Is.EqualTo(501));
            Assert.That(VulcanStudentContext.SelectPeriod(periods, new DateOnly(2027, 8, 1))!.Id, Is.EqualTo(501), "falls back to latest");
            Assert.That(VulcanStudentContext.SelectPeriod([], new DateOnly(2027, 8, 1)), Is.Null);
        });
    }

    private static HebePeriod Period(int id, string start, string end)
    {
        static HebeDateTime D(string d)
        {
            var date = DateOnly.Parse(d);
            return new HebeDateTime(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds(),
                d, date.ToString("dd.MM.yyyy"), "00:00:00");
        }

        return new HebePeriod(id, 8, id - 499, Current: false, Last: false, D(start), D(end));
    }
}
