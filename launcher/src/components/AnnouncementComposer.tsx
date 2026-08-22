import { useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { getApiBaseUrl } from "../utils/apiBaseUrl";
import "./AnnouncementComposer.css";

interface AnnouncementComposerProps {
  initialTitle?: string;
  initialMessage?: string;
  initialImages?: string[];
  submitLabel: string;
  submittingLabel: string;
  onSubmit: (data: { title: string; message: string; images: string[] }) => Promise<void>;
  onCancel?: () => void;
}

// Doit rester synchronisé avec la limite serveur (voir
// api/src/announcements/routes.ts::announcementBodySchema, elle-même calée sur la
// limite réelle de Discord pour la description d'un embed) — sinon l'admin ne
// découvre le dépassement qu'au moment de l'échec de la requête.
const MESSAGE_MAX_LENGTH = 4096;

const TOOLBAR_ITEMS: Array<{ label: string; marker: string }> = [
  { label: "Gras", marker: "**" },
  { label: "Italique", marker: "*" },
  { label: "Souligné", marker: "__" },
  { label: "Barré", marker: "~~" },
  { label: "Spoiler", marker: "||" },
];

export function AnnouncementComposer({
  initialTitle = "",
  initialMessage = "",
  initialImages = [],
  submitLabel,
  submittingLabel,
  onSubmit,
  onCancel,
}: AnnouncementComposerProps) {
  const [title, setTitle] = useState(initialTitle);
  const [message, setMessage] = useState(initialMessage);
  const [images, setImages] = useState(initialImages);
  const [apiBaseUrl, setApiBaseUrl] = useState("");
  const [uploading, setUploading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    getApiBaseUrl().then(setApiBaseUrl);
  }, []);

  function wrapSelection(marker: string) {
    const el = textareaRef.current;
    if (!el) return;

    const start = el.selectionStart;
    const end = el.selectionEnd;
    const selected = message.slice(start, end);
    const next = message.slice(0, start) + marker + selected + marker + message.slice(end);
    setMessage(next);

    requestAnimationFrame(() => {
      el.focus();
      el.setSelectionRange(start + marker.length, end + marker.length);
    });
  }

  async function handleAddImage() {
    setUploading(true);
    setError(null);
    try {
      const url = await invoke<string | null>("pick_and_upload_image");
      if (url) {
        setImages((prev) => [...prev, url]);
      }
    } catch (err) {
      setError(String(err));
    } finally {
      setUploading(false);
    }
  }

  function removeImage(url: string) {
    setImages((prev) => prev.filter((i) => i !== url));
  }

  const messageLength = message.trim().length;
  const overLimit = messageLength > MESSAGE_MAX_LENGTH;

  async function handleSubmit() {
    if (!message.trim() || overLimit) return;
    setSubmitting(true);
    setError(null);
    try {
      await onSubmit({ title: title.trim(), message: message.trim(), images });
    } catch (err) {
      setError(String(err));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="announcement-composer">
      <input
        className="announcement-composer__title"
        placeholder="Titre (optionnel)"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
      />

      <div className="announcement-composer__toolbar">
        {TOOLBAR_ITEMS.map((item) => (
          <button
            key={item.label}
            type="button"
            className="announcement-composer__toolbar-btn"
            onClick={() => wrapSelection(item.marker)}
            title={item.label}
          >
            {item.label}
          </button>
        ))}
      </div>

      <textarea
        ref={textareaRef}
        className="announcement-composer__textarea"
        placeholder="Écrire l'annonce... (markdown façon Discord)"
        rows={4}
        value={message}
        onChange={(e) => setMessage(e.target.value)}
      />
      <p className={`announcement-composer__counter ${overLimit ? "is-over" : ""}`}>
        {messageLength} / {MESSAGE_MAX_LENGTH}
      </p>

      {images.length > 0 && (
        <div className="announcement-composer__images">
          {images.map((url) => (
            <div className="announcement-composer__image" key={url}>
              <img src={`${apiBaseUrl}${url}`} alt="" />
              <button
                type="button"
                className="announcement-composer__image-remove"
                onClick={() => removeImage(url)}
                title="Retirer cette image"
              >
                ×
              </button>
            </div>
          ))}
        </div>
      )}

      {error && <p className="announcement-composer__error">{error}</p>}

      <div className="announcement-composer__actions">
        <button
          type="button"
          className="btn btn--ghost"
          onClick={handleAddImage}
          disabled={uploading || submitting}
        >
          {uploading ? "Envoi..." : "+ Ajouter une image"}
        </button>

        <div className="announcement-composer__actions-right">
          {onCancel && (
            <button type="button" className="btn btn--ghost" onClick={onCancel} disabled={submitting}>
              Annuler
            </button>
          )}
          <button
            type="button"
            className="btn btn--accent"
            onClick={handleSubmit}
            disabled={!message.trim() || overLimit || submitting || uploading}
          >
            {submitting ? submittingLabel : submitLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
