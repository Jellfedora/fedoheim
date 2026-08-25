import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import "./ServerPage.css";

interface ServerPageProps {
  activeSlug: string;
}

// Même forme que OnlinePlayers dans HomePage.tsx (voir GET
// /modpacks/:slug/online-players) -- seuls status/season/time nous intéressent ici,
// pas la liste des joueurs.
type ServerStatus = "starting" | "online" | "stopping" | "offline";

interface OnlineStatus {
  status: ServerStatus;
  season: string | null;
  time: string | null;
}

const STATUS_LABELS: Record<ServerStatus, string> = {
  starting: "Démarrage en cours…",
  online: "En ligne",
  stopping: "Arrêt en cours…",
  offline: "Hors ligne",
};

// Même cadence que HomePage.tsx (10s, le minimum autorisé côté mod pour
// SyncIntervalSeconds) -- pas la peine d'un délai d'affichage supplémentaire.
const POLL_MS = 10_000;

const TIME_OPTIONS: Array<{ hour: 6 | 12 | 18 | 24; label: string }> = [
  { hour: 6, label: "Matin (6h)" },
  { hour: 12, label: "Midi (12h)" },
  { hour: 18, label: "Soir (18h)" },
  { hour: 24, label: "Minuit (24h)" },
];

const SEASON_OPTIONS: Array<{ season: string; label: string }> = [
  { season: "Spring", label: "Printemps" },
  { season: "Summer", label: "Été" },
  { season: "Fall", label: "Automne" },
  { season: "Winter", label: "Hiver" },
  { season: "auto", label: "Automatique" },
];

type CommandState =
  | { kind: "idle" }
  | { kind: "sending" }
  | { kind: "sent" }
  | { kind: "error"; message: string };

// Contrôle en direct du serveur Valheim de ce profil via FedoServerTools (voir
// CLAUDE.md, section "Joueurs en ligne (FedoServerTools)") -- une commande posée ici
// (POST /modpacks/:slug/server-command) n'est appliquée qu'au prochain rapport du mod
// pour ce profil, jamais immédiatement : le jeu ne peut être joint que par sondage,
// jamais l'inverse.
// Longueur max côté API (voir onlinePlayers.ts, serverCommandBodySchema) -- reflétée
// ici seulement pour empêcher de taper plus que ce que l'API acceptera, pas une
// validation à dupliquer.
const MAX_MESSAGE_LENGTH = 200;

export function ServerPage({ activeSlug }: ServerPageProps) {
  const [status, setStatus] = useState<OnlineStatus | null>(null);
  const [commandState, setCommandState] = useState<CommandState>({ kind: "idle" });
  const [message, setMessage] = useState("");

  useEffect(() => {
    let cancelled = false;

    function poll() {
      invoke<OnlineStatus>("fetch_online_players", { slug: activeSlug })
        .then((res) => {
          if (!cancelled) setStatus(res);
        })
        .catch(() => {});
    }

    setStatus(null);
    poll();
    const id = setInterval(poll, POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(id);
    };
  }, [activeSlug]);

  async function sendCommand(command: Record<string, unknown>) {
    setCommandState({ kind: "sending" });
    try {
      await invoke("send_server_command", { slug: activeSlug, command });
      setCommandState({ kind: "sent" });
    } catch (err) {
      setCommandState({ kind: "error", message: String(err) });
    }
  }

  async function sendMessage() {
    const trimmed = message.trim();
    if (!trimmed) return;
    await sendCommand({ command: "broadcast-message", message: trimmed });
    setMessage("");
  }

  const busy = commandState.kind === "sending";

  return (
    <div className="server-page">
      <header className="server-page__header">
        <h1>Serveur</h1>
        <p>Contrôle en direct du serveur Valheim via FedoServerTools.</p>
      </header>

      <div className="server-page__status-card">
        <p className="server-page__status-line">
          <span
            className={`server-page__dot server-page__dot--${status?.status ?? "offline"}`}
            aria-hidden="true"
          />
          Statut : {status ? STATUS_LABELS[status.status] : "…"}
        </p>
        {status?.status === "online" && (status.season || status.time) && (
          <p className="server-page__season">
            {status.season && <>Saison actuelle : {status.season}</>}
            {status.season && status.time && " · "}
            {status.time && <>Heure actuelle : {status.time}</>}
          </p>
        )}
      </div>

      <p className="server-page__hint">
        Chaque action ci-dessous est appliquée au prochain rapport du serveur
        (~30s), pas immédiatement — le serveur doit être en ligne pour qu'elle
        prenne effet.
      </p>

      <section className="server-page__section">
        <h2>Heure du jour</h2>
        <div className="server-page__actions">
          {TIME_OPTIONS.map((opt) => (
            <button
              key={opt.hour}
              type="button"
              className="btn btn--ghost"
              disabled={busy}
              onClick={() => sendCommand({ command: "set-time", hour: opt.hour })}
            >
              {opt.label}
            </button>
          ))}
        </div>
      </section>

      <section className="server-page__section">
        <h2>Saison</h2>
        <div className="server-page__actions">
          {SEASON_OPTIONS.map((opt) => (
            <button
              key={opt.season}
              type="button"
              className="btn btn--ghost"
              disabled={busy}
              onClick={() => sendCommand({ command: "set-season", season: opt.season })}
            >
              {opt.label}
            </button>
          ))}
        </div>
      </section>

      <section className="server-page__section">
        <h2>Message</h2>
        <p className="server-page__hint server-page__hint--tight">
          Affiché en jaune au centre de l'écran de chaque joueur connecté, posté dans son
          tchat en jeu, et dans le salon Discord des logs.
        </p>
        <form
          className="server-page__message-form"
          onSubmit={(e) => {
            e.preventDefault();
            void sendMessage();
          }}
        >
          <input
            type="text"
            className="server-page__message-input"
            placeholder="Message à afficher..."
            value={message}
            maxLength={MAX_MESSAGE_LENGTH}
            disabled={busy}
            onChange={(e) => setMessage(e.target.value)}
          />
          <button type="submit" className="btn btn--accent" disabled={busy || !message.trim()}>
            Envoyer
          </button>
        </form>
      </section>

      {commandState.kind === "sent" && (
        <p className="server-page__feedback is-success">Commande envoyée.</p>
      )}
      {commandState.kind === "error" && (
        <p className="server-page__feedback is-error">{commandState.message}</p>
      )}
    </div>
  );
}
