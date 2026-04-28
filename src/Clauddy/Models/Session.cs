namespace Clauddy.Models;

public enum SessionState { Chilling, Working, Alerting }

public record Session(
    string SessionId,
    string Label,
    SessionState State,
    string Cwd,
    DateTimeOffset LastSeen,
    string Source,
    long Tokens = 0);
