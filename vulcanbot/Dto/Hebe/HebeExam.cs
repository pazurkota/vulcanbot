namespace vulcanbot.Dto.Hebe;

/// <summary>
/// Test / quiz returned by <c>api/mobile/exam/byPupil</c>.
/// </summary>
public record HebeExam(
    int Id,
    string Key,
    string Type,
    string Content,
    HebeDateTime DateCreated,
    HebeDateTime DateModify,
    HebeDateTime Deadline,
    HebeTeacher Creator,
    HebeSubject Subject
    );
