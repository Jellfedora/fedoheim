import fp from "fastify-plugin";
import type { FastifyInstance, FastifyRequest, FastifyReply } from "fastify";
import { eq } from "drizzle-orm";
import { db } from "../db/client.js";
import { users } from "../db/schema.js";
import { verifySession, type SessionPayload } from "./jwt.js";
import { getGuildMemberRoles, hasAdminRole } from "./discord.js";
import { getRulesUpdatedAt } from "../content/rulesMeta.js";

declare module "fastify" {
  interface FastifyRequest {
    session?: SessionPayload;
  }
}

// Décore l'instance Fastify avec `requireAuth`, un preHandler à poser sur les routes
// protégées (manifest de modpack, téléchargement de mods...).
export default fp(async function authPlugin(app: FastifyInstance) {
  app.decorateRequest("session", undefined);

  app.decorate("requireAuth", async (req: FastifyRequest, reply: FastifyReply) => {
    const header = req.headers.authorization;
    if (!header?.startsWith("Bearer ")) {
      return reply.code(401).send({ error: "Missing bearer token" });
    }

    try {
      req.session = verifySession(header.slice("Bearer ".length));
    } catch {
      return reply.code(401).send({ error: "Invalid or expired token" });
    }
  });

  // À poser APRÈS requireAuth. Revérifie le rôle admin en direct auprès de Discord
  // (pas seulement le JWT, qui vit 30 jours) pour qu'une perte de rôle admin soit
  // effective immédiatement sur les actions d'écriture, pas seulement au prochain login.
  app.decorate("requireAdmin", async (req: FastifyRequest, reply: FastifyReply) => {
    if (!req.session) {
      return reply.code(401).send({ error: "Missing bearer token" });
    }

    const user = db.select().from(users).where(eq(users.id, req.session.userId)).get();
    if (!user) {
      return reply.code(401).send({ error: "Invalid session" });
    }

    const roles = await getGuildMemberRoles(user.discordId);
    if (!hasAdminRole(roles)) {
      return reply.code(403).send({ error: "Admin role required" });
    }
  });

  // À poser APRÈS requireAuth, sur les routes qui donnent accès au contenu du jeu
  // (manifest de modpack) : le joueur doit avoir validé le règlement et renseigné son
  // SteamID avant de pouvoir télécharger quoi que ce soit.
  app.decorate("requireOnboarded", async (req: FastifyRequest, reply: FastifyReply) => {
    if (!req.session) {
      return reply.code(401).send({ error: "Missing bearer token" });
    }

    const user = db.select().from(users).where(eq(users.id, req.session.userId)).get();
    if (!user) {
      return reply.code(401).send({ error: "Invalid session" });
    }

    if (user.isBanned) {
      return reply.code(403).send({ error: "Banned" });
    }

    const rulesUpdatedAt = getRulesUpdatedAt();
    const rulesAccepted =
      user.rulesAcceptedAt !== null &&
      (rulesUpdatedAt === null || user.rulesAcceptedAt >= rulesUpdatedAt);
    if (!rulesAccepted) {
      return reply.code(403).send({ error: "Rules not accepted" });
    }

    if (!user.steamId) {
      return reply.code(403).send({ error: "Steam ID not set" });
    }
  });
});

declare module "fastify" {
  interface FastifyInstance {
    requireAuth: (req: FastifyRequest, reply: FastifyReply) => Promise<void>;
    requireAdmin: (req: FastifyRequest, reply: FastifyReply) => Promise<void>;
    requireOnboarded: (req: FastifyRequest, reply: FastifyReply) => Promise<void>;
  }
}
