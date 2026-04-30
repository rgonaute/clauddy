namespace Clauddy.Models;

public enum SessionState { Chilling, Working, Alerting, Sleeping }

public record Session(
    string SessionId,
    string Label,
    SessionState State,
    string Cwd,
    DateTimeOffset LastSeen,
    string Source,
    long Tokens = 0,
    long InputTokens = 0,
    long OutputTokens = 0,
    long CacheCreationTokens = 0,
    long CacheReadTokens = 0,
    int Pid = 0)
{
    public long TotalTokens => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;

    /// <summary>
    /// Fraction of input that was served from cache, in [0, 1].
    /// 0 when no input has been processed yet.
    /// </summary>
    public double CacheHitRate
    {
        get
        {
            var totalInput = InputTokens + CacheCreationTokens + CacheReadTokens;
            return totalInput == 0 ? 0 : (double)CacheReadTokens / totalInput;
        }
    }
}
