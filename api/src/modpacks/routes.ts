import crypto from "node:crypto";
import type { FastifyInstance } from "fastify";
import { and, eq, sql } from "drizzle-orm";
import { z } from "zod";
import { db } from "../db/client.js";
import { modpacks, mods, configFiles } from "../db/schema.js";

// Même format que le slug "default" déjà en place — lettres/chiffres/tirets, pas
// d'espace ni de majuscule (utilisé tel quel dans des URLs et par le launcher).
const slugSchema = z
  .string()
  .trim()
  .toLowerCase()
  .min(1)
  .max(50)
  .regex(/^[a-z0-9]+(-[a-z0-9]+)*$/, "Slug invalide (lettres, chiffres et tirets uniquement)");

const createModpackSchema = z.object({
  slug: slugSchema,
  name: z.string().trim().min(1),
});

// Couleur du profil dans le launcher (badge de playbar, page Profils) — jamais utilisée
// pour le profil production, voir schema.ts::modpacks.color.
const hexColorSchema = z
  .string()
  .trim()
  .regex(/^#[0-9a-f]{6}$/i, "Couleur invalide (format hexadécimal #rrggbb)");

// Cible de connexion automatique du profil (voir FedoServerTools) — un monde local à
// héberger, ou un serveur dédié à rejoindre. `null` explicite pour désactiver la
// fonctionnalité sur ce profil (retour au menu Valheim normal, voir
// schema.ts::modpacks.autoConnectType).
const autoConnectSchema = z
  .discriminatedUnion("type", [
    z.object({ type: z.literal("world"), world: z.string().trim().min(1) }),
    z.object({
      type: z.literal("server"),
      host: z.string().trim().min(1),
      port: z.number().int().min(1).max(65535),
      // Pas un vrai secret (voir CLAUDE.md) : doit de toute façon atteindre le client
      // de chaque joueur pour que l'auto-connexion fonctionne. Vide = pas de mot de
      // passe sur ce serveur.
      password: z.string().trim().default(""),
    }),
  ])
  .nullable();

const updateModpackSchema = z
  .object({
    name: z.string().trim().min(1).optional(),
    // `null` explicite pour réinitialiser (retour à l'apparence par défaut) —
    // distinct d'absent (champ non touché par cette requête).
    color: hexColorSchema.nullable().optional(),
    autoConnect: autoConnectSchema.optional(),
  })
  .refine(
    (data) => data.name !== undefined || data.color !== undefined || data.autoConnect !== undefined,
    { message: "Nothing to update" },
  );

// Reconstruit la forme "objet" d'autoConnect (même shape que le body ci-dessus) à
// partir des colonnes à plat en base — utilisé pour l'affichage admin (GET /modpacks,
// formulaire d'édition) et pour ce que consomme réellement la partie client de
// FedoServerTools (voir GET /modpacks/:slug/manifest).
function resolveAutoConnect(modpack: typeof modpacks.$inferSelect) {
  if (modpack.autoConnectType === "world" && modpack.autoConnectWorld) {
    return { type: "world" as const, world: modpack.autoConnectWorld };
  }
  if (modpack.autoConnectType === "server" && modpack.autoConnectHost && modpack.autoConnectPort) {
    return {
      type: "server" as const,
      host: modpack.autoConnectHost,
      port: modpack.autoConnectPort,
      password: modpack.autoConnectPassword ?? "",
    };
  }
  return null;
}

// Une URL de téléchargement absolue (hébergement externe, ex: GitHub release) OU un
// chemin relatif "/uploads/..." renvoyé par POST /modpacks/files (voir files.ts) — le
// launcher résout ce second cas contre sa propre config d'URL d'API avant de
// télécharger (modpack.rs::resolve_url), pas besoin de PUBLIC_API_URL pour ça.
const downloadUrlSchema = z
  .string()
  .trim()
  .refine((v) => v.startsWith("/uploads/") || /^https?:\/\//.test(v), {
    message: "Must be an absolute URL or an /uploads/... path",
  });

