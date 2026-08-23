import "dotenv/config";
import type { Config } from "drizzle-kit";

export default {
  schema: "./src/db/schema.ts",
  out: "./drizzle",
  dialect: "sqlite",
  dbCredentials: {
    // Même variable que config.ts (DB_PATH) — sinon `db:migrate` appliquerait les
    // migrations à un fichier différent de celui réellement ouvert par l'API en Docker.
    url: process.env.DB_PATH ?? "./data.sqlite",
  },
} satisfies Config;
