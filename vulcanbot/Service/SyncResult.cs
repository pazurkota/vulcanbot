namespace vulcanbot.Service;

/// <summary>
/// Outcome of syncing one kind of data (homework, exams, …) for one account.
/// </summary>
public record SyncResult(int Fetched, int Added, int Updated);
