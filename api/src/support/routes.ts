import { eq } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { users } from "../db/schema.js";
import { postLogToDiscord } from "./discord.js";

// Un LogOutput.log BepInEx peut largement dépasser la limite globale par défaut
// (8 Mo, voir index.ts) sur une session longue avec des mods verbeux.
const MAX_LOG_SIZE = 20 * 1024 * 1024;

export default async function supportRoutes(app: FastifyInstance) {
  // Envoie le LogOutput.log d'un joueur (bouton "Envoyer log" du launcher, à côté de
  // "Réparer") vers le salon Discord de support. Pas requireOnboarded : un joueur doit
  // pouvoir envoyer son log même si l'onboarding a un souci, seul requireAuth compte ici.
  app.post("/support/logs", { preHandler: [app.requireAuth] }, async (req, reply) => {
    const file = await req.file({ limits: { fileSize: MAX_LOG_SIZE } });
    if (!file) {
      return reply.code(400).send({ error: "No file uploaded" });
    }

    const buffer = await file.toBuffer();
    if (file.file.truncated) {
      return reply.code(413).send({ error: "Log file too large (max 20MB)" });
    }

    const user = db.select().from(users).where(eq(users.id, req.session!.userId)).get();
    if (!user) {
      return reply.code(401).send({ error: "Invalid session" });
    }

    const sent = await postLogToDiscord(buffer, "LogOutput.log", user.discordUsername, user.discordId);
    if (!sent) {
      return reply.code(502).send({ error: "Discord channel not configured or unreachable" });
    }

    return reply.code(204).send();
  });
}
