import fs from "node:fs/promises";
import path from "node:path";
import sqlite3 from "sqlite3";
import { normalizeHostId } from "./normalizers.mjs";

function normalizeUsername(value) {
  return String(value || "")
    .trim()
    .replace(/^@+/, "")
    .toLowerCase();
}

function parseSeedUsernames(seedUsernames) {
  if (Array.isArray(seedUsernames)) {
    return [];
  }

  const raw = String(seedUsernames || "").trim();
  if (!raw) {
    return [];
  }

  const assignments = [];
  const segments = raw
    .split(";")
    .map(segment => segment.trim())
    .filter(Boolean);

  for (const segment of segments) {
    const equalsIndex = segment.indexOf("=");
    if (equalsIndex <= 0) {
      continue;
    }

    const hostId = normalizeHostId(segment.slice(0, equalsIndex));
    if (!hostId) {
      continue;
    }

    const usernames = segment
      .slice(equalsIndex + 1)
      .split(",")
      .map(normalizeUsername)
      .filter(Boolean);

    for (const username of usernames) {
      assignments.push({ hostId, username });
    }
  }

  return assignments;
}

function parseGlobalSeedUsernames(seedUsernames) {
  if (Array.isArray(seedUsernames)) {
    return seedUsernames.map(normalizeUsername).filter(Boolean);
  }

  return String(seedUsernames || "")
    .split(",")
    .map(normalizeUsername)
    .filter(Boolean);
}

function openDatabase(dbPath) {
  return new Promise((resolve, reject) => {
    const database = new sqlite3.Database(dbPath, error => {
      if (error) {
        reject(error);
        return;
      }

      resolve(database);
    });
  });
}

function run(database, sql, params = []) {
  return new Promise((resolve, reject) => {
    database.run(sql, params, function onRun(error) {
      if (error) {
        reject(error);
        return;
      }

      resolve(this);
    });
  });
}

function all(database, sql, params = []) {
  return new Promise((resolve, reject) => {
    database.all(sql, params, (error, rows) => {
      if (error) {
        reject(error);
        return;
      }

      resolve(rows || []);
    });
  });
}

export class AdminStore {
  constructor({ dbPath, logger }) {
    this._dbPath = dbPath;
    this._logger = logger;
    this._database = null;
    this._adminsByHost = new Map();
    this._globalAdmins = new Set();
  }

  async initialize({ seedUsernames, seedGlobalUsernames }) {
    const resolvedPath = path.resolve(this._dbPath);
    await fs.mkdir(path.dirname(resolvedPath), { recursive: true });
    this._database = await openDatabase(resolvedPath);

    await run(
      this._database,
      `CREATE TABLE IF NOT EXISTS admin_host_users (
        host_id TEXT NOT NULL,
        username TEXT NOT NULL,
        created_at TEXT NOT NULL DEFAULT (datetime('now')),
        PRIMARY KEY (host_id, username)
      )`
    );

    await run(
      this._database,
      `CREATE TABLE IF NOT EXISTS admin_global_users (
        username TEXT PRIMARY KEY,
        created_at TEXT NOT NULL DEFAULT (datetime('now'))
      )`
    );

    const normalizedSeeds = parseSeedUsernames(seedUsernames);
    for (const assignment of normalizedSeeds) {
      await run(
        this._database,
        "INSERT OR IGNORE INTO admin_host_users (host_id, username) VALUES (?, ?)",
        [assignment.hostId, assignment.username]
      );
    }

    const normalizedGlobalSeeds = parseGlobalSeedUsernames(seedGlobalUsernames);
    for (const username of normalizedGlobalSeeds) {
      await run(
        this._database,
        "INSERT OR IGNORE INTO admin_global_users (username) VALUES (?)",
        [username]
      );
    }

    await this.reloadCache();
    this._logger?.info(
      {
        dbPath: resolvedPath,
        hostCount: this._adminsByHost.size,
        globalAdminCount: this._globalAdmins.size,
        assignments: this.listAllAssignments()
      },
      "admin sqlite store initialized"
    );
  }

  async reloadCache() {
    if (!this._database) {
      return;
    }

    const rows = await all(
      this._database,
      "SELECT host_id, username FROM admin_host_users ORDER BY host_id ASC, username ASC"
    );

    const globalRows = await all(
      this._database,
      "SELECT username FROM admin_global_users ORDER BY username ASC"
    );

    this._adminsByHost.clear();
    this._globalAdmins.clear();
    for (const row of rows) {
      const hostId = normalizeHostId(row?.host_id);
      const normalized = normalizeUsername(row?.username);
      if (!hostId || !normalized) {
        continue;
      }

      if (!this._adminsByHost.has(hostId)) {
        this._adminsByHost.set(hostId, new Set());
      }

      this._adminsByHost.get(hostId).add(normalized);
    }

    for (const row of globalRows) {
      const normalized = normalizeUsername(row?.username);
      if (normalized) {
        this._globalAdmins.add(normalized);
      }
    }
  }

  isAdminForHost(hostId, username) {
    const normalizedHost = normalizeHostId(hostId);
    const normalized = normalizeUsername(username);
    if (!normalizedHost || !normalized) {
      return false;
    }

    if (this._globalAdmins.has(normalized)) {
      return true;
    }

    const hostAdmins = this._adminsByHost.get(normalizedHost);
    if (!hostAdmins) {
      return false;
    }

    return hostAdmins.has(normalized);
  }

  listAdminsForHost(hostId) {
    const normalizedHost = normalizeHostId(hostId);
    if (!normalizedHost) {
      return this.listGlobalAdmins();
    }

    const combined = new Set(this._globalAdmins.values());
    const hostAdmins = this._adminsByHost.get(normalizedHost);
    if (hostAdmins) {
      for (const username of hostAdmins.values()) {
        combined.add(username);
      }
    }

    return Array.from(combined.values());
  }

  listGlobalAdmins() {
    return Array.from(this._globalAdmins.values());
  }

  listAllAssignments() {
    const assignments = [];
    for (const [hostId, usernames] of this._adminsByHost.entries()) {
      assignments.push({
        hostId,
        usernames: Array.from(usernames.values())
      });
    }

    assignments.push({
      hostId: "*",
      usernames: this.listGlobalAdmins()
    });

    return assignments;
  }
}

export function normalizeAdminUsername(value) {
  return normalizeUsername(value);
}
