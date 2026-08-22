import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { openPath } from "@tauri-apps/plugin-opener";
import "./SettingsPage.css";

interface Settings {
  buyMeACoffeeUrl: string;
  heroEyebrow: string;
  heroTagline: string;
}

interface SettingsPageProps {
  onSaved: (settings: Settings) => void;
}

type LoadState = { kind: "loading" } | { kind: "error"; message: string } | { kind: "loaded" };
type SaveState = { kind: "idle" } | { kind: "saving" } | { kind: "error"; message: string };

export function SettingsPage({ onSaved }: SettingsPageProps) {
  const [heroEyebrow, setHeroEyebrow] = useState("");
  const [heroTagline, setHeroTagline] = useState("");
  const [buyMeACoffeeUrl, setBuyMeACoffeeUrl] = useState("");
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [saveState, setSaveState] = useState<SaveState>({ kind: "idle" });
  const [saved, setSaved] = useState(false);
  const [folderError, setFolderError] = useState<string | null>(null);

  useEffect(() => {
    invoke<Settings>("fetch_settings")
      .then((fetched) => {
        setHeroEyebrow(fetched.heroEyebrow);
        setHeroTagline(fetched.heroTagline);
        setBuyMeACoffeeUrl(fetched.buyMeACoffeeUrl);
        setState({ kind: "loaded" });
      })
      .catch((err) => setState({ kind: "error", message: String(err) }));
  }, []);

  function markDirty() {
    setSaved(false);
  }

  async function handleSave() {
    setSaveState({ kind: "saving" });
    setSaved(false);
    try {
      const updated = await invoke<Settings>("save_settings", {
        buyMeACoffeeUrl: buyMeACoffeeUrl.trim(),
        heroEyebrow: heroEyebrow.trim(),
        heroTagline: heroTagline.trim(),
      });
      setHeroEyebrow(updated.heroEyebrow);
      setHeroTagline(updated.heroTagline);
      setBuyMeACoffeeUrl(updated.buyMeACoffeeUrl);
      onSaved(updated);
      setSaveState({ kind: "idle" });
      setSaved(true);
    } catch (err) {
      setSaveState({ kind: "error", message: String(err) });
    }
  }

  async function handleOpenProfileFolder() {
    setFolderError(null);
    try {
      const path = await invoke<string>("profile_dir_path");
      await openPath(path);
    } catch (err) {
      setFolderError(String(err));
    }
  }

  const canSave = heroEyebrow.trim() && heroTagline.trim() && buyMeACoffeeUrl.trim();

  return (
    <div className="settings-page">
      <header className="settings-page__header">
        <h1>Paramètres</h1>
        <p>Réglages généraux du launcher.</p>
      </header>

      {state.kind === "loading" && <p className="settings-page__status">Chargement...</p>}
      {state.kind === "error" && (
        <p className="settings-page__status is-error">{state.message}</p>
      )}

      {state.kind !== "loading" && (
        <>
          <div className="settings-panel">
            <div className="settings-field">
              <label className="settings-field__label" htmlFor="hero-eyebrow">
                Sous-titre de l'accueil (au-dessus du nom du serveur)
              </label>
              <input
                id="hero-eyebrow"
                className="settings-field__input"
                value={heroEyebrow}
                onChange={(e) => {
                  setHeroEyebrow(e.target.value);
                  markDirty();
                }}
              />
            </div>

            <div className="settings-field">
              <label className="settings-field__label" htmlFor="hero-tagline">
                Accroche de l'accueil (sous le nom du serveur)
              </label>
              <input
                id="hero-tagline"
                className="settings-field__input"
                value={heroTagline}
                onChange={(e) => {
                  setHeroTagline(e.target.value);
                  markDirty();
                }}
              />
            </div>

            <div className="settings-field">
              <label className="settings-field__label" htmlFor="buy-me-a-coffee-url">
                Lien "Soutenir" (Buy Me a Coffee)
              </label>
              <input
                id="buy-me-a-coffee-url"
                className="settings-field__input"
                type="url"
                placeholder="https://buymeacoffee.com/..."
                value={buyMeACoffeeUrl}
                onChange={(e) => {
                  setBuyMeACoffeeUrl(e.target.value);
                  markDirty();
                }}
              />
            </div>

            <div className="settings-field">
              <label className="settings-field__label">Dossier BepInEx / mods</label>
              <p className="settings-page__hint">
                Ouvre le dossier où le launcher installe BepInEx et les mods (utile pour
                vérifier ou déboguer une installation).
              </p>
              <button type="button" className="btn btn--ghost" onClick={handleOpenProfileFolder}>
                Ouvrir le dossier
              </button>
              {folderError && <p className="settings-page__status is-error">{folderError}</p>}
            </div>
          </div>

          {saveState.kind === "error" && (
            <p className="settings-page__status is-error">{saveState.message}</p>
          )}
          {saved && saveState.kind === "idle" && (
            <p className="settings-page__status is-success">Enregistré.</p>
          )}

          <div className="settings-page__actions">
            <button
              type="button"
              className="btn btn--accent"
              onClick={handleSave}
              disabled={!canSave || saveState.kind === "saving"}
            >
              {saveState.kind === "saving" ? "Enregistrement..." : "Enregistrer"}
            </button>
          </div>
        </>
      )}
    </div>
  );
}
