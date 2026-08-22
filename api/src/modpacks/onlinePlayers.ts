import crypto from "node:crypto";
import type { FastifyInstance } from "fastify";
import { eq, isNotNull } from "drizzle-orm";
import { z } from "zod";
import { db } from "../db/client.js";
import { modpacks } from "../db/schema.js";

// Considéré périmé si aucun rapport reçu depuis plus longtemps que 3x l'intervalle de
// rapport du mod FedoServerTools (~30s) — large marge pour absorber un rapport manqué
// isolé sans faire clignoter le statut à tort.
const STALE_AFTER_MS = 90_000;

interface OnlinePlayerReport {
  name: string;
  // Nom brut de l'enum `Heightmap.Biome` côté jeu (ex: "Meadows", "BlackForest"), pas
  // traduit ici — au launcher de l'afficher en français. `null` si le joueur a désactivé
  // le partage de sa position (`ZNet.PlayerInfo.m_publicPosition`, voir le mod) : on ne
  // calcule/expose pas son biome dans ce cas, pour respecter ce choix.
  biome: string | null;
}

interface OnlineReport {
  players: OnlinePlayerReport[];
  // Envoyé explicitement par le mod (true à chaque rapport périodique, false sur un
  // arrêt propre du serveur — voir ZNetLifecyclePatches côté mod) — distinct de la
  // fraîcheur du rapport, qui couvre elle le cas d'un crash (pas d'arrêt propre, donc
  // pas de dernier rapport à `online:false` — seule la péremption du timestamp joue).
  online: boolean;
  reportedAt: number;
}

// En mémoire seulement : "qui est en ligne maintenant" n'a pas besoin de survivre à un
// redémarrage de l'API — au prochain rapport du mod (au plus ~30s après), l'état se
// reconstruit tout seul.
const reportsBySlug = new Map<string, OnlineReport>();

const reportBodySchema = z.object({
  players: z
    .array(
      z.object({
        name: z.string().trim().min(1),
        biome: z.string().trim().nullable().default(null),
      }),
    )
    .max(500),
  online: z.boolean().default(true),
});

function timingSafeEqual(a: string, b: string): boolean {
  const bufA = Buffer.from(a);
  const bufB = Buffer.from(b);
  if (bufA.length !== bufB.length) {
    return false;
  }
  return crypto.timingSafeEqual(bufA, bufB);
}

// Le jeton identifie déjà le profil de façon unique (un par modpack, voir
// routes.ts::POST /modpacks/:slug/report-token/regenerate) -- pas besoin de faire
// deviner un slug en plus au mod pour un simple rapport, une seule valeur à recopier
// dans son .cfg suffit. Comparaison en temps constant par ligne plutôt qu'un
// `WHERE report_token = ?` en SQL : la table reste minuscule (quelques profils tout au
// plus), le coût est négligeable.
function findModpackByToken(token: string) {
  const candidates = db.select().from(modpacks).where(isNotNull(modpacks.reportToken)).all();
  return candidates.find((m) => m.reportToken !== null && timingSafeEqual(token, m.reportToken));
}

export default async function onlinePlayersRoutes(app: FastifyInstance) {
  // Posté par le mod serveur FedoServerTools toutes les ~30s (et une dernière fois à
  // l'arrêt du serveur, `online:false`) depuis le serveur Valheim lui-même (dédié ou
  // solo/hôte) — ce n'est pas une session joueur, donc pas de requireAuth ici : le
  // jeton partagé (modpacks.reportToken, voir routes.ts) tient à la fois lieu d'identité
  // et désigne le profil concerné, pas de `:slug` dans l'URL.
  app.post("/modpacks/online-players", async (req, reply) => {
    const token = req.headers["x-server-token"];
    if (typeof token !== "string") {
      return reply.code(401).send({ error: "Invalid or missing server token" });
    }

    const modpack = findModpackByToken(token);
    if (!modpack) {
      return reply.code(401).send({ error: "Invalid or missing server token" });
    }

    const parsed = reportBodySchema.safeParse(req.body);
    if (!parsed.success) {
      return reply.code(400).send({ error: parsed.error.flatten() });
    }

    reportsBySlug.set(modpack.slug, {
      players: parsed.data.players,
      online: parsed.data.online,
      reportedAt: Date.now(),
    });
    return reply.send({ ok: true });
  });

  // Public, comme /health — lu par le launcher pour afficher qui est en ligne sur la
  // page d'accueil, sans compte requis. Scopé par `:slug` (contrairement au POST
  // ci-dessus) : ici c'est le launcher qui appelle sans jeton, pour un profil qu'il
  // connaît déjà (production, ou le profil actif pour un admin).
  // `online` combine la fraîcheur du dernier rapport ET son propre statut envoyé par
  // le mod — un arrêt propre du serveur repasse `online` à false immédiatement, un
  // crash (pas de dernier rapport `online:false`) est rattrapé par la péremption.
  app.get("/modpacks/:slug/online-players", async (req, reply) => {
    const { slug } = req.params as { slug: string };

    const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
    if (!modpack) {
      return reply.code(404).send({ error: "Modpack not found" });
    }

    const report = reportsBySlug.get(slug);
    const fresh = report !== undefined && Date.now() - report.reportedAt < STALE_AFTER_MS;
    const online = fresh && report.online;

    return reply.send({
      online,
      players: online ? report.players : [],
      updatedAt: report ? new Date(report.reportedAt).toISOString() : null,
    });
  });
}
