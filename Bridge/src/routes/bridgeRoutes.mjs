import express from "express";
import sharp from "sharp";
import { normalizeHostId } from "../normalizers.mjs";
import { connectTikTokSession, disconnectTikTokSession } from "../tiktokSession.mjs";

const AVATAR_FETCH_TIMEOUT_MS = 8000;

function isNoWsUpgradeError(error) {
  if (!error) {
    return false;
  }

  const name = String(error.name || error.type || "").toLowerCase();
  const message = String(error.message || error || "").toLowerCase();
  return name.includes("nowsupgradeerror") || message.includes("does not offer a websocket upgrade");
}

function buildAvatarCandidateUrls(rawUrl) {
  if (!rawUrl) {
    return [];
  }

  let parsedUrl;
  try {
    parsedUrl = new URL(String(rawUrl).trim());
  } catch (_error) {
    return [];
  }

  if (parsedUrl.protocol !== "http:" && parsedUrl.protocol !== "https:") {
    return [];
  }

  const original = parsedUrl.toString();
  const pathname = parsedUrl.pathname || "";
  if (!/\.webp$/i.test(pathname)) {
    return [original];
  }

  const jpgPath = pathname.replace(/\.webp$/i, ".jpg");
  const jpegPath = pathname.replace(/\.webp$/i, ".jpeg");

  const jpgUrl = new URL(original);
  jpgUrl.pathname = jpgPath;

  const jpegUrl = new URL(original);
  jpegUrl.pathname = jpegPath;

  return [jpgUrl.toString(), jpegUrl.toString(), original];
}

async function fetchAvatarWithFallback(rawUrl, logger) {
  const candidates = buildAvatarCandidateUrls(rawUrl);
  for (const candidate of candidates) {
    const abortController = new AbortController();
    const timeoutId = setTimeout(() => abortController.abort(), AVATAR_FETCH_TIMEOUT_MS);

    try {
      const response = await fetch(candidate, {
        method: "GET",
        redirect: "follow",
        signal: abortController.signal
      });

      if (!response.ok) {
        logger.warn({ statusCode: response.status, candidate }, "avatar proxy fetch failed");
        continue;
      }

      const imageBuffer = Buffer.from(await response.arrayBuffer());
      const contentType = String(response.headers.get("content-type") || "application/octet-stream").toLowerCase();

      if (imageBuffer.length === 0) {
        logger.warn({ candidate }, "avatar proxy fetch returned empty body");
        continue;
      }

      return {
        sourceUrl: candidate,
        body: imageBuffer,
        contentType
      };
    } catch (error) {
      logger.warn({ err: error, candidate }, "avatar proxy fetch error");
    } finally {
      clearTimeout(timeoutId);
    }
  }

  return null;
}

function isWebPBuffer(buffer) {
  return Buffer.isBuffer(buffer)
    && buffer.length >= 12
    && buffer.toString("ascii", 0, 4) === "RIFF"
    && buffer.toString("ascii", 8, 12) === "WEBP";
}

async function normalizeAvatarForUnity({ body, contentType, logger, sourceUrl }) {
  const normalizedType = String(contentType || "").toLowerCase();
  const looksLikeWebP = normalizedType.includes("image/webp") || isWebPBuffer(body);
  if (!looksLikeWebP) {
    return { body, contentType: contentType || "application/octet-stream" };
  }

  try {
    const pngBody = await sharp(body).png().toBuffer();
    logger.info({ sourceUrl, inputContentType: contentType, outputBytes: pngBody.length }, "avatar converted webp->png");
    return { body: pngBody, contentType: "image/png" };
  } catch (error) {
    logger.warn({ err: error, sourceUrl }, "avatar webp conversion failed, returning original image");
    return { body, contentType: contentType || "application/octet-stream" };
  }
}

