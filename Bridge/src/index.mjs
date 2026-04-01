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
    try {
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

          try {
            disconnectTikTokSession({ session, store, logger });
          } catch (error) {
            logger.error({ err: error, hostId: session.hostId }, "failed to disconnect expired session");
          }
        }
      });

      if (expiredCount > 0) {
        logger.info({ expiredCount, inactivityMs }, "inactive bridge sessions expired");
      }
    } catch (error) {
      logger.error({ err: error }, "inactivity sweep failed");
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
