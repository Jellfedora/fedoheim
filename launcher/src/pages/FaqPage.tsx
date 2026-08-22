import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import "./FaqPage.css";

interface FaqEntry {
  question: string;
  answer: string;
}

interface FaqPageProps {
  isAdmin: boolean;
}

type LoadState = { kind: "loading" } | { kind: "error"; message: string } | { kind: "loaded" };
type SaveState = { kind: "idle" } | { kind: "saving" } | { kind: "error"; message: string };

export function FaqPage({ isAdmin }: FaqPageProps) {
  const [faq, setFaq] = useState<FaqEntry[]>([]);
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [openIndex, setOpenIndex] = useState<number | null>(0);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState<FaqEntry[]>([]);
  const [saveState, setSaveState] = useState<SaveState>({ kind: "idle" });

  useEffect(() => {
    invoke<FaqEntry[]>("fetch_faq")
      .then((fetched) => {
        setFaq(fetched);
        setState({ kind: "loaded" });
      })
      .catch((err) => setState({ kind: "error", message: String(err) }));
  }, []);

  function startEditing() {
    setDraft(faq.map((entry) => ({ ...entry })));
    setSaveState({ kind: "idle" });
    setEditing(true);
  }

  function updateDraft(index: number, field: keyof FaqEntry, value: string) {
    setDraft((prev) => prev.map((entry, i) => (i === index ? { ...entry, [field]: value } : entry)));
  }

  function removeDraftEntry(index: number) {
    setDraft((prev) => prev.filter((_, i) => i !== index));
  }

  function addDraftEntry() {
    setDraft((prev) => [...prev, { question: "", answer: "" }]);
  }

  async function handleSave() {
    const nextFaq = draft
      .map((entry) => ({ question: entry.question.trim(), answer: entry.answer.trim() }))
      .filter((entry) => entry.question.length > 0 && entry.answer.length > 0);

    setSaveState({ kind: "saving" });
    try {
      await invoke("save_faq", { faq: nextFaq });
      setFaq(nextFaq);
      setEditing(false);
      setSaveState({ kind: "idle" });
    } catch (err) {
      setSaveState({ kind: "error", message: String(err) });
    }
  }

  return (
    <div className="faq-page">
      <header className="faq-page__header">
        <div className="faq-page__title-row">
          <h1>FAQ</h1>
          {isAdmin && !editing && (
            <button type="button" className="btn btn--ghost" onClick={startEditing}>
              Éditer
            </button>
          )}
        </div>
        <p>Les questions les plus fréquentes sur le launcher et le serveur.</p>
      </header>

      {state.kind === "loading" && <p className="faq-page__status">Chargement...</p>}
      {state.kind === "error" && <p className="faq-page__status is-error">{state.message}</p>}

      {editing ? (
        <div className="faq-editor">
          {draft.map((entry, i) => (
            <div className="faq-editor__entry" key={i}>
              <input
                className="faq-editor__input"
                placeholder="Question"
                value={entry.question}
                onChange={(e) => updateDraft(i, "question", e.target.value)}
              />
              <textarea
                className="faq-editor__textarea"
                placeholder="Réponse"
                rows={3}
                value={entry.answer}
                onChange={(e) => updateDraft(i, "answer", e.target.value)}
              />
              <button
                type="button"
                className="btn btn--ghost faq-editor__remove"
                onClick={() => removeDraftEntry(i)}
              >
                Supprimer
              </button>
            </div>
          ))}

          <button type="button" className="btn btn--ghost" onClick={addDraftEntry}>
            + Ajouter une question
          </button>

          {saveState.kind === "error" && (
            <p className="faq-page__status is-error">{saveState.message}</p>
          )}

          <div className="faq-editor__actions">
            <button
              type="button"
              className="btn btn--ghost"
              onClick={() => setEditing(false)}
              disabled={saveState.kind === "saving"}
            >
              Annuler
            </button>
            <button
              type="button"
              className="btn btn--accent"
              onClick={handleSave}
              disabled={saveState.kind === "saving"}
            >
              {saveState.kind === "saving" ? "Enregistrement..." : "Enregistrer"}
            </button>
          </div>
        </div>
      ) : (
        <ul className="faq-list">
          {faq.map((entry, i) => {
            const isOpen = openIndex === i;
            return (
              <li key={entry.question} className="faq-list__item">
                <button
                  type="button"
                  className="faq-list__question"
                  aria-expanded={isOpen}
                  onClick={() => setOpenIndex(isOpen ? null : i)}
                >
                  <span>{entry.question}</span>
                  <span className={`faq-list__chevron ${isOpen ? "is-open" : ""}`} aria-hidden="true">
                    ⌄
                  </span>
                </button>
                {isOpen && <p className="faq-list__answer">{entry.answer}</p>}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
