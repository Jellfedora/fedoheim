import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { MarkdownText } from "../components/MarkdownText";
import { SERVER_NAME } from "../data/mock";
import type { Announcement } from "./AnnouncementsPage";
import { getApiBaseUrl } from "../utils/apiBaseUrl";
import { formatDate } from "../utils/date";
import "./HomePage.css";

interface HomePageProps {
  heroEyebrow: string;
  heroTagline: string;
  // Profil ciblé pour "qui est en ligne" — voir App.tsx::effectiveModpackSlug (toujours
  // la production pour un joueur normal, le profil actif pour un admin en train d'en
  // tester un autre).
  slug: string;
}

interface OnlinePlayer {
  name: string;
  // Texte final tel que configuré par l'admin dans le .cfg de FedoServerTools (ex:
  // "Prairies" pour Meadows) — déjà traduit côté mod, affiché tel quel ici, pas de
  // mapping à faire. `null` si le joueur a désactivé le partage de sa position.
  biome: string | null;
  // Armure totale actuelle, arrondie côté mod. `null` si le personnage n'a pas pu être
  // retrouvé côté serveur au moment du rapport.
  armor: number | null;
}

// Icône bouclier minimale, en `currentColor` pour hériter de la couleur du texte
// environnant plutôt que d'ajouter une dépendance à une librairie d'icônes pour un
// seul usage.
function ShieldIcon() {
  return (
    <svg
      className="home-players__shield-icon"
      viewBox="0 0 16 16"
      width="12"
      height="12"
      aria-hidden="true"
    >
      <path
        fill="currentColor"
        d="M8 1 2.5 3v4.2c0 3.4 2.2 6.2 5.5 7.3 3.3-1.1 5.5-3.9 5.5-7.3V3L8 1Z"
      />
    </svg>
  );
}

// "offline" n'est jamais envoyé par le mod (voir onlinePlayers.ts) : c'est ce que l'API
// renvoie elle-même dès que plus aucun rapport frais n'est disponible (péremption ou
// jamais démarré).
type ServerStatus = "starting" | "online" | "stopping" | "offline";

interface OnlinePlayers {
  status: ServerStatus;
  online: boolean;
  players: OnlinePlayer[];
  // Nom déjà traduit par l'admin dans le .cfg de FedoServerTools (section [Seasons]) --
  // affiché tel quel. `null` si le mod Seasons n'est pas installé sur le serveur, ou si
  // aucun rapport frais n'est disponible.
  season: string | null;
  updatedAt: string | null;
}

const STATUS_LABELS: Record<ServerStatus, string> = {
  starting: "Démarrage en cours…",
  online: "En ligne",
  stopping: "Arrêt en cours…",
  offline: "Hors ligne",
};

// Alimenté par le mod serveur FedoServerTools, qui poste toutes les
// SyncIntervalSeconds (10s au minimum autorisé côté mod) — 10s ici aussi, pour ne pas
// ajouter un délai d'affichage supplémentaire au-dessus du rythme le plus rapide
// possible côté mod.
const ONLINE_PLAYERS_POLL_MS = 10_000;

export function HomePage({ heroEyebrow, heroTagline, slug }: HomePageProps) {
  const [latestAnnouncement, setLatestAnnouncement] = useState<Announcement | null>(null);
  const [apiBaseUrl, setApiBaseUrl] = useState("");
  const [onlinePlayers, setOnlinePlayers] = useState<OnlinePlayers | null>(null);

  useEffect(() => {
    invoke<Announcement[]>("fetch_announcements")
      .then((fetched) => setLatestAnnouncement(fetched[0] ?? null))
      .catch(() => setLatestAnnouncement(null));
    getApiBaseUrl().then(setApiBaseUrl);
  }, []);

  useEffect(() => {
    let cancelled = false;

    function load() {
      invoke<OnlinePlayers>("fetch_online_players", { slug })
        .then((fetched) => {
          if (!cancelled) setOnlinePlayers(fetched);
        })
        .catch(() => {
          if (!cancelled) setOnlinePlayers(null);
        });
    }

    load();
    const interval = setInterval(load, ONLINE_PLAYERS_POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [slug]);

  return (
    <div className="home-page">
      <section className="home-hero">
        <p className="home-hero__eyebrow">{heroEyebrow}</p>
        <h1 className="home-hero__title">{SERVER_NAME}</h1>
        <p className="home-hero__tagline">{heroTagline}</p>
      </section>

      <div className="home-grid">
        <section className="home-card home-card--announcement">
          <div className="home-card__header">
            <h2>Dernière annonce</h2>
          </div>
          {latestAnnouncement ? (
            <>
              {latestAnnouncement.title && (
                <h3 className="home-announcement__title">{latestAnnouncement.title}</h3>
              )}
              <div className="home-announcement__message">
                <MarkdownText text={latestAnnouncement.message} />
              </div>
              {latestAnnouncement.images.length > 0 && (
                <div className="home-announcement__images">
                  {latestAnnouncement.images.map((url) => (
                    <img key={url} src={`${apiBaseUrl}${url}`} alt="" />
                  ))}
                </div>
              )}
              <p className="home-announcement__meta">
                {latestAnnouncement.author} · {formatDate(latestAnnouncement.createdAt)}
              </p>
            </>
          ) : (
            <p className="home-announcement__message">Aucune annonce pour le moment.</p>
          )}
        </section>

        <section className="home-card home-card--players">
          <div className="home-card__header">
            <h2>État du serveur</h2>
          </div>
          {(() => {
            const status = onlinePlayers?.status ?? "offline";
            return (
              <p className="home-players__status">
                <span className={`home-players__dot home-players__dot--${status}`} />
                Statut : {STATUS_LABELS[status]}
              </p>
            );
          })()}
          {onlinePlayers?.season && (
            <p className="home-players__season">Saison : {onlinePlayers.season}</p>
          )}
          {onlinePlayers?.status === "online" &&
            (onlinePlayers.players.length > 0 ? (
              <ul className="home-players">
                {onlinePlayers.players.map((player) => (
                  <li key={player.name} className="home-players__item">
                    <span className="home-players__name-group">
                      <span>{player.name}</span>
                      {player.armor !== null && (
                        <span className="home-players__armor" title="Armure">
                          <ShieldIcon />
                          {player.armor}
                        </span>
                      )}
                    </span>
                    {player.biome && <span className="home-players__biome">{player.biome}</span>}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="home-players__hint">Aucun joueur connecté.</p>
            ))}
        </section>
      </div>
    </div>
  );
}
