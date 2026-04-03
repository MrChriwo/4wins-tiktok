export function getConfig() {
  const adminAssignmentsRaw = String(
    process.env.BRIDGE_ADMIN_ASSIGNMENTS || ""
  ).trim();

  const globalAdminUsernamesRaw = String(
    process.env.BRIDGE_GLOBAL_ADMIN_USERNAMES || process.env.BRIDGE_ADMIN_USERNAMES || ""
  ).trim();

  return {
    port: Number(process.env.PORT || 3010),
    logLevel: process.env.BRIDGE_LOG_LEVEL || "info",
    eventBufferSize: Number(process.env.BRIDGE_EVENT_BUFFER_SIZE || 1500),
    registrationGiftName: String(process.env.BRIDGE_REGISTRATION_GIFT_NAME || "Rose").trim(),
    sessionInactivityTimeoutSeconds: Number(process.env.BRIDGE_SESSION_INACTIVITY_TIMEOUT_SECONDS || 20),
    adminDbPath: String(process.env.BRIDGE_ADMIN_DB_PATH || "./data/admins.sqlite").trim(),
    adminAssignments: adminAssignmentsRaw,
    globalAdminUsernames: globalAdminUsernamesRaw
  };
}
