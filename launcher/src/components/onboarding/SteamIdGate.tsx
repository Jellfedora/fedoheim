import { useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import type { UserInfo } from "../../types";
import "./Onboarding.css";

interface SteamIdGateProps {
  onSaved: (user: UserInfo) => void;
  onLogout: () => void;
}

// Même règle de format que côté API (voir api/src/auth/steam.ts) : validation
// syntaxique uniquement, pas d'appel à l'API Steam (nécessiterait une clé dédiée).
const STEAM_ID64_REGEX = /^7656119\d{10}$/;

export function SteamIdGate({ onSaved, onLogout }: SteamIdGateProps) {
  const [value, setValue] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const trimmed = value.trim();
  const isValidFormat = STEAM_ID64_REGEX.test(trimmed);

  async function handleSubmit() {
    if (!isValidFormat) return;
    setSubmitting(true);
    setError(null);
    try {
      const updated = await invoke<UserInfo>("set_steam_id", { steamId: trimmed });
      onSaved(updated);
    } catch (err) {
      setError(String(err));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="onboarding">
      <div className="onboarding__card">
        <p className="onboarding__eyebrow">Dernière étape</p>
        <h1>Ton identifiant Steam</h1>
        <p className="onboarding__intro">
          Renseigne ton SteamID64 pour t'autoriser sur le serveur — c'est ce qui nous
          permet de whitelister ton compte Steam avant de pouvoir télécharger le modpack
          et jouer.
        </p>

        <input
          className="onboarding__input"
          type="text"
          inputMode="numeric"
          placeholder="76561198000000000"
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
        {value.length > 0 && !isValidFormat && (
          <p className="onboarding__status is-error">
            Format invalide — un SteamID64 fait 17 chiffres et commence par 7656119.
          </p>
        )}
        {error && <p className="onboarding__status is-error">{error}</p>}

        <p className="onboarding__hint">
          Pour le trouver : sur Steam, clique sur ton pseudo en haut à droite, puis
          « Détails du compte » — il est affiché sous « Compte de &lt;ton pseudo&gt; ».
        </p>

        <div className="onboarding__actions">
          <button type="button" className="btn btn--ghost" onClick={onLogout} disabled={submitting}>
            Se déconnecter
          </button>
          <button
            type="button"
            className="btn btn--accent"
            onClick={handleSubmit}
            disabled={!isValidFormat || submitting}
          >
            {submitting ? "Enregistrement..." : "Continuer"}
          </button>
        </div>
      </div>
    </div>
  );
}
