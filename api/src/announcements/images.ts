import path from "node:path";
import fs from "node:fs";
import { pipeline } from "node:stream/promises";
import { randomUUID } from "node:crypto";
import type { FastifyInstance } from "fastify";
import { config } from "../config.js";

// Relatif au cwd du process par défaut (comme data.sqlite, voir db/client.ts et
// config.ts), pas au fichier source — le process est toujours lancé depuis api/.
export const UPLOADS_DIR = path.resolve(config.UPLOADS_DIR);

// Extension dérivée du mimetype validé, jamais du nom de fichier envoyé par le client
// (évite tout risque de traversée de chemin ou d'extension usurpée).
const EXTENSION_BY_MIME: Record<string, string> = {
  "image/png": ".png",
  "image/jpeg": ".jpg",
  "image/webp": ".webp",
  "image/gif": ".gif",
};

export default async function announcementImageRoutes(app: FastifyInstance) {
  // Upload d'une image pour une annonce. Réservé aux admins. Renvoie une URL servie
  // statiquement par l'API (voir @fastify/static dans index.ts) à inclure dans le
  // champ `images` de POST/PUT /announcements.
  app.post(
    "/announcements/images",
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

// Best-effort : supprime les fichiers d'images uploadées associés à une annonce
// supprimée. Ignoré silencieusement si un fichier est déjà absent.
export function deleteAnnouncementImages(imageUrls: string[]) {
  for (const url of imageUrls) {
    if (!url.startsWith("/uploads/")) continue;
    const filePath = path.join(UPLOADS_DIR, path.basename(url));
    fs.rm(filePath, { force: true }, () => {});
  }
}
