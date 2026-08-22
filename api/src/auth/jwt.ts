import jwt from "jsonwebtoken";
import { config } from "../config.js";

export interface SessionPayload {
  userId: number;
  discordId: string;
  isAdmin: boolean;
}

// Longue durée : le launcher revalide le rôle Discord en tâche de fond via /auth/me,
// donc le JWT n'a pas besoin d'expirer vite pour rester sûr.
const EXPIRES_IN = "30d";

export function signSession(payload: SessionPayload): string {
  return jwt.sign(payload, config.JWT_SECRET, { expiresIn: EXPIRES_IN });
}

export function verifySession(token: string): SessionPayload {
  return jwt.verify(token, config.JWT_SECRET) as SessionPayload;
}
