import { useCallback, useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { AnnouncementComposer } from "../components/AnnouncementComposer";
import { MarkdownText } from "../components/MarkdownText";
import { SERVER_NAME } from "../data/mock";
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
  // Affiche le bouton "+ Nouvelle annonce" et les actions Modifier/Supprimer sur chaque
  // annonce du fil ci-dessous.
  isAdmin: boolean;
}

export interface Announcement {
  id: number;
  author: string;
  title: string | null;
  message: string;
  images: string[];
  createdAt: string;
  updatedAt: string | null;
  postedToDiscord: boolean;
}

interface AnnouncementPage {
  items: Announcement[];
  total: number;
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

// 3 annonces chargées au départ, les suivantes par lots de la même taille dès que le
// bas du fil déjà chargé devient visible (voir IntersectionObserver plus bas).
const ANNOUNCEMENTS_PAGE_SIZE = 3;

type FeedState = { kind: "loading" } | { kind: "error"; message: string } | { kind: "loaded" };

export function HomePage({ heroEyebrow, heroTagline, slug, isAdmin }: HomePageProps) {
  const [apiBaseUrl, setApiBaseUrl] = useState("");
  const [onlinePlayers, setOnlinePlayers] = useState<OnlinePlayers | null>(null);

  const [announcements, setAnnouncements] = useState<Announcement[]>([]);
  const [totalAnnouncements, setTotalAnnouncements] = useState(0);
  const [feedState, setFeedState] = useState<FeedState>({ kind: "loading" });
  const [loadingMore, setLoadingMore] = useState(false);
  const [composing, setComposing] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const sentinelRef = useRef<HTMLDivElement>(null);
  // Ref plutôt que dérivé de `loadingMore` (state) : évite un double appel si
  // l'IntersectionObserver redéclenche avant le prochain rendu (setState n'est pas
  // synchrone), sans avoir à mettre d'effet de bord dans un setState updater.
  const loadingMoreRef = useRef(false);

  useEffect(() => {
    getApiBaseUrl().then(setApiBaseUrl);
  }, []);

  useEffect(() => {
    invoke<AnnouncementPage>("fetch_announcements", { limit: ANNOUNCEMENTS_PAGE_SIZE, offset: 0 })
      .then((page) => {
        setAnnouncements(page.items);
        setTotalAnnouncements(page.total);
        setFeedState({ kind: "loaded" });
      })
      .catch((err) => setFeedState({ kind: "error", message: String(err) }));
  }, []);

  // Recréé à chaque changement du nombre d'annonces déjà chargées, pour que l'offset de
  // la page suivante reste correct — voir l'effet ci-dessous qui (dé)branche
  // l'IntersectionObserver en fonction de cette même valeur.
  const loadMore = useCallback(() => {
    if (loadingMoreRef.current) return;
    loadingMoreRef.current = true;
    setLoadingMore(true);

    invoke<AnnouncementPage>("fetch_announcements", {
      limit: ANNOUNCEMENTS_PAGE_SIZE,
      offset: announcements.length,
    })
      .then((page) => {
        setAnnouncements((prev) => [...prev, ...page.items]);
        setTotalAnnouncements(page.total);
      })
      .catch(() => {
        // Best-effort : un échec de "charger plus" (réseau furtif...) laisse
        // simplement le fil tel quel, réessayable au prochain scroll.
      })
      .finally(() => {
        loadingMoreRef.current = false;
        setLoadingMore(false);
      });
  }, [announcements.length]);

  // Déclenche `loadMore` dès que la sentinelle en bas du fil déjà chargé devient
  // visible — pas de bouton "Charger plus" explicite, voir le comportement demandé.
  // Rebranché à chaque nouvelle page (announcements.length change) puisque le sentinel
  // reste le même noeud DOM mais que `loadMore` (donc l'offset qu'il capture) change.
  useEffect(() => {
    const el = sentinelRef.current;
    if (!el || announcements.length >= totalAnnouncements) return;

    const observer = new IntersectionObserver((entries) => {
      if (entries[0]?.isIntersecting) loadMore();
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, [announcements.length, totalAnnouncements, loadMore]);

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

  async function handleCreate(data: { title: string; message: string; images: string[] }) {
    const created = await invoke<Announcement>("post_announcement", {
      title: data.title || null,
      message: data.message,
      images: data.images,
    });
    setAnnouncements((prev) => [created, ...prev]);
    setTotalAnnouncements((prev) => prev + 1);
    setComposing(false);
  }

  async function handleUpdate(
    id: number,
    data: { title: string; message: string; images: string[] },
  ) {
    const updated = await invoke<Announcement>("update_announcement", {
      id,
      title: data.title || null,
      message: data.message,
      images: data.images,
    });
    setAnnouncements((prev) => prev.map((a) => (a.id === id ? updated : a)));
    setEditingId(null);
  }

  async function handleDelete(id: number) {
    try {
      await invoke("delete_announcement", { id });
      setAnnouncements((prev) => prev.filter((a) => a.id !== id));
      setTotalAnnouncements((prev) => Math.max(0, prev - 1));
    } catch (err) {
      setFeedState({ kind: "error", message: String(err) });
    }
  }

  return (
    <div className="home-page">
      <section className="home-hero">
        <p className="home-hero__eyebrow">{heroEyebrow}</p>
        <h1 className="home-hero__title">{SERVER_NAME}</h1>
        <p className="home-hero__tagline">{heroTagline}</p>
      </section>

      <div className="home-grid">
        <section className="home-feed">
          <div className="home-feed__header">
            <h2>Annonces</h2>
            {isAdmin && !composing && (
              <button type="button" className="btn btn--ghost" onClick={() => setComposing(true)}>
                + Nouvelle annonce
              </button>
            )}
          </div>

          {isAdmin && composing && (
            <div className="home-feed__composer">
              <AnnouncementComposer
                submitLabel="Publier"
                submittingLabel="Publication..."
                onSubmit={handleCreate}
                onCancel={() => setComposing(false)}
              />
            </div>
          )}

          {feedState.kind === "loading" && <p className="home-feed__status">Chargement...</p>}
          {feedState.kind === "error" && (
            <p className="home-feed__status is-error">{feedState.message}</p>
          )}
          {feedState.kind === "loaded" && announcements.length === 0 && (
            <p className="home-feed__status">Aucune annonce pour le moment.</p>
          )}

          <ul className="home-feed__list">
            {announcements.map((a) =>
              editingId === a.id ? (
                <li key={a.id} className="home-feed__item">
                  <AnnouncementComposer
                    initialTitle={a.title ?? ""}
                    initialMessage={a.message}
                    initialImages={a.images}
                    submitLabel="Enregistrer"
                    submittingLabel="Enregistrement..."
                    onSubmit={(data) => handleUpdate(a.id, data)}
                    onCancel={() => setEditingId(null)}
                  />
                </li>
              ) : (
                <li key={a.id} className="home-feed__item">
                  {a.title && <h3 className="home-feed__title">{a.title}</h3>}
                  <div className="home-feed__message">
                    <MarkdownText text={a.message} />
                  </div>
                  {a.images.length > 0 && (
                    <div className="home-feed__images">
                      {a.images.map((url) => (
                        <img key={url} src={`${apiBaseUrl}${url}`} alt="" />
                      ))}
                    </div>
                  )}
                  <div className="home-feed__footer">
                    <span className="home-feed__meta">
                      {a.author} · {formatDate(a.createdAt)}
                      {a.updatedAt && " · modifiée"}
                      {isAdmin && (
                        <span
                          className={`home-feed__discord-badge ${a.postedToDiscord ? "is-posted" : ""}`}
                        >
                          {a.postedToDiscord ? "✓ Discord" : "Pas sur Discord"}
                        </span>
                      )}
                    </span>
                    {isAdmin && (
                      <div className="home-feed__admin-actions">
                        <button
                          type="button"
                          className="home-feed__action"
                          onClick={() => setEditingId(a.id)}
                        >
                          Modifier
                        </button>
                        <button
                          type="button"
                          className="home-feed__action is-danger"
                          onClick={() => handleDelete(a.id)}
                        >
                          Supprimer
                        </button>
                      </div>
                    )}
                  </div>
                </li>
              ),
            )}
          </ul>

          {announcements.length < totalAnnouncements && (
            <div ref={sentinelRef} className="home-feed__sentinel">
              {loadingMore && <p className="home-feed__status">Chargement...</p>}
            </div>
          )}
        </section>

        <aside className="home-sidebar">
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
        </aside>
      </div>
    </div>
  );
}
