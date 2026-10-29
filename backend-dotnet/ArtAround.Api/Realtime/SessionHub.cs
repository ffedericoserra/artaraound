using ArtAround.Api.Infrastructure;
using ArtAround.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ArtAround.Api.Realtime;

/// <summary>
/// Room membership only. A client joins a session's group and receives broadcasts;
/// every change goes through REST, which owns validation and ownership checks.
/// SignalR drops a connection from its groups on disconnect.
/// </summary>
[Authorize]
public class SessionHub(SessionService sessions) : Hub
{
    private const string SessionIdKey = "sessionId";

    public async Task JoinSession(string code)
    {
        var joined = await sessions.GetStateAsync(Context.User!.RequireUserId(), code, Context.ConnectionAborted);
        if (joined is not { } session)
        {
            await Clients.Caller.SendAsync("session:error", "Session not found");
            return;
        }

        await LeaveSession();
        await Groups.AddToGroupAsync(Context.ConnectionId, SessionNotifier.Group(session.SessionId));
        if (session.IsOwner)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionNotifier.OwnerGroup(session.SessionId));
        }
        Context.Items[SessionIdKey] = session.SessionId;

        // The full catch-up: a client that reloads or joins late rebuilds its view from this.
        await Clients.Caller.SendAsync("session:state", session.State);
    }

    public async Task LeaveSession()
    {
        if (Context.Items.Remove(SessionIdKey, out var value) && value is long sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, SessionNotifier.Group(sessionId));
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, SessionNotifier.OwnerGroup(sessionId));
        }
    }
}
