import "dotenv/config";
import { z } from "zod";

// Une variable optionnelle laissée vide dans .env (ex: `PUBLIC_API_URL=`) arrive comme
// une chaîne vide, pas comme `undefined` — sans ça, `.optional()` ne suffit pas à la
// rendre facultative pour des validateurs comme `.url()` ou `.min(1)`.
const emptyToUndefined = (val: unknown) => (val === "" ? undefined : val);

const envSchema = z.object({
  PORT: z.coerce.number().default(3000),
  HOST: z.string().default("0.0.0.0"),
  NODE_ENV: z.enum(["development", "production", "test"]).default("development"),

  // Par défaut, chemins relatifs au cwd du process (comme en dev, lancé depuis api/) —
  // en Docker, pointés vers un volume monté (ex: /data/data.sqlite) pour survivre au
  // remplacement du conteneur, voir docker-compose.yml.
  DB_PATH: z.string().default("data.sqlite"),
  UPLOADS_DIR: z.string().default("uploads"),

  JWT_SECRET: z
    .string()
    .min(16, "JWT_SECRET must be at least 16 characters")
    .refine(
      (val) => val !== "change-me-to-a-long-random-string",
      "JWT_SECRET is still set to the .env.example placeholder — generate a real random secret",
    ),

  DISCORD_CLIENT_ID: z.string().min(1),
  DISCORD_CLIENT_SECRET: z.string().min(1),

  DISCORD_BOT_TOKEN: z.string().min(1),
  DISCORD_GUILD_ID: z.string().min(1),
  DISCORD_REQUIRED_ROLE_ID: z.string().min(1),
  // Rôle Discord donnant les droits admin dans le launcher.
  DISCORD_ADMIN_ROLE_ID: z.string().min(1),

  // Optionnels : tant qu'ils ne sont pas configurés, les annonces ne sont pas
  // repostées sur Discord (voir announcements/discord.ts) — le reste de l'API
  // fonctionne normalement sans eux.
  // Salon Discord (verrouillé en écriture pour les joueurs) où poster les annonces.
  DISCORD_ANNOUNCEMENT_CHANNEL_ID: z.preprocess(emptyToUndefined, z.string().min(1).optional()),
  // URL publique de cette API (pas 127.0.0.1), pour que Discord puisse aller chercher
  // les images d'annonces et les intégrer dans le message.
  PUBLIC_API_URL: z.preprocess(emptyToUndefined, z.string().url().optional()),
  // Salon Discord où afficher le règlement (édité en place à chaque changement,
  // voir content/discord.ts). Sans lui, le règlement ne vit que dans le launcher.
  DISCORD_RULES_CHANNEL_ID: z.preprocess(emptyToUndefined, z.string().min(1).optional()),
  // Salon Discord où sont envoyés les LogOutput.log des joueurs (voir support/discord.ts
  // et le bouton "Envoyer log" du launcher). Sans lui, la route renvoie une erreur claire
  // plutôt que de prétendre avoir envoyé un log qui n'est parti nulle part.
  DISCORD_LOG_CHANNEL_ID: z.preprocess(emptyToUndefined, z.string().min(1).optional()),
});

const parsed = envSchema.safeParse(process.env);

if (!parsed.success) {
  console.error("Invalid environment configuration:");
  console.error(parsed.error.format());
  process.exit(1);
}

export const config = parsed.data;
