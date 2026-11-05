namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Test / quiz returned by <c>/api/mobile/exam/byPupil</c>.
/// </summary>
public record HebeExam(
    int Id,
    string Key,
    string Type,
    int TypeId,
    string Content,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    DateTime DeadlineAt,
    HebeTeacher Creator,
    HebeSubject Subject,
    int PupilId
    );
