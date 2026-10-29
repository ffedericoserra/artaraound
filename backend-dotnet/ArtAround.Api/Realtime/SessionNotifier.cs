using Microsoft.AspNetCore.SignalR;

namespace ArtAround.Api.Realtime;

/// <summary>
/// Broadcasts committed changes to a session's SignalR groups. Groups are keyed by the
/// session id, not the code: a code is reused once its session ends.
/// </summary>
public class SessionNotifier(IHubContext<SessionHub> hub)
{
    public static string Group(long sessionId) => $"session-{sessionId}";

    public static string OwnerGroup(long sessionId) => $"session-{sessionId}-owner";

    public Task ToSessionAsync(long sessionId, string eventName, object payload) =>
        hub.Clients.Group(Group(sessionId)).SendAsync(eventName, payload);

    public Task ToOwnerAsync(long sessionId, string eventName, object payload) =>
        hub.Clients.Group(OwnerGroup(sessionId)).SendAsync(eventName, payload);
}
