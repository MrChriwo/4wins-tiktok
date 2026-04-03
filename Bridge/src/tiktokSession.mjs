import { WebcastPushConnection } from "tiktok-live-connector";
import { normalizeEventUser } from "./normalizers.mjs";

function normalizeUserId(userId) {
  return String(userId || "").trim().replace(/^@/, "").toLowerCase();
}

function normalizeGiftName(value) {
  return String(value || "")
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]/g, "");
}

function isRegistrationGiftMatch(requiredGiftName, receivedGiftName) {
  const requiredNormalized = normalizeGiftName(requiredGiftName);
  const receivedNormalized = normalizeGiftName(receivedGiftName);

  if (!requiredNormalized || !receivedNormalized) {
    return false;
  }

  if (requiredNormalized === receivedNormalized) {
    return true;
  }

  return receivedNormalized.includes(requiredNormalized)
    || requiredNormalized.includes(receivedNormalized);
}

function extractChatMessage(payload) {
  return String(
    payload?.comment ||
    payload?.message ||
    payload?.msg ||
    payload?.content ||
    payload?.text ||
    payload?.chat?.comment ||
    payload?.chat?.message ||
    ""
  ).trim();
}

function parseAdminCommand(message, fallbackUserId) {
  const normalizedMessage = String(message || "")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/^\/\s+/, "/");

  if (!normalizedMessage.startsWith("/")) {
    return null;
  }

  const parts = normalizedMessage.split(" ").filter(Boolean);
  if (parts.length === 0) {
    return null;
  }

  const command = String(parts[0] || "").replace(/^\//, "").toLowerCase();
  if (!command) {
    return null;
  }

  if (command === "register") {
    const target = normalizeUserId(parts[1]) || normalizeUserId(fallbackUserId);
    if (!target) {
      return null;
    }

    return {
      command: "register",
      targetUserId: target,
      targetDisplayName: String(parts[1] || fallbackUserId || target).replace(/^@+/, "").trim()
    };
  }

  if (command === "takeover") {
    const issuer = normalizeUserId(fallbackUserId);
    if (!issuer) {
      return null;
    }

    return {
      command: "takeover",
      targetUserId: issuer,
      targetDisplayName: String(fallbackUserId || issuer).replace(/^@+/, "").trim()
    };
  }

  if (command === "kick") {
    const target = normalizeUserId(parts[1]);
    if (!target) {
      return null;
    }

    return {
      command: "kick",
      targetUserId: target,
      targetDisplayName: String(parts[1] || target).replace(/^@+/, "").trim()
    };
  }

  return null;
}

function isDuplicateChat(session, userId, message) {
  const now = Date.now();
  const signature = `${userId}|${message}`;
  const isDuplicate = session.lastChatSignature === signature && (now - (session.lastChatAt || 0)) < 750;

  session.lastChatSignature = signature;
  session.lastChatAt = now;

  return isDuplicate;
}

function collectUserAliases(payload) {
  const aliases = new Set();

  const candidates = [
    payload?.user?.uniqueId,
    payload?.user?.userId,
    payload?.user?.id,
    payload?.uniqueId,
    payload?.userId,
    payload?.senderId,
    payload?.owner?.uniqueId,
    payload?.secUid
  ];

  for (const candidate of candidates) {
    const normalized = normalizeUserId(candidate);
    if (normalized) {
      aliases.add(normalized);
    }
  }

  return aliases;
}

