import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { eq } from "drizzle-orm";
import { db } from "../db/client.js";
import { users } from "../db/schema.js";
import {
  exchangeCodeForAccessToken,
  getDiscordUser,
  getGuildMemberRoles,
  hasRequiredRole,
  hasAdminRole,
  DiscordAuthError,
} from "./discord.js";
import { signSession } from "./jwt.js";
import { isValidSteamId64 } from "./steam.js";
import { getRulesUpdatedAt } from "../content/rulesMeta.js";

const tokenBodySchema = z.object({
  code: z.string().min(1),
  redirectUri: z.string().url(),
});

const steamIdBodySchema = z.object({
  steamId: z.string().refine(isValidSteamId64, "Invalid SteamID64"),
});

// Discord ne renvoie qu'un hash d'avatar (ex: "a1b2c3..."), pas une URL exploitable —
// il faut le combiner avec l'ID Discord pour construire l'URL CDN. "a_" en préfixe
// signale un avatar animé (webp/gif), voir la doc Discord sur le format des hash.
export function discordAvatarUrl(user: typeof users.$inferSelect): string | null {
  if (!user.discordAvatar) return null;
  const extension = user.discordAvatar.startsWith("a_") ? "gif" : "png";
  return `https://cdn.discordapp.com/avatars/${user.discordId}/${user.discordAvatar}.${extension}`;
}

// hasAcceptedRules reflète la version ACTUELLE du règlement : si un admin l'a modifié
// depuis la dernière acceptation du joueur, elle redevient false et le launcher lui
// redemande de le signer (voir requireOnboarded dans auth/plugin.ts pour l'équivalent
// côté sécurité serveur).
function serializeUser(user: typeof users.$inferSelect) {
  const rulesUpdatedAt = getRulesUpdatedAt();
  const hasAcceptedRules =
    user.rulesAcceptedAt !== null &&
    (rulesUpdatedAt === null || user.rulesAcceptedAt >= rulesUpdatedAt);

  return {
    id: user.id,
    discordUsername: user.discordUsername,
    discordAvatar: discordAvatarUrl(user),
    isAdmin: user.isAdmin,
    hasAcceptedRules,
    // Brut, peut correspondre à une version dépassée du règlement (voir le commentaire
    // ci-dessus) — le launcher ne doit l'afficher comme "signé" que si hasAcceptedRules
    // est aussi vrai, pas se baser sur ce champ seul.
    rulesAcceptedAt: user.rulesAcceptedAt,
    steamId: user.steamId,
    // Posé une seule fois par le lien serveur↔SteamID (voir
    // modpacks/onlinePlayers.ts::linkCharacterName) -- `null` tant que ce compte n'a
    // jamais été vu connecté en jeu. Consommé par la partie client de FedoServerTools
    // (via le launcher) pour savoir s'il faut sauter direct en création de perso ou en
    // connexion.
    characterName: user.characterName,
  };
}

