import crypto from "node:crypto";
import type { FastifyInstance } from "fastify";
import { and, eq, isNotNull } from "drizzle-orm";
import { z } from "zod";
import { db } from "../db/client.js";
import { modpacks, playerStats, users } from "../db/schema.js";
import { discordAvatarUrl } from "../auth/routes.js";

// Considéré périmé si aucun rapport reçu depuis plus longtemps que 3x l'intervalle de
// rapport du mod FedoServerTools (~30s) — large marge pour absorber un rapport manqué
// isolé sans faire clignoter le statut à tort.
const STALE_AFTER_MS = 90_000;

interface OnlinePlayerReport {
  name: string;
  // Texte final déjà traduit par le mod (voir fedo.servertools.cfg, section [Biomes]) —
  // affiché tel quel, aucun mapping ici. `null` si le joueur a désactivé le partage de
  // sa position et que ForcePublicPosition ne le contourne pas (voir le mod).
  biome: string | null;
  // Armure totale actuelle (Humanoid.GetBodyArmor(), arrondie côté mod) — `null` si le
  // personnage n'a pas pu être retrouvé côté serveur au moment du rapport.
  armor: number | null;
  // SteamID64 du pair connecté, résolu côté serveur (voir FedoServerToolsPlugin) —
  // `null` si non résolvable. Sert uniquement à lier ce nom de perso au compte
  // Fedoheim correspondant (voir linkCharacterName ci-dessous), jamais affiché tel
  // quel.
  steamId: string | null;
}

// Envoyé explicitement par le mod : "starting" juste après le chargement du plugin
// (avant même de savoir si ZNet finira de démarrer, voir FedoServerToolsPlugin.Awake),
// "online" en rapport périodique normal une fois la fenêtre de démarrage passée,
// "stopping" dès qu'un arrêt est amorcé (OnApplicationQuit, best-effort) et confirmé
// une dernière fois juste avant la destruction de ZNet (rapport bloquant, voir
// ReportBlocking côté mod). Distinct de la fraîcheur du rapport ci-dessous, qui couvre
// elle le cas d'un crash (aucun de ces rapports n'a pu partir) — voir "offline" plus bas.
type ReportedStatus = "starting" | "online" | "stopping";

interface OnlinePlayerReport {
  name: string;
  // Texte final déjà traduit par le mod (voir fedo.servertools.cfg, section [Biomes]) —
  // affiché tel quel, aucun mapping ici. `null` si le joueur a désactivé le partage de
  // sa position et que ForcePublicPosition ne le contourne pas (voir le mod).
  biome: string | null;
  // Armure totale actuelle (Humanoid.GetBodyArmor(), arrondie côté mod) — `null` si le
  // personnage n'a pas pu être retrouvé côté serveur au moment du rapport.
  armor: number | null;
  // SteamID64 du pair connecté, résolu côté serveur (voir FedoServerToolsPlugin) —
  // `null` si non résolvable. Sert uniquement à lier ce nom de perso au compte
  // Fedoheim correspondant (voir linkCharacterName ci-dessous), jamais affiché tel
  // quel.
  steamId: string | null;
}

interface OnlineReport {
  players: OnlinePlayerReport[];
  status: ReportedStatus;
  // Saison actuelle rapportée par le mod Seasons (shudnal/Seasons) via
  // FedoServerTools, déjà traduite côté mod (voir fedo.servertools.cfg, section
  // [Seasons]) -- `null` si ce mod tiers n'est pas installé sur le serveur, pas une
  // donnée par joueur contrairement à biome/armor.
  season: string | null;
  // Horloge en jeu au format "HH:MM" (voir FedoServerToolsPlugin.GetCurrentGameTime,
  // dérivée de EnvMan.GetDayFraction()) -- même principe que `season`, une seule valeur
  // par rapport. `null` si EnvMan n'est pas encore chargé au moment du rapport.
  time: string | null;
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
        armor: z.number().nullable().default(null),
        steamId: z.string().trim().nullable().default(null),
      }),
    )
    .max(500),
  status: z.enum(["starting", "online", "stopping"]).default("online"),
  season: z.string().trim().nullable().default(null),
  time: z.string().trim().nullable().default(null),
});