export async function connectTikTokSession({ session, store, logger, registrationGiftName, adminStore }) {
  store.resetSessionLifecycle?.(session);

  const requestedGiftNormalized = normalizeGiftName(registrationGiftName || "Rose") || "rose";
  session.registrationGiftNameNormalized = requestedGiftNormalized;

  if (session.connected || session.connecting) {
    return;
  }

  session.connecting = true;

  if (session.connector) {
    try {
      session.connector.disconnect();
    } catch (error) {
      logger.warn({ err: error, hostId: session.hostId }, "failed to disconnect previous connector");
    }
  }

  const connector = new WebcastPushConnection(session.hostId);
  session.connector = connector;

  const handleChatEvent = (data, sourceEvent) => {
    const { userId, displayName, avatarUrl } = normalizeEventUser(data);
    const message = extractChatMessage(data);
    const normalizedUserId = normalizeUserId(userId);
    const aliases = collectUserAliases(data);
    if (normalizedUserId) {
      aliases.add(normalizedUserId);
    }

    const adminCommand = parseAdminCommand(message, normalizedUserId || userId);
    if (
      adminCommand
      && aliases.size > 0
      && adminStore
      && Array.from(aliases).some(alias => adminStore.isAdminForHost(session.hostId, alias))
    ) {
      const primaryUserId = normalizedUserId || Array.from(aliases)[0];
      store.appendEvent(session, {
        type: "admin_command",
        command: adminCommand.command,
        userId: primaryUserId,
        displayName,
        targetUserId: adminCommand.targetUserId,
        targetDisplayName: adminCommand.targetDisplayName,
        avatarUrl
      });

      logger.info(
        {
          hostId: session.hostId,
          command: adminCommand.command,
          issuedBy: primaryUserId,
          targetUserId: adminCommand.targetUserId
        },
        "admin command accepted"
      );
      return;
    }

    if (aliases.size === 0 || !message) {
      return;
    }

    const isRegistered = Array.from(aliases).some(alias => session.registeredUsers?.has(alias));
    if (!isRegistered) {
      return;
    }

    const primaryUserId = normalizedUserId || Array.from(aliases)[0];

    if (isDuplicateChat(session, primaryUserId, message)) {
      return;
    }

    store.appendEvent(session, {
      type: "chat",
      userId: primaryUserId,
      displayName,
      message,
      avatarUrl
    });
  };

  connector.on("chat", data => handleChatEvent(data, "chat"));
  connector.on("message", data => handleChatEvent(data, "message"));

  connector.on("gift", data => {
    const { userId, displayName, avatarUrl } = normalizeEventUser(data);
    const normalizedUserId = normalizeUserId(userId);
    const aliases = collectUserAliases(data);
    if (normalizedUserId) {
      aliases.add(normalizedUserId);
    }
    const giftName = String(
      data?.giftName ||
      data?.gift?.name ||
      data?.gift?.describe ||
      data?.giftDetails?.giftName ||
      data?.giftDetails?.name ||
      ""
    ).trim();

    const giftId = String(
      data?.giftId ||
      data?.gift?.giftId ||
      data?.gift?.id ||
      data?.giftDetails?.giftId ||
      ""
    ).trim();

    const resolvedGiftName = giftName || (giftId ? `gift-${giftId}` : "");

    if (!resolvedGiftName || aliases.size === 0) {
      return;
    }

    const requiredGiftNormalized = session.registrationGiftNameNormalized || "rose";
    const giftMatchesRegistration = isRegistrationGiftMatch(requiredGiftNormalized, resolvedGiftName);
    if (!giftMatchesRegistration) {
      return;
    }

    const wasRegistered = Array.from(aliases).some(alias => session.registeredUsers?.has(alias));
    if (!session.registeredUsers) {
      session.registeredUsers = new Set();
    }

    for (const alias of aliases) {
      session.registeredUsers.add(alias);
    }

    const primaryUserId = normalizedUserId || Array.from(aliases)[0];
    if (!wasRegistered) {
      logger.info(`user ${primaryUserId} registered for event`);
    }

    store.appendEvent(session, {
      type: "gift",
      userId: primaryUserId,
      displayName,
      giftName: resolvedGiftName,
      avatarUrl
    });
  });

  connector.on("disconnected", () => {
    session.connected = false;
    store.appendEvent(session, { type: "disconnected" });
  });

  connector.on("streamEnd", () => {
    session.connected = false;
    store.appendEvent(session, { type: "disconnected" });
  });

  try {
    await connector.connect();
    session.connected = true;
    store.markConnected?.(session);
    store.appendEvent(session, { type: "connected", target: session.hostId });
    logger.info(
      {
        hostId: session.hostId,
        adminUsers: adminStore?.listAdminsForHost?.(session.hostId) || []
      },
      "session started with sqlite-admin authorization"
    );
    logger.info(`connection to ${session.hostId} established`);
  } finally {
    session.connecting = false;
  }
}

export function disconnectTikTokSession({ session, store, logger }) {
  if (!session) {
    return;
  }

  if (session.connector) {
    try {
      session.connector.disconnect();
    } catch (error) {
      logger.warn({ err: error, hostId: session.hostId }, "failed to disconnect connector");
    }
  }

  session.connected = false;
  session.connecting = false;

  const deleted = store.deleteSession?.(session) ?? false;
  logger.info({ hostId: session.hostId, deleted }, "session lifecycle ended and cleaned");
}