export function createBridgeRouter({ store, logger, config, adminStore }) {
  const router = express.Router();

  router.get("/status", (req, res) => {
    const hostId = normalizeHostId(req.query.hostId);
    if (!hostId) {
      return res.status(400).json({ ok: false, error: "hostId missing" });
    }

    const session = store.getOrCreate(hostId);
    store.markPolled(session);
    const lastEvent = session.events.length > 0 ? session.events[session.events.length - 1] : null;

    return res.json({
      ok: true,
      hostId: session.hostId,
      connected: session.connected,
      connecting: session.connecting,
      nextEventId: session.nextEventId,
      bufferedEvents: session.events.length,
      lastEventType: lastEvent?.type || null,
      lastEventId: lastEvent?.id || null,
      lastEventTimestamp: lastEvent?.timestamp || null
    });
  });

  router.post("/connect", async (req, res) => {
    const hostId = normalizeHostId(req.body?.hostId);
    const registrationGiftName = String(req.body?.registrationGiftName || "").trim();
    if (!hostId) {
      return res.status(400).json({ ok: false, error: "hostId missing" });
    }

    const session = store.getOrCreate(hostId);

    try {
      await connectTikTokSession({
        session,
        store,
        logger,
        registrationGiftName: registrationGiftName || config?.registrationGiftName,
        adminStore
      });
      store.markConnected(session);
      return res.json({ ok: true, hostId: session.hostId });
    } catch (error) {
      if (isNoWsUpgradeError(error)) {
        logger.warn({ err: error, hostId }, "connect endpoint upstream refused websocket upgrade");
        return res.json({
          ok: false,
          hostId,
          retryable: true,
          errorCode: "NO_WS_UPGRADE",
          error: "TikTok refused websocket upgrade for this host right now"
        });
      }

      logger.error({ err: error, hostId }, "connect endpoint failed");
      return res.status(500).json({ ok: false, error: String(error?.message || error) });
    }
  });

  router.get("/avatar", async (req, res) => {
    const rawUrl = String(req.query.url || "").trim();
    if (!rawUrl) {
      return res.status(400).json({ ok: false, error: "url missing" });
    }

    const avatarResult = await fetchAvatarWithFallback(rawUrl, logger);
    if (!avatarResult) {
      return res.status(502).json({ ok: false, error: "avatar fetch failed" });
    }

    const normalizedAvatar = await normalizeAvatarForUnity({
      body: avatarResult.body,
      contentType: avatarResult.contentType,
      logger,
      sourceUrl: avatarResult.sourceUrl
    });

    logger.info(
      {
        sourceUrl: avatarResult.sourceUrl,
        contentType: normalizedAvatar.contentType,
        bytes: normalizedAvatar.body.length
      },
      "avatar proxied"
    );

    res.setHeader("Content-Type", normalizedAvatar.contentType);
    res.setHeader("Cache-Control", "public, max-age=300");
    return res.status(200).send(normalizedAvatar.body);
  });

  router.get("/events", (req, res) => {
    const hostId = normalizeHostId(req.query.hostId);
    if (!hostId) {
      return res.status(400).json({ ok: false, error: "hostId missing", events: [] });
    }

    const after = Number(req.query.after || 0);
    const session = store.getOrCreate(hostId);
    store.markPolled(session);
    const events = store.getEventsAfter(session, after);
    return res.json({ ok: true, events });
  });

  router.post("/disconnect", (req, res) => {
    const hostId = normalizeHostId(req.body?.hostId);
    if (!hostId) {
      return res.status(400).json({ ok: false, error: "hostId missing" });
    }

    const session = store.getOrCreate(hostId);
    disconnectTikTokSession({ session, store, logger });

    return res.json({ ok: true });
  });

  router.post("/debug/inject", (req, res) => {
    const hostId = normalizeHostId(req.body?.hostId);
    const type = String(req.body?.type || "gift").trim().toLowerCase();
    if (!hostId) {
      return res.status(400).json({ ok: false, error: "hostId missing" });
    }

    const session = store.getOrCreate(hostId);
    if (type === "gift") {
      const userId = String(req.body?.userId || "debug-user").trim();
      const displayName = String(req.body?.displayName || userId).trim();
      const giftName = String(req.body?.giftName || "Rose").trim();

      store.appendEvent(session, {
        type: "gift",
        userId,
        displayName,
        giftName,
        avatarUrl: ""
      });

      return res.json({ ok: true, injectedType: "gift", giftName });
    }

    if (type === "chat") {
      const userId = String(req.body?.userId || "debug-user").trim();
      const displayName = String(req.body?.displayName || userId).trim();
      const message = String(req.body?.message || "1").trim();

      store.appendEvent(session, {
        type: "chat",
        userId,
        displayName,
        message,
        avatarUrl: ""
      });

      return res.json({ ok: true, injectedType: "chat", message });
    }

    return res.status(400).json({ ok: false, error: "unsupported type" });
  });

  return router;
}
