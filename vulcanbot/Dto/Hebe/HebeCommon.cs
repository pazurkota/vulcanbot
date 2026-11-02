namespace vulcanbot.Dto.Hebe;

public record HebeTeacher(
    int Id,
    string Name,
    string Surname,
    string DisplayName
    );

public record HebeSubject(
    int Id,
    string Key,
    string Name,
    string Kod,
    int Position
    );

public record HebeAttachment(
    string Name,
    string Link
    );
