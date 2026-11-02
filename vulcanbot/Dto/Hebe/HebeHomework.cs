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
    HebeDateTime DateCreated,
    HebeDateTime Date,
    HebeDateTime? AnswerDate,
    HebeDateTime? AnswerDeadline,
    HebeTeacher Creator,
    HebeSubject Subject,
    IReadOnlyList<HebeAttachment>? Attachments
    );
