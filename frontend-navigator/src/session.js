/**
 * Session hub helper (SignalR) — the real-time half of a group visit (Extension 1).
 *
 * The server treats the hub as broadcast-only: every mutation goes through the
 * REST endpoints in `src/api.js` and the server echoes the result to the room.
 * So this module only ever *connects, joins a room and listens*. If you find
 * yourself wanting a connection.invoke() that changes state, add a REST
 * call instead — the ownership and bounds checks live there.
 */

import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { getToken } from './api.js';

/** Activity actions worth showing the teacher, labelled as the student saw them. */
export const ACTIVITY_LABELS = {
  joined: 'activities.actions.joined',
  left: 'activities.actions.left',
  more: 'activities.actions.more',
  simpler: 'activities.actions.simpler',
  author: 'activities.actions.author',
  year: 'activities.actions.year',
  exit: 'activities.actions.exit',
  map: 'activities.actions.map',
  // Legacy values kept so old sessions still render something sensible.
  tellMore: 'activities.actions.tellMore',
  tellLess: 'activities.actions.tellLess',
  tooSimple: 'activities.actions.tooSimple',
};

/**
 * Connect to the session hub and join the group for `code`.
 *
 * `handlers` is a map of event name → callback; every key is wired before the
 * join is invoked, so the catch-up `session:state` can't arrive before its
 * listener exists. Returns a disconnect function for the effect cleanup.
 */
export function connectSession(code, handlers = {}) {
  const connection = new HubConnectionBuilder()
    // Same origin in both dev (vite proxies /hubs) and production (the API serves
    // the built app), so there's no URL to configure. A WebSocket cannot carry an
    // Authorization header, so SignalR sends the token as ?access_token=.
    .withUrl('/hubs/session', { accessTokenFactory: () => getToken() })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  for (const [event, handler] of Object.entries(handlers)) {
    connection.on(event, handler);
  }

  // Re-join after every reconnect, not just the first connect: group membership
  // belongs to the connection, and a reconnect is a new connection.
  const join = () => connection.invoke('JoinSession', code);
  connection.onreconnected(join);

  let stopped = false;
  const started = connection.start().then(() => {
    if (!stopped) return join();
  });
  started.catch((err) => console.warn('Session connection failed', err));

  return () => {
    stopped = true;
    // stop() also leaves every group: membership ends with the connection.
    started.finally(() => connection.stop());
  };
}