export default async function authRoutes(app: FastifyInstance) {
  // Appelé par le launcher une fois qu'il a récupéré le `code` OAuth2 sur son
  // serveur loopback local. Échange le code, vérifie le rôle Discord requis et le
  // statut de ban, upsert l'utilisateur, renvoie un JWT de session.
  app.post(
    "/auth/discord/token",
    // Échange un `code` OAuth2 auprès de Discord à chaque appel — plus coûteux et plus
    // sensible (brute-force de code) que le reste de l'API, d'où une limite dédiée plus
    // stricte que le plafond global de 100/min.
    { config: { rateLimit: { max: 10, timeWindow: "1 minute" } } },
    async (req, reply) => {
      const parsed = tokenBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }
      const { code, redirectUri } = parsed.data;

      try {
        const accessToken = await exchangeCodeForAccessToken(code, redirectUri);
        const discordUser = await getDiscordUser(accessToken);
        const roles = await getGuildMemberRoles(discordUser.id);

        if (!hasRequiredRole(roles)) {
          return reply.code(403).send({ error: "Missing required Discord role" });
        }

        const existing = db
          .select()
          .from(users)
          .where(eq(users.discordId, discordUser.id))
          .get();

        // Le ban est un état géré manuellement (voir admin/routes.ts), jamais recalculé
        // depuis Discord : on le vérifie avant de laisser passer, sans le toucher ici.
        if (existing?.isBanned) {
          return reply.code(403).send({ error: "Banned" });
        }

        const isAdmin = hasAdminRole(roles);
        const now = new Date();

        const user = existing
          ? db
              .update(users)
              .set({
                discordUsername: discordUser.username,
                discordAvatar: discordUser.avatar,
                isAdmin,
                lastLoginAt: now,
              })
              .where(eq(users.discordId, discordUser.id))
              .returning()
              .get()
          : db
              .insert(users)
              .values({
                discordId: discordUser.id,
                discordUsername: discordUser.username,
                discordAvatar: discordUser.avatar,
                isAdmin,
                createdAt: now,
                lastLoginAt: now,
              })
              .returning()
              .get();

        const token = signSession({ userId: user.id, discordId: user.discordId, isAdmin: user.isAdmin });

        return reply.send({ token, user: serializeUser(user) });
      } catch (err) {
        if (err instanceof DiscordAuthError) {
          return reply.code(err.statusCode).send({ error: err.message });
        }
        throw err;
      }
    },
  );

  // Appelé périodiquement par le launcher en tâche de fond (pas juste à la connexion) :
  // revérifie en direct que le rôle requis est toujours présent sur Discord et que le
  // joueur n'a pas été banni depuis, pour ne pas se reposer uniquement sur l'état figé
  // au moment du login initial.
  app.get("/auth/me", { preHandler: app.requireAuth }, async (req, reply) => {
    const user = db
      .select()
      .from(users)
      .where(eq(users.id, req.session!.userId))
      .get();

    if (!user) {
      return reply.code(404).send({ error: "User not found" });
    }

    if (user.isBanned) {
      return reply.code(403).send({ error: "Banned" });
    }

    try {
      const roles = await getGuildMemberRoles(user.discordId);

      if (!hasRequiredRole(roles)) {
        return reply.code(403).send({ error: "Missing required Discord role" });
      }

      const isAdmin = hasAdminRole(roles);
      const updated =
        isAdmin !== user.isAdmin
          ? db.update(users).set({ isAdmin }).where(eq(users.id, user.id)).returning().get()
          : user;

      return reply.send(serializeUser(updated));
    } catch (err) {
      if (err instanceof DiscordAuthError) {
        return reply.code(err.statusCode).send({ error: err.message });
      }
      throw err;
    }
  });

  // Validation du règlement par le joueur — une fois acceptée, elle reste acquise
  // (pas besoin de revalider à chaque session).
  app.post("/auth/accept-rules", { preHandler: app.requireAuth }, async (req, reply) => {
    const user = db
      .update(users)
      .set({ rulesAcceptedAt: new Date() })
      .where(eq(users.id, req.session!.userId))
      .returning()
      .get();

    if (!user) {
      return reply.code(404).send({ error: "User not found" });
    }

    return reply.send(serializeUser(user));
  });

  // Enregistrement du SteamID64 du joueur, requis avant de pouvoir télécharger le
  // modpack et jouer (voir CLAUDE.md / flow d'onboarding).
  app.post("/auth/steam-id", { preHandler: app.requireAuth }, async (req, reply) => {
    const parsed = steamIdBodySchema.safeParse(req.body);
    if (!parsed.success) {
      return reply.code(400).send({ error: parsed.error.flatten() });
    }

    const user = db
      .update(users)
      .set({ steamId: parsed.data.steamId })
      .where(eq(users.id, req.session!.userId))
      .returning()
      .get();

    if (!user) {
      return reply.code(404).send({ error: "User not found" });
    }

    return reply.send(serializeUser(user));
  });
}
