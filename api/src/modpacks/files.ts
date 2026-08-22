import path from "node:path";
import fs from "node:fs";
import { pipeline } from "node:stream/promises";
import { randomUUID, createHash } from "node:crypto";
import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { UPLOADS_DIR } from "../announcements/images.js";

const deleteFilesBodySchema = z.object({
  urls: z.array(z.string()).max(50),
});

// Un zip de mod (dll + cfg + assets...) ou du package BepInEx lui-même dépasse largement
// le plafond de 8 Mo pensé pour des images d'annonces (index.ts) — override dédié ici.
// 1 Go pour couvrir les mods "AIO" à gros assets (ex: More_World_Locations_AIO, ~300 Mo).
const MAX_ZIP_SIZE = 1024 * 1024 * 1024;

export default async function modpackFileRoutes(app: FastifyInstance) {
  // Upload générique d'une archive zip (mod ou package BepInEx), réservé aux admins.
  // Renvoie une URL servie statiquement (voir @fastify/static dans index.ts) et le
  // sha256 calculé côté serveur à partir du fichier réellement écrit — jamais un hash
  // fourni par le client, qui n'est pas une source de confiance.
  app.post(
    "/modpacks/files",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const file = await req.file({ limits: { fileSize: MAX_ZIP_SIZE } });
      if (!file) {
        return reply.code(400).send({ error: "No file uploaded" });
      }

      if (file.mimetype !== "application/zip") {
        return reply.code(400).send({ error: "Expected a application/zip upload" });
      }

      const filename = `${randomUUID()}.zip`;
      const destPath = path.join(UPLOADS_DIR, filename);

      await pipeline(file.file, fs.createWriteStream(destPath));

      if (file.file.truncated) {
        fs.unlinkSync(destPath);
        return reply.code(413).send({ error: "File too large (max 1024MB)" });
      }

      const sha256 = createHash("sha256").update(fs.readFileSync(destPath)).digest("hex");

      return reply.code(201).send({ url: `/uploads/${filename}`, sha256 });
    },
  );

  // Supprime des fichiers uploadés (zip de mod/BepInEx, icône) qui ne seront finalement
  // pas utilisés — ex: l'admin importe un zip puis annule l'édition avant
  // d'enregistrer. Best-effort comme deleteAnnouncementImages : ignore un fichier déjà
  // absent, ne renvoie jamais d'erreur pour ça. Réservé aux admins.
  app.delete(
    "/modpacks/files",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const parsed = deleteFilesBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      for (const url of parsed.data.urls) {
        if (!url.startsWith("/uploads/")) continue;
        const filePath = path.join(UPLOADS_DIR, path.basename(url));
        fs.rm(filePath, { force: true }, () => {});
      }

      return reply.send({ ok: true });
    },
  );
}