const modBodySchema = z.object({
  name: z.string().trim().min(1),
  version: z.string().trim().min(1),
  downloadUrl: downloadUrlSchema,
  sha256: z
    .string()
    .trim()
    .regex(/^[0-9a-f]{64}$/i, "Invalid sha256"),
  description: z.string().trim().default(""),
  category: z.string().trim().min(1),
  // Dépendances Thunderstore ("Auteur-NomDuPackage-Version") détectées depuis le
  // manifest.json de l'archive — affichage/avertissement admin seulement.
  dependencies: z.array(z.string()).default([]),
  // icon.png de l'archive, uploadé séparément via POST /modpacks/icons — vide si
  // l'archive n'en avait pas.
  iconUrl: z.string().trim().default(""),
  // Coché par un admin dans l'éditeur — voir schema.ts::mods.adminOnly.
  adminOnly: z.boolean().default(false),
  // Décoché par un admin pour désactiver ce mod pour tout le monde — voir
  // schema.ts::mods.enabled.
  enabled: z.boolean().default(true),
});

const modsBodySchema = z.object({
  mods: z.array(modBodySchema).max(200),
});

const manifestQuerySchema = z.object({
  mode: z.enum(["player", "admin"]).default("player"),
});

const bepinexBodySchema = z.object({
  url: downloadUrlSchema,
  sha256: z
    .string()
    .trim()
    .regex(/^[0-9a-f]{64}$/i, "Invalid sha256"),
  // Détectés côté launcher depuis le manifest.json Thunderstore de l'archive — vides si
  // l'archive n'en a pas (affichage seulement, jamais utilisés pour l'installation).
  version: z.string().trim().default(""),
  description: z.string().trim().default(""),
  iconUrl: z.string().trim().default(""),
});

