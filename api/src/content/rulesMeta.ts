import { eq } from "drizzle-orm";
import { db } from "../db/client.js";
import { rulesMeta } from "../db/schema.js";

const SINGLETON_ID = 1;

export function getRulesUpdatedAt(): Date | null {
  const meta = db.select().from(rulesMeta).where(eq(rulesMeta.id, SINGLETON_ID)).get();
  return meta?.updatedAt ?? null;
}
