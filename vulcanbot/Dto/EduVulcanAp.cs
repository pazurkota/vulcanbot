namespace vulcanbot.Dto;

/// <summary>
/// JSON stored in the hidden <c>&lt;input id="ap"&gt;</c> on https://eduvulcan.pl/api/ap after logging in.
/// Each token is a one-time JWT for a single student.
/// </summary>
public record EduVulcanAp(
    bool Success,
    string? ErrorMessage,
    bool IsConsentAccepted,
    IReadOnlyList<string>? Tokens
    );
