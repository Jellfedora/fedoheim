import { db } from "./client.js";
import { modpacks, mods, rules, faqEntries, announcements } from "./schema.js";

// Données de départ pour dev/test, correspondant à ce qui était en mock côté launcher.
// À lancer avec: npx tsx src/db/seed.ts

const now = new Date();

const modpack = db
  .insert(modpacks)
  .values({
    slug: "default",
    name: "Fedoheim Modpack",
    version: "1.0.0",
    bepinexUrl: "https://example.com/bepinex/BepInExPack_Valheim-5.4.2100.zip",
    bepinexSha256: "0".repeat(64),
    bepinexVersion: "5.4.2100",
    isDefault: true,
    updatedAt: now,
  })
  .returning()
  .get();

db.insert(mods)
  .values([
    {
      modpackId: modpack.id,
      name: "Epic Loot",
      version: "0.9.14",
      downloadUrl: "https://example.com/mods/epic-loot-0.9.14.zip",
      sha256: "0".repeat(64),
      description: "Ajoute du butin légendaire, des affixes et des ensembles d'objets.",
      category: "Gameplay",
    },
    {
      modpackId: modpack.id,
      name: "Better Archery",
      version: "1.2.0",
      downloadUrl: "https://example.com/mods/better-archery-1.2.0.zip",
      sha256: "0".repeat(64),
      description: "Rééquilibre les arcs et flèches pour un tir plus nerveux.",
      category: "Gameplay",
    },
    {
      modpackId: modpack.id,
      name: "Craft From Containers",
      version: "2.1.3",
      downloadUrl: "https://example.com/mods/craft-from-containers-2.1.3.zip",
      sha256: "0".repeat(64),
      description: "Permet de crafter directement depuis les coffres proches.",
      category: "QoL",
    },
    {
      modpackId: modpack.id,
      name: "Auto Sort Containers",
      version: "1.0.7",
      downloadUrl: "https://example.com/mods/auto-sort-containers-1.0.7.zip",
      sha256: "0".repeat(64),
      description: "Trie automatiquement les coffres partagés de la base commune.",
      category: "QoL",
    },
    {
      modpackId: modpack.id,
      name: "First Person View",
      version: "3.4.1",
      downloadUrl: "https://example.com/mods/first-person-view-3.4.1.zip",
      sha256: "0".repeat(64),
      description: "Ajoute une caméra vue première personne, activable à la volée.",
      category: "Visuel",
    },
    {
      modpackId: modpack.id,
      name: "Fedoheim Server Tools",
      version: "1.0.0",
      downloadUrl: "https://example.com/mods/fedoheim-server-tools-1.0.0.zip",
      sha256: "0".repeat(64),
      description: "Mod maison : synchronisation des annonces et de l'état du serveur.",
      category: "Serveur",
    },
    {
      modpackId: modpack.id,
      name: "Server Devcommands",
      version: "1.9.0",
      downloadUrl: "https://example.com/mods/server-devcommands-1.9.0.zip",
      sha256: "0".repeat(64),
      description: "Commandes de modération et de debug réservées aux admins.",
      category: "Admin",
      adminOnly: true,
    },
  ])
  .run();

db.insert(rules)
  .values(
    [
      "Pas de raid ni de vol dans les coffres ou bases d'un autre joueur.",
      "Le PvP est interdit sauf accord explicite des deux joueurs concernés.",
      "Merci de construire à une distance raisonnable des bases existantes.",
      "Le modpack fourni par le launcher est obligatoire pour rejoindre le serveur.",
      "Un souci, un conflit ? Un message au Jarl sur Discord suffit.",
    ].map((text, i) => ({ text, sortOrder: i })),
  )
  .run();

db.insert(faqEntries)
  .values(
    [
      {
        question: "Pourquoi je n'arrive pas à me connecter ?",
        answer:
          "La connexion se fait via Discord : vérifie que tu as bien rejoint le serveur Discord " +
          "de la communauté et que tu possèdes le rôle autorisé. Sans ce rôle, l'accès est refusé.",
      },
      {
        question: "Le launcher me dit que mon modpack n'est pas à jour, que faire ?",
        answer:
          "Clique sur \"Mettre à jour\" dans la barre du bas : le launcher compare ta liste de mods " +
          "locale à celle du serveur et télécharge automatiquement ce qui manque ou a changé.",
      },
      {
        question: "Est-ce que je peux ajouter mes propres mods ?",
        answer:
          "Non, seuls les mods du modpack officiel sont autorisés pour garantir que tout le monde " +
          "joue avec la même configuration et éviter les incompatibilités ou les triches.",
      },
      {
        question: "Le launcher fonctionne-t-il sur Mac ?",
        answer:
          "Oui, une version macOS est prévue en plus de la version Windows. Certains mods peuvent " +
          "toutefois ne pas être disponibles sur Mac selon leur compatibilité.",
      },
      {
        question: "Le jeu ne se lance pas après avoir cliqué sur \"Jouer\", pourquoi ?",
        answer:
          "Assure-toi que Valheim est bien installé via Steam et que le launcher a pu détecter le " +
          "dossier d'installation. Si le problème persiste, contacte le Jarl sur Discord.",
      },
      {
        question: "J'ai un souci qui n'est pas listé ici, que faire ?",
        answer: "Envoie un message au Jarl sur Discord, il pourra t'aider directement.",
      },
    ].map((entry, i) => ({ ...entry, sortOrder: i })),
  )
  .run();

db.insert(announcements)
  .values({
    author: "Le Jarl",
    message:
      "Mise à jour du modpack ce soir : nouveaux biomes et rééquilibrage du loot. " +
      "Redémarrage du serveur prévu vers 20h, pensez à sauvegarder vos trajets en cours.",
    createdAt: now,
  })
  .run();

console.log("Seed OK");
