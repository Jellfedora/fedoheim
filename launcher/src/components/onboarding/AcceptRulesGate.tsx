import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import type { UserInfo } from "../../types";
import "./Onboarding.css";

interface AcceptRulesGateProps {
  onAccepted: (user: UserInfo) => void;
  onLogout: () => void;
}

type LoadState = { kind: "loading" } | { kind: "error"; message: string } | { kind: "loaded" };

export function AcceptRulesGate({ onAccepted, onLogout }: AcceptRulesGateProps) {
  const [rules, setRules] = useState<string[]>([]);
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [checked, setChecked] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    invoke<string[]>("fetch_rules")
      .then((fetched) => {
        setRules(fetched);
        setState({ kind: "loaded" });
      })
      .catch((err) => setState({ kind: "error", message: String(err) }));
  }, []);

  async function handleAccept() {
    setSubmitting(true);
    setError(null);
    try {
      const updated = await invoke<UserInfo>("accept_rules");
      onAccepted(updated);
    } catch (err) {
      setError(String(err));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="onboarding">
      <div className="onboarding__card">
        <p className="onboarding__eyebrow">Avant de continuer</p>
        <h1>Règlement du serveur</h1>
        <p className="onboarding__intro">Lis et accepte le règlement pour accéder au launcher.</p>

        {state.kind === "loading" && <p className="onboarding__status">Chargement...</p>}
        {state.kind === "error" && <p className="onboarding__status is-error">{state.message}</p>}

        <ol className="onboarding__rules">
          {rules.map((rule, i) => (
            <li key={rule}>
              <span className="onboarding__rules-index">{String(i + 1).padStart(2, "0")}</span>
              <span>{rule}</span>
            </li>
          ))}
        </ol>

        <label className="onboarding__checkbox">
          <input
            type="checkbox"
            checked={checked}
            onChange={(e) => setChecked(e.target.checked)}
          />
          J'ai lu et j'accepte le règlement.
        </label>

        {error && <p className="onboarding__status is-error">{error}</p>}

        <div className="onboarding__actions">
          <button type="button" className="btn btn--ghost" onClick={onLogout} disabled={submitting}>
            Se déconnecter
          </button>
          <button
            type="button"
            className="btn btn--accent"
            onClick={handleAccept}
            disabled={!checked || submitting}
          >
            {submitting ? "Validation..." : "Continuer"}
          </button>
        </div>
      </div>
    </div>
  );
}
