import pino from "pino";

export function createLogger(logLevel) {
  return pino({
    level: logLevel,
    base: undefined,
    timestamp: pino.stdTimeFunctions.isoTime
  });
}
