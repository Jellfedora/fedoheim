import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { MarkdownText } from "../components/MarkdownText";
import { MOCK_PLAYERS_ONLINE, SERVER_NAME } from "../data/mock";
import type { Announcement } from "./AnnouncementsPage";
import { getApiBaseUrl } from "../utils/apiBaseUrl";
import { formatDate } from "../utils/date";
import "./HomePage.css";

interface HomePageProps {
  heroEyebrow: string;
  heroTagline: string;
}

export function HomePage({ heroEyebrow, heroTagline }: HomePageProps) {
  const [latestAnnouncement, setLatestAnnouncement] = useState<Announcement | null>(null);
  const [apiBaseUrl, setApiBaseUrl] = useState("");

  useEffect(() => {
    invoke<Announcement[]>("fetch_announcements")
      .then((fetched) => setLatestAnnouncement(fetched[0] ?? null))
      .catch(() => setLatestAnnouncement(null));
    getApiBaseUrl().then(setApiBaseUrl);
  }, []);

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
            <h2>En ligne</h2>
            <span className="home-card__badge">{MOCK_PLAYERS_ONLINE.length}</span>
          </div>
          <ul className="home-players">
            {MOCK_PLAYERS_ONLINE.map((name) => (
              <li key={name}>
                <span className="home-players__dot" />
                {name}
              </li>
            ))}
          </ul>
        </section>
      </div>
    </div>
  );
}
