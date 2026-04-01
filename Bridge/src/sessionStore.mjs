import { normalizeHostId } from "./normalizers.mjs";

export class SessionStore {
  constructor({ eventBufferSize }) {
    this._eventBufferSize = eventBufferSize;
    this._sessions = new Map();
  }

  getOrCreate(hostId) {
    const key = normalizeHostId(hostId);
    if (!key) {
      return null;
    }

    if (!this._sessions.has(key)) {
      this._sessions.set(key, {
        hostId: key,
        connector: null,
        connected: false,
        connecting: false,
        registrationGiftNameNormalized: "rose",
        registeredUsers: new Set(),
        lastPolledAt: Date.now(),
        lastConnectedAt: 0,
        nextEventId: 0,
        events: []
      });
    }

    return this._sessions.get(key);
  }

  markPolled(session) {
    if (!session) {
      return;
    }

    session.lastPolledAt = Date.now();
  }

  markConnected(session) {
    if (!session) {
      return;
    }

    session.lastConnectedAt = Date.now();
  }

  sweepInactiveSessions({ inactivityMs, onExpire }) {
    if (!Number.isFinite(inactivityMs) || inactivityMs <= 0) {
      return 0;
    }

    const now = Date.now();
    let expired = 0;

    for (const session of this._sessions.values()) {
      const referenceTime = Math.max(Number(session.lastPolledAt || 0), Number(session.lastConnectedAt || 0));
      if (referenceTime <= 0 || (now - referenceTime) < inactivityMs) {
        continue;
      }

      if (!session.connected && !session.connecting) {
        continue;
      }

      expired += 1;
      if (typeof onExpire === "function") {
        onExpire(session);
      }
    }

    return expired;
  }

  appendEvent(session, event) {
    session.nextEventId += 1;
    session.events.push({
      id: session.nextEventId,
      timestamp: new Date().toISOString(),
      ...event
    });

    if (session.events.length > this._eventBufferSize) {
      session.events.splice(0, session.events.length - this._eventBufferSize);
    }
  }

  getEventsAfter(session, after) {
    if (!Number.isFinite(after)) {
      return session.events;
    }

    return session.events.filter(event => event.id > after);
  }
}
