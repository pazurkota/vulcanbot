namespace vulcanbot.Service;

/// <summary>
/// Outcome of syncing one kind of data (homework, exams, …) for one account.
/// </summary>
/// <param name="Removed">Items no longer returned by Vulcan that were deleted or marked as removed.</param>
public record SyncResult(int Fetched, int Added, int Updated, int Removed = 0);
