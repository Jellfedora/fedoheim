import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { AnnouncementComposer } from "../components/AnnouncementComposer";
import { MarkdownText } from "../components/MarkdownText";
import { getApiBaseUrl } from "../utils/apiBaseUrl";
import { formatDate } from "../utils/date";
import "./AnnouncementsPage.css";

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

interface AnnouncementsPageProps {
  isAdmin: boolean;
}

type LoadState = { kind: "loading" } | { kind: "error"; message: string } | { kind: "loaded" };

export function AnnouncementsPage({ isAdmin }: AnnouncementsPageProps) {
  const [announcements, setAnnouncements] = useState<Announcement[]>([]);
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [apiBaseUrl, setApiBaseUrl] = useState("");
  const [composing, setComposing] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);

  useEffect(() => {
    loadAnnouncements();
    getApiBaseUrl().then(setApiBaseUrl);
  }, []);

  function loadAnnouncements() {
    setState({ kind: "loading" });
    invoke<Announcement[]>("fetch_announcements")
      .then((fetched) => {
        setAnnouncements(fetched);
        setState({ kind: "loaded" });
      })
      .catch((err) => setState({ kind: "error", message: String(err) }));
  }

  async function handleCreate(data: { title: string; message: string; images: string[] }) {
    const created = await invoke<Announcement>("post_announcement", {
      title: data.title || null,
      message: data.message,
      images: data.images,
    });
    setAnnouncements((prev) => [created, ...prev]);
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
    } catch (err) {
      setState({ kind: "error", message: String(err) });
    }
  }

  return (
    <div className="announcements-page">
      <header className="announcements-page__header">
        <div className="announcements-page__title-row">
          <h1>Annonces</h1>
          {isAdmin && !composing && (
            <button type="button" className="btn btn--ghost" onClick={() => setComposing(true)}>
              + Nouvelle annonce
            </button>
          )}
        </div>
        <p>L'historique des annonces du serveur.</p>
      </header>

      {isAdmin && composing && (
        <div className="announcements-page__composer">
          <AnnouncementComposer
            submitLabel="Publier"
            submittingLabel="Publication..."
            onSubmit={handleCreate}
            onCancel={() => setComposing(false)}
          />
        </div>
      )}

      {state.kind === "loading" && <p className="announcements-page__status">Chargement...</p>}
      {state.kind === "error" && (
        <p className="announcements-page__status is-error">{state.message}</p>
      )}
      {state.kind === "loaded" && announcements.length === 0 && (
        <p className="announcements-page__status">Aucune annonce pour le moment.</p>
      )}

      <ul className="announcements-list">
        {announcements.map((a) =>
          editingId === a.id ? (
            <li key={a.id} className="announcements-list__item">
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
            <li key={a.id} className="announcements-list__item">
              {a.title && <h3 className="announcements-list__title">{a.title}</h3>}
              <div className="announcements-list__message">
                <MarkdownText text={a.message} />
              </div>
              {a.images.length > 0 && (
                <div className="announcements-list__images">
                  {a.images.map((url) => (
                    <img key={url} src={`${apiBaseUrl}${url}`} alt="" />
                  ))}
                </div>
              )}
              <div className="announcements-list__footer">
                <span className="announcements-list__meta">
                  {a.author} · {formatDate(a.createdAt)}
                  {a.updatedAt && " · modifiée"}
                  {isAdmin && (
                    <span
                      className={`announcements-list__discord-badge ${a.postedToDiscord ? "is-posted" : ""}`}
                    >
                      {a.postedToDiscord ? "✓ Discord" : "Pas sur Discord"}
                    </span>
                  )}
                </span>
                {isAdmin && (
                  <div className="announcements-list__admin-actions">
                    <button
                      type="button"
                      className="announcements-list__action"
                      onClick={() => setEditingId(a.id)}
                    >
                      Modifier
                    </button>
                    <button
                      type="button"
                      className="announcements-list__action is-danger"
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
    </div>
  );
}
