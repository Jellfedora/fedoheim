import type { FastifyInstance } from "fastify";
import { asc, eq } from "drizzle-orm";
import { z } from "zod";
import { db } from "../db/client.js";
import { rules, faqEntries, rulesMeta } from "../db/schema.js";
import { syncRulesToDiscord } from "./discord.js";

const RULES_META_ID = 1;

const rulesBodySchema = z.object({
  rules: z.array(z.string().trim().min(1)).max(200),
});

const faqBodySchema = z.object({
  faq: z
    .array(
      z.object({
        question: z.string().trim().min(1),
        answer: z.string().trim().min(1),
      }),
    )
    .max(200),
});

// Contenu public en lecture (pas de login requis) : un joueur doit pouvoir lire le
// règlement et la FAQ avant même de se connecter via Discord. L'écriture, elle, est
// réservée aux admins (voir requireAdmin dans auth/plugin.ts, qui revérifie le rôle
// Discord en direct — impossible de forger l'accès juste en trafiquant le launcher).
export default async function contentRoutes(app: FastifyInstance) {
  app.get("/content/rules", async (_req, reply) => {
    const rows = db.select().from(rules).orderBy(asc(rules.sortOrder)).all();
    return reply.send(rows.map((r) => r.text));
  });

  app.put(
    "/content/rules",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const parsed = rulesBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      const existingMeta = db.select().from(rulesMeta).where(eq(rulesMeta.id, RULES_META_ID)).get();

      db.transaction((tx) => {
        tx.delete(rules).run();
        if (parsed.data.rules.length > 0) {
          tx.insert(rules)
            .values(parsed.data.rules.map((text, i) => ({ text, sortOrder: i })))
            .run();
        }

        // Marque le règlement comme modifié : les joueurs qui l'avaient déjà accepté
        // devront le re-signer (voir hasAcceptedRules dans auth/routes.ts).
        const now = new Date();
        if (existingMeta) {
          tx.update(rulesMeta).set({ updatedAt: now }).where(eq(rulesMeta.id, RULES_META_ID)).run();
        } else {
          tx.insert(rulesMeta).values({ id: RULES_META_ID, updatedAt: now }).run();
        }
      });

      // Best-effort : édite (ou poste) le règlement sur Discord si le salon est
      // configuré — ne bloque jamais la réponse de cette route.
      const discordMessageId = await syncRulesToDiscord(
        parsed.data.rules,
        existingMeta?.discordMessageId ?? null,
      );
      if (discordMessageId !== (existingMeta?.discordMessageId ?? null)) {
        db.update(rulesMeta).set({ discordMessageId }).where(eq(rulesMeta.id, RULES_META_ID)).run();
      }

      return reply.send({ ok: true });
    },
  );

  app.get("/content/faq", async (_req, reply) => {
    const rows = db.select().from(faqEntries).orderBy(asc(faqEntries.sortOrder)).all();
    return reply.send(rows.map((r) => ({ question: r.question, answer: r.answer })));
  });

  app.put(
    "/content/faq",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const parsed = faqBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      db.transaction((tx) => {
        tx.delete(faqEntries).run();
        if (parsed.data.faq.length > 0) {
          tx.insert(faqEntries)
            .values(parsed.data.faq.map((entry, i) => ({ ...entry, sortOrder: i })))
            .run();
        }
      });

      return reply.send({ ok: true });
    },
  );
}
