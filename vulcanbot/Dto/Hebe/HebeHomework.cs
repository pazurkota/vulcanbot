namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Homework returned by <c>/api/mobile/homework/byPupil</c>.
/// </summary>
public record HebeHomework(
    int Id,
    string Key,
    int IdPupil,
    int IdHomework,
    string Content,
    bool IsAnswerRequired,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    DateOnly DateAt,
    DateTime? AnswerAt,
    DateOnly DeadlineAt,
    HebeTeacher Creator,
    HebeSubject Subject,
    IReadOnlyList<HebeAttachment>? Attachments = null
    );
