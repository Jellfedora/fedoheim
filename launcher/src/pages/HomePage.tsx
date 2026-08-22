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
}

interface OnlinePlayers {
  online: boolean;
  players: OnlinePlayer[];
  updatedAt: string | null;
}

// Alimenté par le mod serveur FedoServerTools, qui poste toutes les ~30s — un intervalle
// plus court côté launcher n'apporterait rien de plus frais.
const ONLINE_PLAYERS_POLL_MS = 30_000;

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
          <p className="home-players__status">
            <span
              className={`home-players__dot ${onlinePlayers?.online ? "" : "home-players__dot--offline"}`}
            />
            Statut : {onlinePlayers?.online ? "En ligne" : "Hors ligne"}
          </p>
          {onlinePlayers?.online &&
            (onlinePlayers.players.length > 0 ? (
              <ul className="home-players">
                {onlinePlayers.players.map((player) => (
                  <li key={player.name} className="home-players__item">
                    <span>{player.name}</span>
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
