using System.Collections.ObjectModel;
using Clauddy.Models;

namespace Clauddy.Services;

public class SessionStore
{
    public ObservableCollection<Session> Sessions { get; } = new();

    public void Upsert(Session s)
    {
        for (int i = 0; i < Sessions.Count; i++)
        {
            if (Sessions[i].SessionId == s.SessionId)
            {
                Sessions[i] = s;
                return;
            }
        }
        Sessions.Add(s);
    }

    public void Remove(string sessionId)
    {
        for (int i = Sessions.Count - 1; i >= 0; i--)
            if (Sessions[i].SessionId == sessionId) Sessions.RemoveAt(i);
    }

    public void RemoveStale(DateTimeOffset now, TimeSpan maxAge)
    {
        for (int i = Sessions.Count - 1; i >= 0; i--)
            if (now - Sessions[i].LastSeen > maxAge) Sessions.RemoveAt(i);
    }
}