const characterCheckSchema = z.object({
  characterName: z.string().trim().min(1),
  steamId: z.string().trim().nullable().default(null),
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

// Historique persistant "dernier état connu" par joueur (voir player_stats dans
// schema.ts) -- contrairement à reportsBySlug ci-dessus (en mémoire, périmé après 90s),
// une ligne ici survit à une déconnexion : un joueur qui se déconnecte garde son dernier
// biome/armure connu plutôt que de disparaître de la page "Joueurs" du launcher. Select
// puis update-ou-insert plutôt qu'un vrai upsert SQL : ce dépôt n'utilise `onConflict`
// nulle part ailleurs, et le volume (quelques joueurs par rapport, un rapport toutes les
// ~30s) rend la différence de perf non pertinente ici.
function upsertPlayerStats(modpackId: number, players: OnlinePlayerReport[], now: Date) {
  for (const player of players) {
    const existing = db
      .select()
      .from(playerStats)
      .where(and(eq(playerStats.modpackId, modpackId), eq(playerStats.name, player.name)))
      .get();

    if (existing) {
      db.update(playerStats)
        .set({ biome: player.biome, armor: player.armor, lastSeenAt: now })
        .where(eq(playerStats.id, existing.id))
        .run();
    } else {
      db.insert(playerStats)
        .values({ modpackId, name: player.name, biome: player.biome, armor: player.armor, lastSeenAt: now })
        .run();
    }
  }
}

// Associe un nom de perso au compte Fedoheim correspondant, "premier arrivé, premier
// servi" (voir schema.ts::users.characterName) : si le steamId rapporté correspond à un
// compte dont characterName est encore null, ET que ce nom n'est pas déjà pris par un
// AUTRE compte, on le pose définitivement. Sinon (déjà lié à ce nom, ou pris par
// quelqu'un d'autre) on ne touche à rien -- c'est ce qui matérialise "restreint à ce
// nom" une fois posé, sans jamais faire échouer le rapport du mod.
function linkCharacterName(players: OnlinePlayerReport[]) {
  for (const player of players) {
    if (!player.steamId) continue;

    const user = db.select().from(users).where(eq(users.steamId, player.steamId)).get();
    if (!user || user.characterName !== null) continue;

    const nameTaken = db.select().from(users).where(eq(users.characterName, player.name)).get();
    if (nameTaken) continue;

    db.update(users).set({ characterName: player.name }).where(eq(users.id, user.id)).run();
  }
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

    const now = new Date();
    reportsBySlug.set(modpack.slug, {
      players: parsed.data.players,
      status: parsed.data.status,
      season: parsed.data.season,
      time: parsed.data.time,
      reportedAt: now.getTime(),
    });

    // Seuls les rapports "online" portent une vraie liste de joueurs ("starting"/
    // "stopping" sont envoyés avec une liste vide, voir FedoServerToolsPlugin) -- pas la
    // peine de faire une requête pour rien dans ces deux cas.
    if (parsed.data.players.length > 0) {
      upsertPlayerStats(modpack.id, parsed.data.players, now);
      linkCharacterName(parsed.data.players);
    }

    return reply.send({ ok: true });
  });

  // Appelé par FedoServerTools à chaque connexion d'un joueur distant (jamais pour
  // l'hôte lui-même, voir mods/FedoServerTools/PeerSteamId.cs), avant de le laisser
  // rejoindre pour de bon — protection contre l'usurpation d'un nom déjà lié à un AUTRE
  // compte Fedoheim (voir linkCharacterName ci-dessus, "premier arrivé, premier servi") :
  // sans ce contrôle, n'importe qui pourrait recréer localement un perso du même nom et
  // se faire passer pour ce compte aux yeux du serveur/des autres joueurs. Un nom pas
  // encore lié à personne est toujours autorisé — cette route ne fait qu'empêcher un vol
  // d'identité, jamais la première liaison elle-même (qui reste `linkCharacterName`, sur
  // rapport périodique normal). Réponse par code HTTP seul (200/403), pas de JSON à
  // parser côté mod — aucun mod de ce repo n'a de dépendance de parsing JSON.
  app.post("/modpacks/character-check", async (req, reply) => {
    const token = req.headers["x-server-token"];
    if (typeof token !== "string") {
      return reply.code(401).send({ error: "Invalid or missing server token" });
    }

    const modpack = findModpackByToken(token);
    if (!modpack) {
      return reply.code(401).send({ error: "Invalid or missing server token" });
    }

    const parsed = characterCheckSchema.safeParse(req.body);
    if (!parsed.success) {
      return reply.code(400).send({ error: parsed.error.flatten() });
    }

    const owner = db
      .select()
      .from(users)
      .where(eq(users.characterName, parsed.data.characterName))
      .get();

    if (!owner || (parsed.data.steamId !== null && owner.steamId === parsed.data.steamId)) {
      return reply.send({ ok: true });
    }

    return reply.code(403).send({ error: "Character name already linked to another account" });
  });

  // Public, comme /health — lu par le launcher pour afficher l'état du serveur sur la
  // page d'accueil, sans compte requis. Scopé par `:slug` (contrairement au POST
  // ci-dessus) : ici c'est le launcher qui appelle sans jeton, pour un profil qu'il
  // connaît déjà (production, ou le profil actif pour un admin).
  // `status` combine la fraîcheur du dernier rapport ET son propre statut envoyé par le
  // mod — "offline" dès que plus rien n'arrive depuis 90s (crash : aucun rapport
  // "stopping" n'a pu partir), sinon le statut littéral envoyé ("starting"/"online"/
  // "stopping"). `online` reste exposé en plus, dérivé de `status`, pour un launcher qui
  // n'a besoin que du booléen (liste de joueurs, par ex.).
  app.get("/modpacks/:slug/online-players", async (req, reply) => {
    const { slug } = req.params as { slug: string };

    const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
    if (!modpack) {
      return reply.code(404).send({ error: "Modpack not found" });
    }

    const report = reportsBySlug.get(slug);
    const fresh = report !== undefined && Date.now() - report.reportedAt < STALE_AFTER_MS;
    const status: ReportedStatus | "offline" = fresh ? report.status : "offline";
    const online = status === "online";

    // Même jointure que GET /modpacks/:slug/player-stats : le compte Fedoheim lié à ce
    // nom de perso (voir linkCharacterName), pour afficher l'avatar Discord à côté du
    // joueur sur la page d'accueil — `null` pour un perso jamais lié.
    const players = (online ? (report?.players ?? []) : []).map((player) => {
      const linkedUser = db.select().from(users).where(eq(users.characterName, player.name)).get();
      return {
        name: player.name,
        biome: player.biome,
        armor: player.armor,
        discordUsername: linkedUser?.discordUsername ?? null,
        discordAvatar: linkedUser ? discordAvatarUrl(linkedUser) : null,
      };
    });

    return reply.send({
      status,
      online,
      players,
      // Même principe que `players` : la dernière saison connue reste dans le rapport
      // le temps que `status` retombe à `offline` (jusqu'à 90s après un arrêt), mais
      // n'a plus de sens à afficher dès que le serveur n'est plus vraiment "online" --
      // sinon le launcher continue de montrer une saison alors que le jeu est fermé.
      season: online ? (report?.season ?? null) : null,
      // Même principe que `season` ci-dessus : n'a plus de sens à afficher une fois le
      // serveur retombé hors "online".
      time: online ? (report?.time ?? null) : null,
      updatedAt: report ? new Date(report.reportedAt).toISOString() : null,
    });
  });

  // Public, même principe que GET /modpacks/:slug/online-players ci-dessus (page
  // "Joueurs" du launcher) -- mais renvoie TOUS les joueurs déjà vus sur ce profil, pas
  // seulement ceux actuellement connectés : `online` est calculé en croisant avec le
  // rapport en mémoire (même fraîcheur que ci-dessus), `biome`/`armor`/`lastSeenAt`
  // viennent eux de player_stats et restent affichés même une fois le joueur déconnecté
  // (dernières valeurs connues, pas remises à null).
  app.get("/modpacks/:slug/player-stats", async (req, reply) => {
    const { slug } = req.params as { slug: string };

    const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
    if (!modpack) {
      return reply.code(404).send({ error: "Modpack not found" });
    }

    const report = reportsBySlug.get(slug);
    const fresh = report !== undefined && Date.now() - report.reportedAt < STALE_AFTER_MS;
    const online = fresh && report?.status === "online";
    const onlineNames = new Set(online ? (report?.players ?? []).map((p) => p.name) : []);

    const rows = db.select().from(playerStats).where(eq(playerStats.modpackId, modpack.id)).all();

    // Compte Fedoheim lié à ce nom de perso (voir linkCharacterName ci-dessus) — `null`
    // pour un perso vu avant l'existence de cette fonctionnalité, ou jamais lié (pas de
    // steamId rapporté). Requête par ligne : la table `player_stats` d'un profil ne
    // dépasse jamais quelques dizaines de joueurs, pas la peine d'un vrai join SQL.
    const players = rows
      .map((row) => {
        const linkedUser = db.select().from(users).where(eq(users.characterName, row.name)).get();
        return {
          name: row.name,
          biome: row.biome,
          armor: row.armor,
          online: onlineNames.has(row.name),
          lastSeenAt: row.lastSeenAt.toISOString(),
          discordUsername: linkedUser?.discordUsername ?? null,
          discordAvatar: linkedUser ? discordAvatarUrl(linkedUser) : null,
        };
      })
      // En ligne d'abord, puis par dernière activité décroissante -- l'ordre le plus
      // utile pour un admin qui veut voir qui joue en ce moment sans avoir à trier.
      .sort((a, b) => {
        if (a.online !== b.online) return a.online ? -1 : 1;
        return b.lastSeenAt.localeCompare(a.lastSeenAt);
      });

    return reply.send({ players });
  });
}
