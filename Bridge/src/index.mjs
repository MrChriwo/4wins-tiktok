import { getConfig } from "./config.mjs";
import { createLogger } from "./logger.mjs";
import { SessionStore } from "./sessionStore.mjs";
import { createApp } from "./app.mjs";
import { disconnectTikTokSession } from "./tiktokSession.mjs";
import { AdminStore } from "./adminStore.mjs";

async function startServer() {
  const config = getConfig();
  const logger = createLogger(config.logLevel);
  const store = new SessionStore({ eventBufferSize: config.eventBufferSize });
  const adminStore = new AdminStore({ dbPath: config.adminDbPath, logger });
  await adminStore.initialize({
    seedUsernames: config.adminAssignments,
    seedGlobalUsernames: config.globalAdminUsernames
  });
  const app = createApp({ store, logger, config, adminStore });

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
        sessionInactivityTimeoutSeconds: config.sessionInactivityTimeoutSeconds,
        adminDbPath: config.adminDbPath,
        adminAssignments: adminStore.listAllAssignments()
      },
      "4wins bridge server started"
    );
  });
}

startServer().catch(error => {
  console.error("failed to start 4wins bridge server", error);
  process.exit(1);
});
