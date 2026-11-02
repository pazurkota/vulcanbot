using System.Text.Json.Serialization;

namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Homework returned by <c>api/mobile/homework/byPupil</c>.
/// </summary>
public record HebeHomework(
    int Id,
    string Key,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int IdHomework,
    string Content,
    bool IsAnswerRequired,
    HebeDateTime DateCreated,
    HebeDateTime Deadline,
    HebeDateTime? AnswerDate,
    HebeDateTime? AnswerDeadline,
    HebeTeacher Creator,
    HebeSubject Subject,
    IReadOnlyList<HebeAttachment>? Attachments
    );