export default async function modpackRoutes(app: FastifyInstance) {
  // Liste des profils de modpack (production + profils de test créés par un admin
  // pour valider un modpack avant de le répliquer en production — voir CLAUDE.md).
  // Réservé aux admins : un joueur normal n'a jamais besoin de voir autre chose que le
  // profil production, fixé côté launcher.
  app.get(
    "/modpacks",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (_req, reply) => {
      const list = db.select().from(modpacks).all();
      const counts = db
        .select({ modpackId: mods.modpackId, count: sql<number>`count(*)` })
        .from(mods)
        .groupBy(mods.modpackId)
        .all();
      const countByModpackId = new Map(counts.map((c) => [c.modpackId, c.count]));

      return reply.send(
        list.map((m) => ({
          slug: m.slug,
          name: m.name,
          version: m.version,
          isDefault: m.isDefault,
          color: m.color,
          autoConnect: resolveAutoConnect(m),
          hasReportToken: Boolean(m.reportToken),
          modCount: countByModpackId.get(m.id) ?? 0,
          updatedAt: m.updatedAt,
        })),
      );
    },
  );

  // Crée un nouveau profil de modpack, vide (pas de mods, pas de BepInEx configuré) —
  // jamais marqué production (voir schema.ts::modpacks.isDefault), un admin doit
  // explicitement configurer et remplir ce profil comme n'importe quel autre.
  app.post(
    "/modpacks",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const parsed = createModpackSchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      const existing = db
        .select()
        .from(modpacks)
        .where(eq(modpacks.slug, parsed.data.slug))
        .get();
      if (existing) {
        return reply.code(409).send({ error: "A modpack with this slug already exists" });
      }

      const now = new Date();
      const created = db
        .insert(modpacks)
        .values({
          slug: parsed.data.slug,
          name: parsed.data.name,
          version: "1.0.0",
          isDefault: false,
          updatedAt: now,
        })
        .returning()
        .get();

      return reply.code(201).send({
        slug: created.slug,
        name: created.name,
        version: created.version,
        isDefault: created.isDefault,
        color: created.color,
        autoConnect: resolveAutoConnect(created),
        modCount: 0,
        updatedAt: created.updatedAt,
      });
    },
  );

  // Met à jour le nom et/ou la couleur d'un profil (le slug, lui, ne change jamais une
  // fois créé — il est référencé tel quel par les installs locales des joueurs/
  // admins). Les deux champs sont indépendants : le launcher appelle cette route soit
  // pour renommer, soit pour changer la couleur, jamais forcément les deux à la fois.
  app.patch(
    "/modpacks/:slug",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };
      const parsed = updateModpackSchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }

      const update: {
        name?: string;
        color?: string | null;
        autoConnectType?: "world" | "server" | null;
        autoConnectWorld?: string | null;
        autoConnectHost?: string | null;
        autoConnectPort?: number | null;
        autoConnectPassword?: string | null;
        updatedAt: Date;
      } = {
        updatedAt: new Date(),
      };
      if (parsed.data.name !== undefined) update.name = parsed.data.name;
      if (parsed.data.color !== undefined) update.color = parsed.data.color;
      if (parsed.data.autoConnect !== undefined) {
        const autoConnect = parsed.data.autoConnect;
        if (autoConnect === null) {
          update.autoConnectType = null;
          update.autoConnectWorld = null;
          update.autoConnectHost = null;
          update.autoConnectPort = null;
          update.autoConnectPassword = null;
        } else if (autoConnect.type === "world") {
          update.autoConnectType = "world";
          update.autoConnectWorld = autoConnect.world;
          update.autoConnectHost = null;
          update.autoConnectPort = null;
          update.autoConnectPassword = null;
        } else {
          update.autoConnectType = "server";
          update.autoConnectWorld = null;
          update.autoConnectHost = autoConnect.host;
          update.autoConnectPort = autoConnect.port;
          update.autoConnectPassword = autoConnect.password || null;
        }
      }

      db.update(modpacks).set(update).where(eq(modpacks.id, modpack.id)).run();

      return reply.send({ ok: true });
    },
  );

  // Supprime un profil de test et tous ses mods — jamais le profil production
  // (isDefault), pour qu'une suppression malencontreuse depuis l'éditeur de profils ne
  // puisse jamais casser l'expérience des joueurs normaux.
  app.delete(
    "/modpacks/:slug",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };

      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }
      if (modpack.isDefault) {
        return reply.code(400).send({ error: "Cannot delete the production modpack" });
      }

      db.transaction((tx) => {
        tx.delete(mods).where(eq(mods.modpackId, modpack.id)).run();
        tx.delete(modpacks).where(eq(modpacks.id, modpack.id)).run();
      });

      return reply.send({ ok: true });
    },
  );

  // Liste publique des mods pour affichage (page "Mods" du launcher) : pas de
  // downloadUrl/sha256 ici, contrairement au manifest ci-dessous qui est protégé. Les
  // mods "admin only" ou désactivés en sont toujours absents, y compris pour un admin
  // qui consulte cette liste hors édition — invisibles ici, gérés via GET /mods/full.
  app.get("/modpacks/:slug/mods", async (req, reply) => {
    const { slug } = req.params as { slug: string };

    const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
    if (!modpack) {
      return reply.code(404).send({ error: "Modpack not found" });
    }

    const modList = db
      .select()
      .from(mods)
      .where(and(eq(mods.modpackId, modpack.id), eq(mods.adminOnly, false), eq(mods.enabled, true)))
      .all();

    return reply.send(
      modList.map((m) => ({
        name: m.name,
        version: m.version,
        description: m.description,
        category: m.category,
        iconUrl: m.iconUrl,
      })),
    );
  });

  // Liste complète (avec downloadUrl/sha256, l'archive zip du mod) pour l'écran
  // d'édition admin — distincte de la liste publique ci-dessus, qui ne sert qu'à
  // l'affichage joueur.
  app.get(
    "/modpacks/:slug/mods/full",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };

      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }

      const modList = db.select().from(mods).where(eq(mods.modpackId, modpack.id)).all();

      return reply.send(
        modList.map((m) => ({
          name: m.name,
          version: m.version,
          downloadUrl: m.downloadUrl,
          sha256: m.sha256,
          description: m.description,
          category: m.category,
          dependencies: m.dependencies,
          iconUrl: m.iconUrl,
          adminOnly: m.adminOnly,
          enabled: m.enabled,
          createdAt: m.createdAt,
          updatedAt: m.updatedAt,
        })),
      );
    },
  );

  // Remplace entièrement la liste des mods du modpack (nom, version, chemin
  // d'installation, URL de téléchargement, checksum, description, catégorie).
  // Réservé aux admins — c'est aussi ce qui alimente le manifest ci-dessous.
  app.put(
    "/modpacks/:slug/mods",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };

      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }

      const parsed = modsBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      // La liste est remplacée en bloc à chaque sauvegarde (pas de diff par id) —
      // createdAt/updatedAt sont donc recalculés ici en comparant à l'état précédent,
      // matché par nom (unique en pratique, voir le key={mod.name} déjà utilisé côté
      // affichage joueur). Jamais accepté depuis le body client (voir modBodySchema).
      const existingByName = new Map(
        db
          .select()
          .from(mods)
          .where(eq(mods.modpackId, modpack.id))
          .all()
          .map((m) => [m.name, m]),
      );
      const now = new Date();

      db.transaction((tx) => {
        tx.delete(mods).where(eq(mods.modpackId, modpack.id)).run();
        if (parsed.data.mods.length > 0) {
          tx.insert(mods)
            .values(
              parsed.data.mods.map((m) => {
                const prev = existingByName.get(m.name);
                const filesChanged =
                  !prev || prev.downloadUrl !== m.downloadUrl || prev.sha256 !== m.sha256;
                return {
                  ...m,
                  modpackId: modpack.id,
                  createdAt: prev?.createdAt ?? now,
                  updatedAt: filesChanged ? now : (prev?.updatedAt ?? now),
                };
              }),
            )
            .run();
        }
        tx.update(modpacks).set({ updatedAt: now }).where(eq(modpacks.id, modpack.id)).run();
      });

      return reply.send({ ok: true });
    },
  );

  // Manifest consommé par le launcher pour savoir quoi télécharger/mettre à jour.
  // `bepinex` est `null` tant qu'un admin n'a pas configuré le package (voir routes
  // ci-dessous) — le launcher refuse alors de lancer le jeu plutôt que de planter.
  //
  // `?mode=player|admin` (défaut "player") détermine si les mods "admin only" sont
  // inclus. Le mode "player" est celui de tout le monde ; le mode "admin" revérifie le
  // rôle admin en direct sur Discord (comme `requireAdmin`, pas seulement le JWT) avant
  // de servir la liste complète — sinon changer ce paramètre d'URL suffirait à
  // récupérer les mods admin sans avoir le rôle.
  app.get(
    "/modpacks/:slug/manifest",
    { preHandler: [app.requireAuth, app.requireOnboarded] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };
      const parsedQuery = manifestQuerySchema.safeParse(req.query);
      if (!parsedQuery.success) {
        return reply.code(400).send({ error: parsedQuery.error.flatten() });
      }
      const { mode } = parsedQuery.data;

      if (mode === "admin") {
        await app.requireAdmin(req, reply);
        if (reply.sent) return;
      }

      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }

      // `enabled` filtre toujours, quel que soit `mode` -- un mod désactivé ne doit être
      // installé par personne, joueur comme admin (contrairement à `adminOnly`, qui ne
      // filtre qu'en mode joueur).
      const modList = db
        .select()
        .from(mods)
        .where(
          mode === "admin"
            ? and(eq(mods.modpackId, modpack.id), eq(mods.enabled, true))
            : and(eq(mods.modpackId, modpack.id), eq(mods.adminOnly, false), eq(mods.enabled, true)),
        )
        .all();

      // Fichiers de config bruts (voir configFiles.ts) — jamais filtrés par mode, pas de
      // notion "admin only" ici, contrairement aux mods.
      const configFileList = db
        .select()
        .from(configFiles)
        .where(eq(configFiles.modpackId, modpack.id))
        .all();

      return reply.send({
        slug: modpack.slug,
        name: modpack.name,
        version: modpack.version,
        bepinex:
          modpack.bepinexUrl && modpack.bepinexSha256
            ? {
                downloadUrl: modpack.bepinexUrl,
                sha256: modpack.bepinexSha256,
                version: modpack.bepinexVersion ?? "",
              }
            : null,
        mods: modList.map((m) => ({
          name: m.name,
          version: m.version,
          downloadUrl: m.downloadUrl,
          sha256: m.sha256,
        })),
        configFiles: configFileList.map((f) => ({
          filename: f.filename,
          downloadUrl: f.downloadUrl,
          sha256: f.sha256,
        })),
        // Cible de connexion automatique pour la partie client de FedoServerTools —
        // `null` = profil non configuré, comportement vanilla inchangé (kill-switch,
        // voir CLAUDE.md).
        // Même endpoint que celui déjà appelé par le launcher juste avant de lancer le
        // jeu : pas besoin d'une route publique séparée pour qu'un joueur normal (pas
        // seulement un admin) puisse lire la cible de son profil actif.
        autoConnect: resolveAutoConnect(modpack),
      });
    },
  );

  // Statut public de BepInEx (configuré ou non, version) — BepInEx est un mod comme un
  // autre du point de vue du joueur, il doit pouvoir le voir dans la liste sans être
  // admin. Pas d'URL/sha256 ici, contrairement à la version admin ci-dessous (même
  // principe que GET /modpacks/:slug/mods vs mods/full).
  app.get("/modpacks/:slug/bepinex/status", async (req, reply) => {
    const { slug } = req.params as { slug: string };

    const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
    if (!modpack) {
      return reply.code(404).send({ error: "Modpack not found" });
    }

    const configured = Boolean(modpack.bepinexUrl && modpack.bepinexSha256);
    return reply.send({
      configured,
      version: configured ? (modpack.bepinexVersion ?? "") : null,
      description: configured ? modpack.bepinexDescription : null,
      iconUrl: configured ? modpack.bepinexIconUrl : null,
    });
  });

  // Config BepInEx du modpack (package officiel BepInExPack_Valheim dézippé, uploadé
  // via POST /modpacks/files) — pas une entrée de `mods`, c'est un réglage du modpack
  // lui-même. Réservé aux admins, comme le reste de l'édition.
  app.get(
    "/modpacks/:slug/bepinex",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };

      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }

      if (!modpack.bepinexUrl || !modpack.bepinexSha256) {
        return reply.send(null);
      }

      return reply.send({
        url: modpack.bepinexUrl,
        sha256: modpack.bepinexSha256,
        version: modpack.bepinexVersion ?? "",
        description: modpack.bepinexDescription,
        iconUrl: modpack.bepinexIconUrl,
      });
    },
  );

  app.put(
    "/modpacks/:slug/bepinex",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };

      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }

      const parsed = bepinexBodySchema.safeParse(req.body);
      if (!parsed.success) {
        return reply.code(400).send({ error: parsed.error.flatten() });
      }

      db.update(modpacks)
        .set({
          bepinexUrl: parsed.data.url,
          bepinexSha256: parsed.data.sha256,
          bepinexVersion: parsed.data.version || null,
          bepinexDescription: parsed.data.description,
          bepinexIconUrl: parsed.data.iconUrl,
          updatedAt: new Date(),
        })
        .where(eq(modpacks.id, modpack.id))
        .run();

      return reply.send({ ok: true });
    },
  );

  // Jeton partagé donné au mod serveur FedoServerTools (voir mods/FedoServerTools) pour
  // qu'il puisse poster qui est en ligne sur ce profil précis — voir onlinePlayers.ts.
  // Révélé tel quel à un admin qui doit le recopier dans le .cfg du mod ; pas exposé
  // dans GET /modpacks (seulement `hasReportToken`, voir ci-dessus).
  app.get(
    "/modpacks/:slug/report-token",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };
      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }
      return reply.send({ reportToken: modpack.reportToken });
    },
  );

  // Génère un nouveau jeton (et invalide l'ancien, s'il existait) — à recopier dans le
  // `.cfg` de FedoServerTools sur le serveur Valheim de ce profil.
  app.post(
    "/modpacks/:slug/report-token/regenerate",
    { preHandler: [app.requireAuth, app.requireAdmin] },
    async (req, reply) => {
      const { slug } = req.params as { slug: string };
      const modpack = db.select().from(modpacks).where(eq(modpacks.slug, slug)).get();
      if (!modpack) {
        return reply.code(404).send({ error: "Modpack not found" });
      }

      const reportToken = crypto.randomBytes(24).toString("hex");
      db.update(modpacks)
        .set({ reportToken, updatedAt: new Date() })
        .where(eq(modpacks.id, modpack.id))
        .run();

      return reply.send({ reportToken });
    },
  );
}
