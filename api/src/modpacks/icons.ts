import path from "node:path";
import fs from "node:fs";
import { pipeline } from "node:stream/promises";
import { randomUUID } from "node:crypto";
import type { FastifyInstance } from "fastify";
import { UPLOADS_DIR } from "../announcements/images.js";

// Extension dérivée du mimetype validé, jamais du nom de fichier envoyé par le client
// (évite tout risque de traversée de chemin ou d'extension usurpée) — même principe que
// announcements/images.ts.
const EXTENSION_BY_MIME: Record<string, string> = {
  "image/png": ".png",
  "image/jpeg": ".jpg",
  "image/webp": ".webp",
};

export default async function modpackIconRoutes(app: FastifyInstance) {
  // Upload de l'icône d'un mod (icon.png, extrait par le launcher depuis le zip choisi
  // — voir modpack.rs::find_icon_bytes). Réservé aux admins, distinct de
  // POST /modpacks/files qui n'accepte que des zip.
  app.post(
    "/modpacks/icons",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const file = await req.file();
      if (!file) {
        return reply.code(400).send({ error: "No file uploaded" });
      }

      const extension = EXTENSION_BY_MIME[file.mimetype];
      if (!extension) {
        return reply.code(400).send({ error: "Unsupported image type" });
      }

      const filename = `${randomUUID()}${extension}`;
      const destPath = path.join(UPLOADS_DIR, filename);

      await pipeline(file.file, fs.createWriteStream(destPath));

      if (file.file.truncated) {
        fs.unlinkSync(destPath);
        return reply.code(413).send({ error: "File too large (max 8MB)" });
      }

      return reply.code(201).send({ url: `/uploads/${filename}` });
    },
  );
}
