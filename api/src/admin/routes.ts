import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { eq } from "drizzle-orm";
import { db } from "../db/client.js";
import { users } from "../db/schema.js";

const banBodySchema = z.object({
  banned: z.boolean(),
});

// Pas encore d'UI dédiée côté launcher — à appeler directement (curl/Postman) en
// attendant un panneau d'admin. Protégé par requireAdmin (revérifié en direct sur
// Discord à chaque appel, voir auth/plugin.ts).
export default async function adminRoutes(app: FastifyInstance) {
  app.patch(
    "/admin/users/:discordId/ban",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { discordId } = req.params as { discordId: string };
      const parsed = banBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      const user = db
        .update(users)
        .set({ isBanned: parsed.data.banned })
        .where(eq(users.discordId, discordId))
        .returning()
        .get();

      if (!user) {
        return reply.code(404).send({ error: "User not found" });
      }

      return reply.send({ discordId: user.discordId, isBanned: user.isBanned });
    },
  );
}
