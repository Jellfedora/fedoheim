import type { FastifyInstance } from "fastify";
import { eq } from "drizzle-orm";
import { z } from "zod";
import { db } from "../db/client.js";
import { settings } from "../db/schema.js";

const SETTINGS_ID = 1;

const settingsBodySchema = z.object({
  buyMeACoffeeUrl: z.string().trim().url(),
  heroEyebrow: z.string().trim().min(1).max(80),
  heroTagline: z.string().trim().min(1).max(160),
});

function serializeSettings(s: typeof settings.$inferSelect) {
  return {
    buyMeACoffeeUrl: s.buyMeACoffeeUrl,
    heroEyebrow: s.heroEyebrow,
    heroTagline: s.heroTagline,
  };
}

function getOrCreateSettings() {
  const existing = db.select().from(settings).where(eq(settings.id, SETTINGS_ID)).get();
  if (existing) return existing;
  return db.insert(settings).values({ id: SETTINGS_ID }).returning().get();
}

// Lecture publique (ces textes sont affichés à tout le monde sur l'accueil et dans la
// sidebar). Écriture réservée aux admins, comme le règlement/la FAQ.
export default async function settingsRoutes(app: FastifyInstance) {
  app.get("/settings", async (_req, reply) => {
    return reply.send(serializeSettings(getOrCreateSettings()));
  });

  app.put(
    "/settings",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const parsed = settingsBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      getOrCreateSettings();
      const updated = db
        .update(settings)
        .set(parsed.data)
        .where(eq(settings.id, SETTINGS_ID))
        .returning()
        .get()!;

      return reply.send(serializeSettings(updated));
    },
  );
}
