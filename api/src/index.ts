import fs from "node:fs";
import Fastify from "fastify";
import cors from "@fastify/cors";
import rateLimit from "@fastify/rate-limit";
import multipart from "@fastify/multipart";
import fastifyStatic from "@fastify/static";
import { config } from "./config.js";
import authPlugin from "./auth/plugin.js";
import authRoutes from "./auth/routes.js";
import modpackRoutes from "./modpacks/routes.js";
import modpackFileRoutes from "./modpacks/files.js";
import modpackIconRoutes from "./modpacks/icons.js";
import modpackConfigFileRoutes from "./modpacks/configFiles.js";
import onlinePlayersRoutes from "./modpacks/onlinePlayers.js";
import contentRoutes from "./content/routes.js";
import adminRoutes from "./admin/routes.js";
import announcementRoutes from "./announcements/routes.js";
import announcementImageRoutes, { UPLOADS_DIR } from "./announcements/images.js";
import settingsRoutes from "./settings/routes.js";
import supportRoutes from "./support/routes.js";

const app = Fastify({
  logger: config.NODE_ENV === "development" ? { transport: { target: "pino-pretty" } } : true,
});

fs.mkdirSync(UPLOADS_DIR, { recursive: true });

await app.register(cors, {
  // Le launcher appelle l'API depuis un webview local, pas un navigateur classique,
  // donc pas d'origine à restreindre finement pour l'instant.
  origin: true,
});

// Défense en profondeur de base contre le spam/brute-force — les routes sensibles
// restent de toute façon derrière Discord OAuth/requireAdmin, mais rien n'empêchait
// jusqu'ici de marteler /health ou /auth/discord/token sans aucune friction.
await app.register(rateLimit, {
  max: 100,
  timeWindow: "1 minute",
});

await app.register(multipart, {
  limits: { fileSize: 8 * 1024 * 1024, files: 1 },
});

// Sert les images d'annonces uploadées (voir POST /announcements/images).
await app.register(fastifyStatic, {
  root: UPLOADS_DIR,
  prefix: "/uploads/",
});

await app.register(authPlugin);
await app.register(authRoutes);
await app.register(modpackRoutes);
await app.register(modpackFileRoutes);
await app.register(modpackIconRoutes);
await app.register(modpackConfigFileRoutes);
await app.register(onlinePlayersRoutes);
await app.register(contentRoutes);
await app.register(adminRoutes);
await app.register(announcementRoutes);
await app.register(announcementImageRoutes);
await app.register(settingsRoutes);
await app.register(supportRoutes);

app.get("/health", async () => ({ status: "ok" }));

app
  .listen({ port: config.PORT, host: config.HOST })
  .then((address) => app.log.info(`API listening on ${address}`))
  .catch((err) => {
    app.log.error(err);
    process.exit(1);
  });
