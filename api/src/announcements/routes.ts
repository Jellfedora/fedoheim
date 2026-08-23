import type { FastifyInstance } from "fastify";
import { desc, eq } from "drizzle-orm";
import { z } from "zod";
import { db } from "../db/client.js";
import { announcements, users } from "../db/schema.js";
import { deleteAnnouncementImages } from "./images.js";
import {
  postAnnouncementToDiscord,
  editAnnouncementOnDiscord,
  deleteAnnouncementOnDiscord,
} from "./discord.js";

// Page d'accueil du launcher : 3 annonces chargées au départ, puis le reste par lots au
// scroll (voir HomePage.tsx) -- pas de valeur par défaut ici, `limit` absent renvoie tout
// (utilisé par rien aujourd'hui côté launcher, mais évite de casser un futur appelant
// qui ne penserait pas à paginer, ex: le site web prévu -- voir CLAUDE.md).
const listQuerySchema = z.object({
  limit: z.coerce.number().int().min(1).max(50).optional(),
  offset: z.coerce.number().int().min(0).default(0),
});

const announcementBodySchema = z.object({
  title: z.string().trim().max(200).optional(),
  // 4096 = limite réelle de Discord pour la description d'un embed (voir
  // announcements/discord.ts::DESCRIPTION_LIMIT) — au-delà, le repost Discord
  // tronquerait silencieusement le texte, donc autant refuser avant plutôt que de
  // publier une annonce coupée sans prévenir l'admin.
  message: z.string().trim().min(1).max(4096),
  images: z.array(z.string().trim().min(1)).max(10).optional(),
});

function serializeAnnouncement(a: typeof announcements.$inferSelect) {
  return {
    id: a.id,
    author: a.author,
    title: a.title,
    message: a.message,
    images: a.images,
    createdAt: a.createdAt,
    updatedAt: a.updatedAt,
    // Pas besoin d'exposer le discordMessageId brut au launcher, juste s'il est posté.
    postedToDiscord: a.discordMessageId !== null,
  };
}

// Lecture publique (pas de login requis), comme le règlement et la FAQ. Écriture
// réservée aux admins — l'auteur est dérivé du compte authentifié, jamais du body,
// pour ne pas pouvoir usurper un pseudo dans une annonce.
//
// `message` est du markdown façon Discord (**gras**, *italique*, __souligné__,
// ~~barré~~, ||spoiler||), posté tel quel dans le salon configuré via DISCORD_ANNOUNCEMENT_
// CHANNEL_ID (voir ./discord.ts) — best-effort, ne bloque jamais la requête API.
// `images` référence des fichiers uploadés via POST /announcements/images.
export default async function announcementRoutes(app: FastifyInstance) {
  app.get("/announcements", async (req, reply) => {
    const parsed = listQuerySchema.safeParse(req.query);
    if (!parsed.success) {
      return reply.code(400).send({ error: parsed.error.flatten() });
    }
    const { limit, offset } = parsed.data;

    const all = db.select().from(announcements).orderBy(desc(announcements.createdAt)).all();
    const page = limit !== undefined ? all.slice(offset, offset + limit) : all.slice(offset);

    return reply.send({ items: page.map(serializeAnnouncement), total: all.length });
  });

  app.post(
    "/announcements",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const parsed = announcementBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      const author = db.select().from(users).where(eq(users.id, req.session!.userId)).get();
      if (!author) {
        return reply.code(404).send({ error: "User not found" });
      }

      const title = parsed.data.title ?? null;
      const images = parsed.data.images ?? [];

      const created = db
        .insert(announcements)
        .values({
          author: author.discordUsername,
          title,
          message: parsed.data.message,
          images,
          createdAt: new Date(),
        })
        .returning()
        .get();

      const discordMessageId = await postAnnouncementToDiscord(title, parsed.data.message, images);
      const final = discordMessageId
        ? db
            .update(announcements)
            .set({ discordMessageId })
            .where(eq(announcements.id, created.id))
            .returning()
            .get()!
        : created;

      return reply.code(201).send(serializeAnnouncement(final));
    },
  );

  app.put(
    "/announcements/:id",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { id } = req.params as { id: string };
      const parsed = announcementBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      const existing = db
        .select()
        .from(announcements)
        .where(eq(announcements.id, Number(id)))
        .get();
      if (!existing) {
        return reply.code(404).send({ error: "Announcement not found" });
      }

      const title = parsed.data.title ?? null;
      const nextImages = parsed.data.images ?? existing.images;
      const removedImages = existing.images.filter((url) => !nextImages.includes(url));

      const updated = db
        .update(announcements)
        .set({
          title,
          message: parsed.data.message,
          images: nextImages,
          updatedAt: new Date(),
        })
        .where(eq(announcements.id, Number(id)))
        .returning()
        .get()!;

      if (removedImages.length > 0) {
        deleteAnnouncementImages(removedImages);
      }

      // On n'édite que si l'annonce avait déjà été postée — on ne la poste pas
      // rétroactivement ici, éditer n'a pas la même sémantique que publier.
      if (updated.discordMessageId) {
        await editAnnouncementOnDiscord(updated.discordMessageId, title, updated.message, nextImages);
      }

      return reply.send(serializeAnnouncement(updated));
    },
  );

  app.delete(
    "/announcements/:id",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { id } = req.params as { id: string };

      const deleted = db
        .delete(announcements)
        .where(eq(announcements.id, Number(id)))
        .returning()
        .get();

      if (!deleted) {
        return reply.code(404).send({ error: "Announcement not found" });
      }

      deleteAnnouncementImages(deleted.images);

      if (deleted.discordMessageId) {
        await deleteAnnouncementOnDiscord(deleted.discordMessageId);
      }

      return reply.send({ ok: true });
    },
  );
}
