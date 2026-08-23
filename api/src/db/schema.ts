import { sqliteTable, text, integer, uniqueIndex } from "drizzle-orm/sqlite-core";

export const users = sqliteTable("users", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  discordId: text("discord_id").notNull().unique(),
  discordUsername: text("discord_username").notNull(),
  discordAvatar: text("discord_avatar"),
  // Recalculé à chaque login à partir des rôles Discord du joueur.
  isAdmin: integer("is_admin", { mode: "boolean" }).notNull().default(false),
  // Géré manuellement par un admin (voir PATCH /admin/users/:discordId/ban), jamais
  // recalculé depuis Discord — un joueur banni reste banni même s'il garde son rôle.
  isBanned: integer("is_banned", { mode: "boolean" }).notNull().default(false),
  // null = pas encore validé. Rempli une fois via POST /auth/accept-rules.
  rulesAcceptedAt: integer("rules_accepted_at", { mode: "timestamp" }),
  // Rempli une fois via POST /auth/steam-id, après validation du format SteamID64.
  steamId: text("steam_id"),
  createdAt: integer("created_at", { mode: "timestamp" }).notNull(),
  lastLoginAt: integer("last_login_at", { mode: "timestamp" }).notNull(),
});

export const modpacks = sqliteTable("modpacks", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  slug: text("slug").notNull().unique(),
  name: text("name").notNull(),
  version: text("version").notNull(),
  // Archive du package BepInEx (structure officielle BepInExPack_Valheim dézippée),
  // extraite par le launcher dans un dossier profil externe — voir modpack.rs. Nullable
  // tant qu'un admin ne l'a pas configuré ; le launcher refuse alors de lancer le jeu.
  bepinexUrl: text("bepinex_url"),
  bepinexSha256: text("bepinex_sha256"),
  // Détectées depuis le manifest.json Thunderstore de l'archive au moment de l'upload
  // (voir modpack.rs::read_manifest_info) — affichage seulement (BepInEx est un mod
  // comme un autre du point de vue du joueur), jamais utilisées pour l'installation.
  bepinexVersion: text("bepinex_version"),
  bepinexDescription: text("bepinex_description").notNull().default(""),
  bepinexIconUrl: text("bepinex_icon_url").notNull().default(""),
  // Le modpack "production", celui que tout joueur normal reçoit (slug fixe côté
  // launcher, voir PRODUCTION_MODPACK_SLUG) — protégé de la suppression (voir
  // routes.ts::DELETE /modpacks/:slug). Les autres modpacks sont des profils de test
  // créés librement par un admin pour valider un modpack avant de le répliquer en
  // production, jamais servis à un joueur normal.
  isDefault: integer("is_default", { mode: "boolean" }).notNull().default(false),
  // Couleur choisie par un admin pour distinguer ce profil dans le launcher (badge de
  // la playbar, page Profils) — hex "#rrggbb", `null` tant qu'aucune n'a été choisie.
  // Ignorée pour le profil production (isDefault), qui garde toujours l'apparence
  // standard — le sélecteur de couleur n'est de toute façon jamais proposé pour lui.
  color: text("color"),
  // Secret partagé avec le mod serveur FedoServerTools (header `x-server-token` sur
  // POST /modpacks/:slug/online-players), généré/régénéré par un admin depuis la page
  // Profils — voir modpacks/routes.ts. `null` tant qu'aucun n'a été généré : le serveur
  // Valheim de ce profil n'a alors aucun moyen de poster qui est en ligne.
  reportToken: text("report_token"),
  updatedAt: integer("updated_at", { mode: "timestamp" }).notNull(),
});

export const mods = sqliteTable("mods", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  modpackId: integer("modpack_id")
    .notNull()
    .references(() => modpacks.id),
  name: text("name").notNull(),
  version: text("version").notNull(),
  // Archive zip du mod (dll + cfg + tout ce qu'il faut), extraite par le launcher dans
  // un sous-dossier dédié de BepInEx/plugins — voir launcher/src-tauri/src/modpack.rs.
  downloadUrl: text("download_url").notNull(),
  sha256: text("sha256").notNull(),
  // Affichage côté launcher (page "Mods"), pas utilisé pour l'installation.
  description: text("description").notNull().default(""),
  category: text("category").notNull().default("Gameplay"),
  // Dépendances Thunderstore ("Auteur-NomDuPackage-Version"), détectées depuis
  // manifest.json au moment de l'upload — sert uniquement à avertir l'admin dans
  // l'éditeur si une dépendance (mod ou BepInEx) n'est pas configurée dans le modpack,
  // jamais utilisé pour l'installation côté joueur.
  dependencies: text("dependencies", { mode: "json" }).$type<string[]>().notNull().default([]),
  // Icône du mod (icon.png, quasi systématique dans un package Thunderstore), détectée
  // et uploadée au moment de l'upload du zip — affichage seulement, jamais vide dans le
  // sens où une valeur vide signifie simplement "pas d'icône détectée".
  iconUrl: text("icon_url").notNull().default(""),
  // Mod réservé au modpack "Admin" (voir GET /modpacks/:slug/manifest?mode=), invisible
  // des joueurs normaux : absent de la liste publique (GET /mods) et du manifest en mode
  // "player". Coché par un admin dans l'éditeur, jamais déduit automatiquement.
  adminOnly: integer("admin_only", { mode: "boolean" }).notNull().default(false),
  // Coché par un admin pour désactiver ce mod pour tout le monde (joueur comme admin)
  // sans perdre sa fiche (description, catégorie, dépendances, zip déjà importé) —
  // absent du manifest (voir routes.ts::GET /modpacks/:slug/manifest, filtré quel que
  // soit `mode`) et de la liste publique tant qu'il est décoché, mais toujours visible
  // dans l'éditeur admin pour pouvoir le réactiver.
  enabled: integer("enabled", { mode: "boolean" }).notNull().default(true),
  // Gérés par l'API, jamais par le client (voir routes.ts) : createdAt est préservé
  // d'une sauvegarde à l'autre (match par nom, la liste étant remplacée en bloc à
  // chaque PUT) ; updatedAt ne bouge que si downloadUrl/sha256 changent réellement,
  // pas sur une simple modif de description/catégorie — "date du dernier zip importé".
  createdAt: integer("created_at", { mode: "timestamp" }),
  updatedAt: integer("updated_at", { mode: "timestamp" }),
});

