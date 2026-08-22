import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { formatDate } from "../utils/date";
import "./RulesPage.css";

type LoadState = { kind: "loading" } | { kind: "error"; message: string } | { kind: "loaded" };
type SaveState = { kind: "idle" } | { kind: "saving" } | { kind: "error"; message: string };

interface RulesPageProps {
  isAdmin: boolean;
  // Un `rulesAcceptedAt` non nul peut correspondre à une version dépassée du règlement
  // (voir serializeUser côté API) — n'affiche "signé le ..." que si hasAcceptedRules est
  // aussi vrai, sinon la date affichée mentirait sur la validité de la signature.
  hasAcceptedRules: boolean;
  rulesAcceptedAt: string | null;
}

export function RulesPage({ isAdmin, hasAcceptedRules, rulesAcceptedAt }: RulesPageProps) {
  const [rules, setRules] = useState<string[]>([]);
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  const [saveState, setSaveState] = useState<SaveState>({ kind: "idle" });

  useEffect(() => {
    invoke<string[]>("fetch_rules")
      .then((fetched) => {
        setRules(fetched);
        setState({ kind: "loaded" });
      })
      .catch((err) => setState({ kind: "error", message: String(err) }));
  }, []);

  function startEditing() {
    setDraft(rules.join("\n"));
    setSaveState({ kind: "idle" });
    setEditing(true);
  }

  async function handleSave() {
    const nextRules = draft
      .split("\n")
      .map((line) => line.trim())
      .filter((line) => line.length > 0);

    setSaveState({ kind: "saving" });
    try {
      await invoke("save_rules", { rules: nextRules });
      setRules(nextRules);
      setEditing(false);
      setSaveState({ kind: "idle" });
    } catch (err) {
      setSaveState({ kind: "error", message: String(err) });
    }
  }

  return (
    <div className="rules-page">
      <header className="rules-page__header">
        <div className="rules-page__title-row">
          <h1>Règlement</h1>
          {isAdmin && !editing && (
            <button type="button" className="btn btn--ghost" onClick={startEditing}>
              Éditer
            </button>
          )}
        </div>
        <p>À lire avant de rejoindre le serveur.</p>
      </header>

      {!editing && hasAcceptedRules && rulesAcceptedAt && (
        <p className="rules-page__signed">
          Tu as signé cette version du règlement le {formatDate(rulesAcceptedAt)}.
        </p>
      )}

      {state.kind === "loading" && <p className="rules-page__status">Chargement...</p>}
      {state.kind === "error" && <p className="rules-page__status is-error">{state.message}</p>}

      {editing ? (
        <div className="rules-editor">
          <textarea
            className="rules-editor__textarea"
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            rows={10}
            placeholder="Une règle par ligne..."
          />
          {saveState.kind === "error" && (
            <p className="rules-page__status is-error">{saveState.message}</p>
          )}
          <div className="rules-editor__actions">
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
        <ol className="rules-list">
          {rules.map((rule, i) => (
            <li key={rule}>
              <span className="rules-list__index">{String(i + 1).padStart(2, "0")}</span>
              <span className="rules-list__text">{rule}</span>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
