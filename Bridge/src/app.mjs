import express from "express";
import cors from "cors";
import { createBridgeRouter } from "./routes/bridgeRoutes.mjs";

export function createApp({ store, logger, config }) {
  const app = express();

  app.use(cors());
  app.use(express.json());

  app.get("/health", (_req, res) => {
    res.json({ ok: true });
  });

  app.use("/bridge", createBridgeRouter({ store, logger, config }));

  return app;
}
