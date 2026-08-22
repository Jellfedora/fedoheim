import path from "node:path";
import fs from "node:fs";
import { pipeline } from "node:stream/promises";
import { randomUUID, createHash } from "node:crypto";
import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { eq } from "drizzle-orm";
import { db } from "../db/client.js";
import { modpacks, configFiles } from "../db/schema.js";
import { UPLOADS_DIR } from "../announcements/images.js";

// Un fichier de config texte (.cfg BepInEx typiquement) — bien plus petit qu'un zip de
// mod, pas besoin du plafond de 1 Go de files.ts.
const MAX_CONFIG_FILE_SIZE = 4 * 1024 * 1024;

// Nom de destination dans BepInEx/config/ (voir modpack.rs::sync_config_files côté
// launcher) — pas de séparateur de chemin, pour qu'il ne puisse pas s'échapper de ce
// dossier une fois copié chez le joueur.
const filenameSchema = z
  .string()
  .trim()
  .min(1)
  .max(255)
  .refine(
    (v) => !v.includes("/") && !v.includes("\\") && !v.includes(".."),
    "Nom de fichier invalide",
  );

const downloadUrlSchema = z
  .string()
  .trim()
  .refine((v) => v.startsWith("/uploads/") || /^https?:\/\//.test(v), {
    message: "Must be an absolute URL or an /uploads/... path",
  });

const configFileBodySchema = z.object({
  filename: filenameSchema,
  downloadUrl: downloadUrlSchema,
  sha256: z
    .string()
    .trim()
    .regex(/^[0-9a-f]{64}$/i, "Invalid sha256"),
});

const configFilesBodySchema = z.object({
  files: z.array(configFileBodySchema).max(50),
});

export default async function modpackConfigFileRoutes(app: FastifyInstance) {
  // Upload d'un fichier de config brut choisi par l'admin (ex: FastLink.cfg pré-rempli
  // avec l'adresse/mot de passe du serveur) — contrairement à POST /modpacks/files, pas
  // un zip : ce fichier est copié tel quel par le launcher dans BepInEx/config/. Le nom
  // d'origine du fichier (`file.filename`, fourni par le client dans le multipart) est
  // renvoyé pour préremplir le champ "nom de destination" côté éditeur, mais le fichier
  // stocké lui-même est renommé (évite toute collision entre deux admins qui uploadent
  // un fichier du même nom).
  app.post(
    "/modpacks/config-files",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const file = await req.file({ limits: { fileSize: MAX_CONFIG_FILE_SIZE } });
      if (!file) {
        return reply.code(400).send({ error: "No file uploaded" });
      }

      const originalName = path.basename(file.filename || "config.cfg");
      const storedName = `${randomUUID()}-${originalName}`;
      const destPath = path.join(UPLOADS_DIR, storedName);

      await pipeline(file.file, fs.createWriteStream(destPath));

      if (file.file.truncated) {
        fs.unlinkSync(destPath);
        return reply.code(413).send({ error: "File too large (max 4MB)" });
      }

      const sha256 = createHash("sha256").update(fs.readFileSync(destPath)).digest("hex");

      return reply.code(201).send({
        url: `/uploads/${storedName}`,
        sha256,
        filename: originalName,
      });
    },
  );

  // Liste des fichiers de config du modpack, avec leur URL de téléchargement — pour
  // préremplir l'éditeur admin. Pas de liste publique distincte (contrairement aux mods) :
  // ce n'est pas un contenu à afficher aux joueurs, juste de la plomberie de sync.
  app.get(
    "/modpacks/:slug/config-files",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };
      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }

      const list = db
        .select()
        .from(configFiles)
        .where(eq(configFiles.modpackId, modpack.id))
        .all();

      return reply.send(
        list.map((f) => ({
          filename: f.filename,
          downloadUrl: f.downloadUrl,
          sha256: f.sha256,
          updatedAt: f.updatedAt,
        })),
      );
    },
  );

  // Remplace entièrement la liste des fichiers de config du modpack — même principe que
  // PUT /modpacks/:slug/mods (liste remplacée en bloc, pas de diff par id).
  app.put(
    "/modpacks/:slug/config-files",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };
      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }

      const parsed = configFilesBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      // Deux fichiers de config avec le même nom entreraient en collision sur le même
      // chemin de destination (BepInEx/config/<filename>) chez le joueur — le second
      // écraserait le premier silencieusement au moment de la sync.
      const names = parsed.data.files.map((f) => f.filename.toLowerCase());
      if (new Set(names).size !== names.length) {
        return reply.code(400).send({ error: "Deux fichiers de config portent le même nom" });
      }

      const now = new Date();
      db.transaction((tx) => {
        tx.delete(configFiles).where(eq(configFiles.modpackId, modpack.id)).run();
        if (parsed.data.files.length > 0) {
          tx.insert(configFiles)
            .values(
              parsed.data.files.map((f) => ({
                ...f,
                modpackId: modpack.id,
                updatedAt: now,
              })),
            )
            .run();
        }
      });

      return reply.send({ ok: true });
    },
  );
}
