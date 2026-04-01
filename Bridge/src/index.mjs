import { getConfig } from "./config.mjs";
import { createLogger } from "./logger.mjs";
import { SessionStore } from "./sessionStore.mjs";
import { createApp } from "./app.mjs";
import { disconnectTikTokSession } from "./tiktokSession.mjs";

const config = getConfig();
const logger = createLogger(config.logLevel);
const store = new SessionStore({ eventBufferSize: config.eventBufferSize });
const app = createApp({ store, logger, config });

const inactivityMs = Math.max(0, Number(config.sessionInactivityTimeoutSeconds || 0)) * 1000;
const sweepIntervalMs = 5000;

if (inactivityMs > 0) {
  setInterval(() => {
    const expiredCount = store.sweepInactiveSessions({
      inactivityMs,
      onExpire: session => {
        logger.warn(
          {
            hostId: session.hostId,
            inactivityMs,
            lastPolledAt: session.lastPolledAt || null,
            lastConnectedAt: session.lastConnectedAt || null
          },
          "expiring inactive bridge session"
        );

        disconnectTikTokSession({ session, store, logger });
      }
    });

    if (expiredCount > 0) {
      logger.info({ expiredCount, inactivityMs }, "inactive bridge sessions expired");
    }
  }, sweepIntervalMs);
}

app.listen(config.port, () => {
  logger.info(
    {
      port: config.port,
      logLevel: config.logLevel,
      sessionInactivityTimeoutSeconds: config.sessionInactivityTimeoutSeconds
    },
    "4wins bridge server started"
  );
});