// Fichier de config brut envoyé par un admin indépendamment de tout mod (ex:
// FastLink.cfg pré-rempli avec l'adresse/mot de passe du serveur) — copié tel quel par
// le launcher dans BepInEx/config/ (voir modpack.rs::sync_config_files), pas extrait
// d'un zip. `filename` fait autorité pour le nom de destination, doit donc être unique
// par modpack (imposé à l'enregistrement, voir modpacks/configFiles.ts) — deux fichiers
// de même nom entreraient en collision au même chemin.
export const configFiles = sqliteTable("config_files", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  modpackId: integer("modpack_id")
    .notNull()
    .references(() => modpacks.id),
  filename: text("filename").notNull(),
  downloadUrl: text("download_url").notNull(),
  sha256: text("sha256").notNull(),
  updatedAt: integer("updated_at", { mode: "timestamp" }).notNull(),
});

// Dernier état connu de chaque joueur ayant déjà été rapporté par FedoServerTools pour
// ce profil de modpack (voir modpacks/onlinePlayers.ts) — contrairement au rapport en
// mémoire (`reportsBySlug`, périmé après 90s), une ligne ici survit à une déconnexion ou
// à un redémarrage de l'API : c'est un historique "dernier biome/armure vus", pas "en
// ligne maintenant" (dérivé séparément en croisant avec le rapport en mémoire). `name`
// est le pseudo Valheim tel que rapporté par le mod, pas un compte Discord — ce mod n'a
// aucune notion d'identité joueur (voir CLAUDE.md).
export const playerStats = sqliteTable(
  "player_stats",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    modpackId: integer("modpack_id")
      .notNull()
      .references(() => modpacks.id),
    name: text("name").notNull(),
    biome: text("biome"),
    armor: integer("armor"),
    lastSeenAt: integer("last_seen_at", { mode: "timestamp" }).notNull(),
  },
  (table) => [uniqueIndex("player_stats_modpack_name_idx").on(table.modpackId, table.name)],
);

export const rules = sqliteTable("rules", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  text: text("text").notNull(),
  sortOrder: integer("sort_order").notNull().default(0),
});

// Ligne unique (id=1) qui trace la dernière modification du règlement par un admin.
// Sert à savoir si l'acceptation d'un joueur (users.rulesAcceptedAt) est encore
// valable ou si le règlement a changé depuis — voir content/rulesMeta.ts.
export const rulesMeta = sqliteTable("rules_meta", {
  id: integer("id").primaryKey(),
  updatedAt: integer("updated_at", { mode: "timestamp" }).notNull(),
  // ID du message Discord affichant le règlement (édité en place à chaque changement,
  // voir content/discord.ts). null si DISCORD_RULES_CHANNEL_ID n'est pas configuré.
  discordMessageId: text("discord_message_id"),
});

export const faqEntries = sqliteTable("faq_entries", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  question: text("question").notNull(),
  answer: text("answer").notNull(),
  sortOrder: integer("sort_order").notNull().default(0),
});

export const announcements = sqliteTable("announcements", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  // Dérivé du compte admin authentifié qui poste, jamais saisi côté client.
  author: text("author").notNull(),
  title: text("title"),
  // Markdown façon Discord (**gras**, *italique*, __souligné__, ~~barré~~, ||spoiler||)
  // — même syntaxe que celle attendue par l'API Discord, pour pouvoir un jour poster
  // ce texte tel quel dans un salon du serveur via le bot.
  message: text("message").notNull(),
  // URLs d'images hébergées par l'API (voir /announcements/images), stockées en JSON.
  images: text("images", { mode: "json" }).$type<string[]>().notNull().default([]),
  createdAt: integer("created_at", { mode: "timestamp" }).notNull(),
  updatedAt: integer("updated_at", { mode: "timestamp" }),
  // ID du message posté dans le salon Discord dédié (voir announcements/discord.ts).
  // null si DISCORD_ANNOUNCEMENT_CHANNEL_ID n'est pas configuré ou si le post a échoué.
  discordMessageId: text("discord_message_id"),
});

// Ligne unique (id=1) pour des réglages simples éditables par un admin, sans passer
// par une variable d'env ni un redéploiement (voir settings/routes.ts).
export const settings = sqliteTable("settings", {
  id: integer("id").primaryKey(),
  buyMeACoffeeUrl: text("buy_me_a_coffee_url").notNull().default("https://buymeacoffee.com/fedoheim"),
  // Textes de l'accueil du launcher (au-dessus du nom du serveur / sous le titre).
  heroEyebrow: text("hero_eyebrow").notNull().default("Serveur communautaire"),
  heroTagline: text("hero_tagline").notNull().default("Le feu brûle, les portes sont ouvertes."),
});
