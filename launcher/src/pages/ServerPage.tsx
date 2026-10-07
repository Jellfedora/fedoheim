import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { confirm } from "@tauri-apps/plugin-dialog";
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

// Voir GET /modpacks/:slug/power-status -- état du service systemd rapporté par l'agent
// d'alimentation (game-server/power-agent/), indépendant de FedoServerTools : reste
// disponible quand le jeu est éteint, c'est tout son intérêt.
type PowerAction = "start" | "stop" | "restart";

interface PowerStatus {
  agentConnected: boolean;
  serviceState: string | null;
  pendingAction: PowerAction | null;
}

const SERVICE_STATE_LABELS: Record<string, string> = {
  active: "Process lancé",
  activating: "Process en cours de lancement",
  deactivating: "Process en cours d'arrêt",
  inactive: "Process arrêté",
  failed: "Process planté",
};

const PENDING_ACTION_LABELS: Record<PowerAction, string> = {
  start: "Démarrage demandé",
  stop: "Arrêt demandé",
  restart: "Redémarrage demandé",
};

const POWER_CONFIRMATIONS: Partial<Record<PowerAction, string>> = {
  stop: "Arrêter le serveur Valheim ? Les joueurs connectés seront déconnectés (le monde est sauvegardé avant l'arrêt).",
  restart:
    "Redémarrer le serveur Valheim ? Les joueurs connectés seront déconnectés (le monde est sauvegardé avant l'arrêt).",
};

type CommandState =
  { kind: "idle" } | { kind: "sending" } | { kind: "sent" } | { kind: "error"; message: string };

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
  const [power, setPower] = useState<PowerStatus | null>(null);

  useEffect(() => {
    let cancelled = false;

    function poll() {
      invoke<OnlineStatus>("fetch_online_players", { slug: activeSlug })
        .then((res) => {
          if (!cancelled) setStatus(res);
        })
        .catch(() => {});
      invoke<PowerStatus>("fetch_power_status", { slug: activeSlug })
        .then((res) => {
          if (!cancelled) setPower(res);
        })
        .catch(() => {});
    }

    setStatus(null);
    setPower(null);
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

  async function sendPowerAction(action: PowerAction) {
    const question = POWER_CONFIRMATIONS[action];
    if (question && !(await confirm(question))) return;
    setCommandState({ kind: "sending" });
    try {
      await invoke("send_power_command", { slug: activeSlug, action });
      setCommandState({ kind: "sent" });
      setPower((prev) => (prev ? { ...prev, pendingAction: action } : prev));
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
  const serviceRunning = power?.serviceState === "active" || power?.serviceState === "activating";
  const powerDisabled = busy || !power?.agentConnected || power.pendingAction !== null;

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

      <section className="server-page__section">
        <h2>Machine</h2>
        <p className="server-page__hint server-page__hint--tight">
          {!power
            ? "…"
            : !power.agentConnected
              ? "Agent d'alimentation injoignable — impossible de démarrer/arrêter le serveur depuis le launcher."
              : power.pendingAction
                ? `${PENDING_ACTION_LABELS[power.pendingAction]} — prise en compte dans ~10s.`
                : (SERVICE_STATE_LABELS[power.serviceState ?? ""] ?? power.serviceState)}
        </p>
        <div className="server-page__actions">
          <button
            type="button"
            className="btn btn--accent"
            disabled={powerDisabled || serviceRunning}
            onClick={() => sendPowerAction("start")}
          >
            Démarrer
          </button>
          <button
            type="button"
            className="btn btn--ghost"
            disabled={powerDisabled || !serviceRunning}
            onClick={() => sendPowerAction("restart")}
          >
            Redémarrer
          </button>
          <button
            type="button"
            className="btn btn--ghost"
            disabled={powerDisabled || !serviceRunning}
            onClick={() => sendPowerAction("stop")}
          >
            Arrêter
          </button>
        </div>
      </section>

      <p className="server-page__hint">
        Chaque action de jeu ci-dessous est appliquée au prochain rapport du serveur (~30s), pas
        immédiatement — le serveur doit être en ligne pour qu'elle prenne effet.
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
          Affiché en jaune au centre de l'écran de chaque joueur connecté, posté dans son tchat en
          jeu, et dans le salon Discord des logs.
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
