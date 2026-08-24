import { useEffect, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { confirm } from "@tauri-apps/plugin-dialog";
import { PRODUCTION_MODPACK_SLUG } from "../data/mock";
import { formatDate } from "../utils/date";
import { hexToRgba } from "../utils/color";
import "./ProfilesPage.css";

// Cible d'auto-connexion de ce profil (voir FedoServerTools / CLAUDE.md) — `null` =
// profil non configuré, comportement vanilla inchangé pour ce profil.
type AutoConnectTarget =
  | { type: "world"; world: string }
  | { type: "server"; host: string; port: number; password: string };

interface ModpackProfile {
  slug: string;
  name: string;
  version: string;
  isDefault: boolean;
  // Choisie par un admin pour distinguer ce profil dans le launcher (badge de la
  // playbar, cette page) — `null` tant qu'aucune n'a été choisie. Toujours ignorée
  // pour le profil production, qui n'expose de toute façon jamais le sélecteur.
  color: string | null;
  // Le mod serveur FedoServerTools a-t-il déjà un jeton pour ce profil (voir bloc
  // "Jeton serveur" ci-dessous) — jamais la valeur elle-même, révélée séparément via
  // fetch_report_token/regenerate_report_token.
  hasReportToken: boolean;
  modCount: number;
  updatedAt: string;
  autoConnect: AutoConnectTarget | null;
}

// Point de départ du <input type="color"> tant qu'aucune couleur n'a encore été
// choisie pour ce profil — un <input type="color"> exige toujours une valeur, mais
// celle-ci ne doit surtout pas ressembler à une couleur déjà choisie (voir le libellé
// conditionnel "Choisir une couleur"/"Couleur" ci-dessous, qui lève l'ambiguïté) —
// jamais persisté tel quel (voir ModpackProfile.color, qui reste `null` jusqu'à un
// choix explicite).
const UNSET_PICKER_COLOR = "#6d7178";

// Mêmes formes que ModWrite/BepinexConfig dans ModsPage.tsx (pas partagées entre pages,
// comme le reste des types représentant la forme JSON échangée avec l'API/le Rust).
interface ModFull {
  name: string;
  version: string;
  downloadUrl: string;
  sha256: string;
  description: string;
  category: string;
  dependencies: string[];
  iconUrl: string;
  adminOnly: boolean;
}

interface BepinexFull {
  url: string;
  sha256: string;
  version: string;
  description: string;
  iconUrl: string;
}

interface ProfilesPageProps {
  // Profil actuellement ciblé par "Jouer"/"Mettre à jour"/"Réparer" et par l'éditeur
  // de mods — voir App.tsx. Reste toujours "PRODUCTION_MODPACK_SLUG" pour un joueur
  // normal, cette page n'étant de toute façon jamais visible pour lui.
  activeSlug: string;
  // Reçoit aussi la couleur (voir ModpackProfile.color) pour que le badge de la
  // playbar puisse se teinter immédiatement — appelé à la fois pour changer de
  // profil actif et pour rafraîchir sa couleur après une modification (voir
  // handleColorChange), le slug pouvant alors rester le même.
  onSelect: (profile: { slug: string; color: string | null }) => void;
  // Appelé après une copie réussie, pour redéclencher immédiatement
  // `check_update_available` côté App.tsx si le profil actif vient de changer de
  // contenu — même principe que ModsPage.onModpackUpdated.
  onModpackUpdated: () => void;
}

type LoadState = { kind: "loading" } | { kind: "error"; message: string } | { kind: "loaded" };
type ActionState = { kind: "idle" } | { kind: "busy" } | { kind: "error"; message: string };

// Même règle que côté API (voir routes.ts::slugSchema) — validée aussi ici pour un
// message d'erreur immédiat plutôt qu'un aller-retour réseau pour une faute de frappe.
const SLUG_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;

type ModDiffEntry =
  | { kind: "added"; name: string; toVersion: string }
  | { kind: "removed"; name: string; fromVersion: string }
  | { kind: "updated"; name: string; fromVersion: string; toVersion: string };

type BepinexDiff =
  | { kind: "skipped" } // Non configuré côté source — jamais copié ni effacé côté destination.
  | { kind: "unchanged" }
  | { kind: "added"; toVersion: string }
  | { kind: "updated"; fromVersion: string; toVersion: string };

// La copie remplace en bloc le modpack de destination par celui de la source — même
// logique que "PUT /modpacks/:slug/mods" (jamais une fusion). Comparé par nom (comme le
// matching createdAt/updatedAt côté API), pas par contenu exact, pour distinguer "mod
// ajouté/retiré" de "mod mis à jour" (fichier ou version différents).
function diffMods(source: ModFull[], target: ModFull[]): ModDiffEntry[] {
  const key = (name: string) => name.trim().toLowerCase();
  const targetByName = new Map(target.map((m) => [key(m.name), m]));
  const sourceByName = new Map(source.map((m) => [key(m.name), m]));
  const entries: ModDiffEntry[] = [];

  for (const mod of source) {
    const existing = targetByName.get(key(mod.name));
    if (!existing) {
      entries.push({ kind: "added", name: mod.name, toVersion: mod.version });
    } else if (existing.sha256 !== mod.sha256 || existing.version !== mod.version) {
      entries.push({
        kind: "updated",
        name: mod.name,
        fromVersion: existing.version,
        toVersion: mod.version,
      });
    }
  }
  for (const mod of target) {
    if (!sourceByName.has(key(mod.name))) {
      entries.push({ kind: "removed", name: mod.name, fromVersion: mod.version });
    }
  }
  return entries;
}

function diffBepinex(source: BepinexFull | null, target: BepinexFull | null): BepinexDiff {
  if (!source) return { kind: "skipped" };
  if (!target) return { kind: "added", toVersion: source.version || "?" };
  if (target.sha256 === source.sha256) return { kind: "unchanged" };
  return { kind: "updated", fromVersion: target.version || "?", toVersion: source.version || "?" };
}

type CopyState =
  | { kind: "idle" }
  | { kind: "loading-diff" }
  | {
      kind: "previewing";
      modDiff: ModDiffEntry[];
      bepinexDiff: BepinexDiff;
      sourceMods: ModFull[];
      sourceBepinex: BepinexFull | null;
    }
  | { kind: "applying" }
  | { kind: "done" }
  | { kind: "error"; message: string };

export function ProfilesPage({ activeSlug, onSelect, onModpackUpdated }: ProfilesPageProps) {
  const [profiles, setProfiles] = useState<ModpackProfile[]>([]);
  const [state, setState] = useState<LoadState>({ kind: "loading" });

  const [renamingSlug, setRenamingSlug] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState("");
  const [actionState, setActionState] = useState<ActionState>({ kind: "idle" });

  const [newSlug, setNewSlug] = useState("");
  const [newName, setNewName] = useState("");
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  const [fromSlug, setFromSlug] = useState("");
  const [toSlug, setToSlug] = useState("");
  const [copyState, setCopyState] = useState<CopyState>({ kind: "idle" });

  // Jeton révélé (voir "Voir le jeton"/"Régénérer") — gardé seulement en mémoire, pas
  // rechargé automatiquement : un admin qui quitte cette page et y revient doit le
  // redemander explicitement, comme un secret de type "vu une seule fois".
  const [revealedTokens, setRevealedTokens] = useState<Record<string, string>>({});
  const [tokenBusySlug, setTokenBusySlug] = useState<string | null>(null);
  const [tokenError, setTokenError] = useState<string | null>(null);
  // Retour visuel "Copié !" sur le bouton, effacé après un court délai — voir
  // handleCopyToken.
  const [copiedSlug, setCopiedSlug] = useState<string | null>(null);

  // Édition de la cible d'auto-connexion (voir FedoServerTools) — brouillon local tant que
  // non enregistré, même principe que renamingSlug/renameDraft ci-dessus.
  const [autoConnectSlug, setAutoConnectSlug] = useState<string | null>(null);
  const [autoConnectDraft, setAutoConnectDraft] = useState<{
    type: "none" | "world" | "server";
    world: string;
    host: string;
    port: string;
    password: string;
  }>({ type: "none", world: "", host: "", port: "", password: "" });
  const [autoConnectBusy, setAutoConnectBusy] = useState(false);
  const [autoConnectError, setAutoConnectError] = useState<string | null>(null);

  function loadProfiles() {
    setState({ kind: "loading" });
    invoke<ModpackProfile[]>("list_modpacks")
      .then((fetched) => {
        setProfiles(fetched);
        setState({ kind: "loaded" });
        // Préremplit la source/destination une seule fois (pas à chaque rechargement,
        // sinon un admin qui vient de changer sa sélection la verrait réinitialisée
        // après une action sans rapport, comme un renommage).
        setFromSlug((prev) => prev || fetched[0]?.slug || "");
        setToSlug((prev) => prev || fetched.find((p) => p.slug !== fetched[0]?.slug)?.slug || "");
      })
      .catch((err) => setState({ kind: "error", message: String(err) }));
  }

  useEffect(() => {
    loadProfiles();
  }, []);

  async function handleCreate() {
    const slug = newSlug.trim().toLowerCase();
    const name = newName.trim();
    if (!name) {
      setCreateError("Le nom est requis.");
      return;
    }
    if (!SLUG_PATTERN.test(slug)) {
      setCreateError("Le slug ne peut contenir que des lettres, chiffres et tirets.");
      return;
    }
    setCreating(true);
    setCreateError(null);
    try {
      await invoke<ModpackProfile>("create_modpack", { slug, name });
      setNewSlug("");
      setNewName("");
      loadProfiles();
    } catch (err) {
      setCreateError(String(err));
    } finally {
      setCreating(false);
    }
  }

  function startRename(profile: ModpackProfile) {
    setRenamingSlug(profile.slug);
    setRenameDraft(profile.name);
    setActionState({ kind: "idle" });
  }

  async function confirmRename() {
    const slug = renamingSlug;
    const name = renameDraft.trim();
    if (!slug || !name) return;
    setActionState({ kind: "busy" });
    try {
      await invoke("rename_modpack", { slug, name });
      setRenamingSlug(null);
      loadProfiles();
    } catch (err) {
      setActionState({ kind: "error", message: String(err) });
    }
  }

  async function handleDelete(profile: ModpackProfile) {
    const confirmed = await confirm(
      `Supprimer le profil "${profile.name}" et ses ${profile.modCount} mod(s) ? Cette action est irréversible.`,
    );
    if (!confirmed) return;
    setActionState({ kind: "busy" });
    try {
      await invoke("delete_modpack", { slug: profile.slug });
      if (activeSlug === profile.slug) onSelect({ slug: PRODUCTION_MODPACK_SLUG, color: null });
      loadProfiles();
      setActionState({ kind: "idle" });
    } catch (err) {
      setActionState({ kind: "error", message: String(err) });
    }
  }

  // `null` réinitialise (retour à l'apparence par défaut) — voir UNSET_PICKER_COLOR
  // pour la distinction entre "aucune couleur choisie" et la valeur de départ du
  // widget. Si c'est le profil actif, `onSelect` est rappelé pour que le thème global
  // reflète le changement immédiatement, sans changer de profil.
  async function handleColorChange(profile: ModpackProfile, color: string | null) {
    setActionState({ kind: "busy" });
    try {
      await invoke("set_modpack_color", { slug: profile.slug, color });
      if (activeSlug === profile.slug) onSelect({ slug: profile.slug, color });
      loadProfiles();
      setActionState({ kind: "idle" });
    } catch (err) {
      setActionState({ kind: "error", message: String(err) });
    }
  }

  async function handleRevealToken(slug: string) {
    setTokenBusySlug(slug);
    setTokenError(null);
    try {
      const token = await invoke<string | null>("fetch_report_token", { slug });
      if (token) {
        setRevealedTokens((prev) => ({ ...prev, [slug]: token }));
      } else {
        setTokenError("Aucun jeton généré pour ce profil pour l'instant.");
      }
    } catch (err) {
      setTokenError(String(err));
    } finally {
      setTokenBusySlug(null);
    }
  }

  async function handleCopyToken(slug: string) {
    const token = revealedTokens[slug];
    if (!token) return;
    try {
      await navigator.clipboard.writeText(token);
      setCopiedSlug(slug);
      setTimeout(() => setCopiedSlug((prev) => (prev === slug ? null : prev)), 2000);
    } catch (err) {
      setTokenError(String(err));
    }
  }

  async function handleRegenerateToken(profile: ModpackProfile) {
    const message = profile.hasReportToken
      ? `Régénérer le jeton du profil "${profile.name}" ? L'ancien cessera immédiatement de fonctionner -- il faudra le remettre à jour dans le .cfg de FedoServerTools sur ce serveur.`
      : `Générer un jeton pour le profil "${profile.name}" ?`;
    const confirmed = await confirm(message);
    if (!confirmed) return;

    setTokenBusySlug(profile.slug);
    setTokenError(null);
    try {
      const token = await invoke<string>("regenerate_report_token", { slug: profile.slug });
      setRevealedTokens((prev) => ({ ...prev, [profile.slug]: token }));
      loadProfiles();
    } catch (err) {
      setTokenError(String(err));
    } finally {
      setTokenBusySlug(null);
    }
  }

  function startEditAutoConnect(profile: ModpackProfile) {
    const current = profile.autoConnect;
    setAutoConnectDraft({
      type: current?.type ?? "none",
      world: current?.type === "world" ? current.world : "",
      host: current?.type === "server" ? current.host : "",
      port: current?.type === "server" ? String(current.port) : "",
      password: current?.type === "server" ? current.password : "",
    });
    setAutoConnectError(null);
    setAutoConnectSlug(profile.slug);
  }

  async function saveAutoConnect() {
    const slug = autoConnectSlug;
    if (!slug) return;

    let autoConnect: AutoConnectTarget | null = null;
    if (autoConnectDraft.type === "world") {
      if (!autoConnectDraft.world.trim()) {
        setAutoConnectError("Le nom du monde est requis.");
        return;
      }
      autoConnect = { type: "world", world: autoConnectDraft.world.trim() };
    } else if (autoConnectDraft.type === "server") {
      const port = Number(autoConnectDraft.port);
      if (!autoConnectDraft.host.trim() || !Number.isInteger(port) || port < 1 || port > 65535) {
        setAutoConnectError("Adresse et port (1-65535) valides requis.");
        return;
      }
      autoConnect = {
        type: "server",
        host: autoConnectDraft.host.trim(),
        port,
        password: autoConnectDraft.password,
      };
    }

    setAutoConnectBusy(true);
    setAutoConnectError(null);
    try {
      await invoke("set_modpack_auto_connect", { slug, autoConnect });
      setAutoConnectSlug(null);
      loadProfiles();
    } catch (err) {
      setAutoConnectError(String(err));
    } finally {
      setAutoConnectBusy(false);
    }
  }

  // Charge les deux modpacks complets (mods + BepInEx) et calcule ce qui changerait —
  // rien n'est écrit à ce stade, seulement affiché pour confirmation (voir
  // handleConfirmCopy).
  async function handlePreviewCopy() {
    if (!fromSlug || !toSlug || fromSlug === toSlug) return;
    setCopyState({ kind: "loading-diff" });
    try {
      const [sourceMods, targetMods, sourceBepinex, targetBepinex] = await Promise.all([
        invoke<ModFull[]>("fetch_mods_full", { slug: fromSlug }),
        invoke<ModFull[]>("fetch_mods_full", { slug: toSlug }),
        invoke<BepinexFull | null>("fetch_bepinex", { slug: fromSlug }),
        invoke<BepinexFull | null>("fetch_bepinex", { slug: toSlug }),
      ]);
      setCopyState({
        kind: "previewing",
        modDiff: diffMods(sourceMods, targetMods),
        bepinexDiff: diffBepinex(sourceBepinex, targetBepinex),
        sourceMods,
        sourceBepinex,
      });
    } catch (err) {
      setCopyState({ kind: "error", message: String(err) });
    }
  }

  async function handleConfirmCopy() {
    if (copyState.kind !== "previewing") return;
    const { sourceMods, sourceBepinex } = copyState;
    setCopyState({ kind: "applying" });
    try {
      await invoke("save_mods", { slug: toSlug, mods: sourceMods });
      if (sourceBepinex) {
        await invoke("save_bepinex", { slug: toSlug, bepinex: sourceBepinex });
      }
      setCopyState({ kind: "done" });
      loadProfiles();
      onModpackUpdated();
    } catch (err) {
      setCopyState({ kind: "error", message: String(err) });
    }
  }

  const fromProfile = profiles.find((p) => p.slug === fromSlug);
  const toProfile = profiles.find((p) => p.slug === toSlug);
  const previewing = copyState.kind === "previewing";
  const hasChanges =
    previewing && (copyState.modDiff.length > 0 || copyState.bepinexDiff.kind !== "unchanged");

  return (
    <div className="profiles-page">
      <header className="profiles-page__header">
        <h1>Profils de modpack</h1>
        <p>
          Le profil "Production" est celui reçu par tout joueur normal. Crée un autre
          profil pour tester un modpack (sur un serveur Valheim séparé) avant de le
          répliquer en production — le bouton "Jouer" cible toujours le profil actif
          ci-dessous, en mode Joueur ou Admin comme d'habitude.
        </p>
      </header>

      {state.kind === "loading" && <p className="profiles-page__status">Chargement...</p>}
      {state.kind === "error" && (
        <p className="profiles-page__status is-error">{state.message}</p>
      )}

      {state.kind === "loaded" && (
        <ul className="profiles-list">
          {profiles.map((profile) => {
            const isActive = profile.slug === activeSlug;
            // Jamais pour le profil production, qui n'expose de toute façon jamais le
            // sélecteur de couleur — voir ModpackProfile.color.
            const tint = !profile.isDefault ? profile.color : null;
            return (
              <li
                key={profile.slug}
                className={`profiles-list__item ${isActive ? "is-active" : ""}`}
                style={tint ? { borderColor: tint } : undefined}
              >
                <div className="profiles-list__main">
                  {renamingSlug === profile.slug ? (
                    <input
                      className="profiles-list__rename-input"
                      value={renameDraft}
                      autoFocus
                      onChange={(e) => setRenameDraft(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") confirmRename();
                        if (e.key === "Escape") setRenamingSlug(null);
                      }}
                    />
                  ) : (
                    <span className="profiles-list__name">{profile.name}</span>
                  )}
                  <span className="profiles-list__slug">{profile.slug}</span>
                  {profile.isDefault && (
                    <span className="profiles-list__badge">Production</span>
                  )}
                  {isActive && (
                    <span
                      className="profiles-list__badge profiles-list__badge--accent"
                      style={tint ? { background: hexToRgba(tint, 0.18), color: tint } : undefined}
                    >
                      Profil actif
                    </span>
                  )}
                </div>
                <p className="profiles-list__meta">
                  {profile.modCount} mod{profile.modCount > 1 ? "s" : ""} — mis à jour le{" "}
                  {formatDate(profile.updatedAt)}
                </p>

                <div className="profiles-list__report-token">
                  <span className="profiles-list__report-token-label">
                    Jeton serveur : {profile.hasReportToken ? "configuré" : "aucun"}
                  </span>
                  {revealedTokens[profile.slug] ? (
                    <>
                      <input
                        className="profiles-list__report-token-value"
                        readOnly
                        value={revealedTokens[profile.slug]}
                        onFocus={(e) => e.target.select()}
                      />
                      <button
                        type="button"
                        className="btn btn--ghost"
                        onClick={() => handleCopyToken(profile.slug)}
                      >
                        {copiedSlug === profile.slug ? "Copié !" : "Copier"}
                      </button>
                    </>
                  ) : (
                    profile.hasReportToken && (
                      <button
                        type="button"
                        className="btn btn--ghost"
                        onClick={() => handleRevealToken(profile.slug)}
                        disabled={tokenBusySlug === profile.slug}
                      >
                        Voir le jeton
                      </button>
                    )
                  )}
                  <button
                    type="button"
                    className="btn btn--ghost"
                    onClick={() => handleRegenerateToken(profile)}
                    disabled={tokenBusySlug === profile.slug}
                  >
                    {profile.hasReportToken ? "Régénérer le jeton" : "Générer un jeton"}
                  </button>
                </div>

                <div className="profiles-list__auto-connect">
                  {autoConnectSlug === profile.slug ? (
                    <>
                      <select
                        value={autoConnectDraft.type}
                        onChange={(e) =>
                          setAutoConnectDraft((prev) => ({
                            ...prev,
                            type: e.target.value as "none" | "world" | "server",
                          }))
                        }
                      >
                        <option value="none">Aucune (menu Valheim normal)</option>
                        <option value="world">Monde local à héberger</option>
                        <option value="server">Serveur dédié à rejoindre</option>
                      </select>
                      {autoConnectDraft.type === "world" && (
                        <input
                          placeholder="Nom du monde (ex: fedodev3)"
                          value={autoConnectDraft.world}
                          onChange={(e) =>
                            setAutoConnectDraft((prev) => ({ ...prev, world: e.target.value }))
                          }
                        />
                      )}
                      {autoConnectDraft.type === "server" && (
                        <>
                          <input
                            placeholder="Adresse (IP ou nom d'hôte)"
                            value={autoConnectDraft.host}
                            onChange={(e) =>
                              setAutoConnectDraft((prev) => ({ ...prev, host: e.target.value }))
                            }
                          />
                          <input
                            placeholder="Port"
                            value={autoConnectDraft.port}
                            onChange={(e) =>
                              setAutoConnectDraft((prev) => ({ ...prev, port: e.target.value }))
                            }
                          />
                          <input
                            placeholder="Mot de passe (optionnel)"
                            value={autoConnectDraft.password}
                            onChange={(e) =>
                              setAutoConnectDraft((prev) => ({ ...prev, password: e.target.value }))
                            }
                          />
                        </>
                      )}
                      <button
                        type="button"
                        className="btn btn--accent"
                        onClick={saveAutoConnect}
                        disabled={autoConnectBusy}
                      >
                        Enregistrer
                      </button>
                      <button
                        type="button"
                        className="btn btn--ghost"
                        onClick={() => setAutoConnectSlug(null)}
                        disabled={autoConnectBusy}
                      >
                        Annuler
                      </button>
                      {autoConnectError && (
                        <p className="profiles-page__status is-error">{autoConnectError}</p>
                      )}
                    </>
                  ) : (
                    <>
                      <span className="profiles-list__report-token-label">
                        Connexion auto :{" "}
                        {profile.autoConnect === null
                          ? "aucune"
                          : profile.autoConnect.type === "world"
                            ? `monde "${profile.autoConnect.world}"`
                            : `serveur ${profile.autoConnect.host}:${profile.autoConnect.port}`}
                      </span>
                      <button
                        type="button"
                        className="btn btn--ghost"
                        onClick={() => startEditAutoConnect(profile)}
                      >
                        Configurer
                      </button>
                    </>
                  )}
                </div>

                <div className="profiles-list__actions">
                  {renamingSlug === profile.slug ? (
                    <>
                      <button
                        type="button"
                        className="btn btn--accent"
                        onClick={confirmRename}
                        disabled={actionState.kind === "busy" || !renameDraft.trim()}
                      >
                        Enregistrer
                      </button>
                      <button
                        type="button"
                        className="btn btn--ghost"
                        onClick={() => setRenamingSlug(null)}
                      >
                        Annuler
                      </button>
                    </>
                  ) : (
                    <>
                      {!isActive && (
                        <button
                          type="button"
                          className="btn btn--accent"
                          onClick={() => onSelect({ slug: profile.slug, color: profile.color })}
                        >
                          Utiliser ce profil
                        </button>
                      )}
                      <button
                        type="button"
                        className="btn btn--ghost"
                        onClick={() => startRename(profile)}
                      >
                        Renommer
                      </button>
                      {!profile.isDefault && (
                        <label
                          className={`profiles-list__color-picker ${
                            profile.color ? "" : "is-unset"
                          }`}
                          title="Couleur du profil (reteinte tout le launcher tant que ce profil est actif)"
                        >
                          <input
                            type="color"
                            value={profile.color ?? UNSET_PICKER_COLOR}
                            disabled={actionState.kind === "busy"}
                            onChange={(e) => handleColorChange(profile, e.target.value)}
                          />
                          {profile.color ? "Couleur" : "Choisir une couleur"}
                        </label>
                      )}
                      {!profile.isDefault && profile.color && (
                        <button
                          type="button"
                          className="btn btn--ghost"
                          onClick={() => handleColorChange(profile, null)}
                          disabled={actionState.kind === "busy"}
                        >
                          Réinitialiser la couleur
                        </button>
                      )}
                      <button
                        type="button"
                        className="btn btn--ghost profiles-list__delete"
                        onClick={() => handleDelete(profile)}
                        disabled={profile.isDefault || actionState.kind === "busy"}
                        title={
                          profile.isDefault
                            ? "Le profil production ne peut pas être supprimé"
                            : undefined
                        }
                      >
                        Supprimer
                      </button>
                    </>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}

      {actionState.kind === "error" && (
        <p className="profiles-page__status is-error">{actionState.message}</p>
      )}
      {tokenError && <p className="profiles-page__status is-error">{tokenError}</p>}

      {state.kind === "loaded" && profiles.length >= 2 && (
        <div className="profiles-copy">
          <h2>Copier un modpack d'un profil à l'autre</h2>
          <p className="profiles-page__hint">
            Remplace en bloc les mods et la config BepInEx du profil de destination par
            ceux de la source — dans n'importe quel sens (ex: du profil de test vers la
            production, ou l'inverse pour repartir d'une base propre). Un récapitulatif
            des changements est affiché avant toute écriture.
          </p>
          <div className="profiles-copy__row">
            <select
              className="profiles-copy__select"
              value={fromSlug}
              onChange={(e) => setFromSlug(e.target.value)}
            >
              {profiles.map((p) => (
                <option key={p.slug} value={p.slug}>
                  {p.name}
                  {p.isDefault ? " (Production)" : ""}
                </option>
              ))}
            </select>
            <span className="profiles-copy__arrow" aria-hidden="true">
              →
            </span>
            <select
              className="profiles-copy__select"
              value={toSlug}
              onChange={(e) => setToSlug(e.target.value)}
            >
              {profiles.map((p) => (
                <option key={p.slug} value={p.slug}>
                  {p.name}
                  {p.isDefault ? " (Production)" : ""}
                </option>
              ))}
            </select>
            <button
              type="button"
              className="btn btn--accent"
              onClick={handlePreviewCopy}
              disabled={
                !fromSlug || !toSlug || fromSlug === toSlug || copyState.kind === "loading-diff"
              }
            >
              {copyState.kind === "loading-diff" ? "Comparaison..." : "Comparer"}
            </button>
          </div>
          {fromSlug === toSlug && (
            <p className="profiles-page__hint">Choisis deux profils différents.</p>
          )}
          {copyState.kind === "error" && (
            <p className="profiles-page__status is-error">{copyState.message}</p>
          )}
          {copyState.kind === "done" && (
            <p className="profiles-page__status is-success">
              Copié vers "{toProfile?.name ?? toSlug}".
            </p>
          )}
        </div>
      )}

      {previewing && fromProfile && toProfile && (
        <div
          className="copy-modal-overlay"
          role="presentation"
          onClick={() => setCopyState({ kind: "idle" })}
        >
          <div
            className="copy-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="copy-modal-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 id="copy-modal-title">
              Copier "{fromProfile.name}" → "{toProfile.name}"
            </h2>

            {toProfile.isDefault && (
              <p className="copy-modal__warning">
                ⚠️ La destination est le profil production — tout joueur normal recevra
                ce contenu à sa prochaine synchronisation.
              </p>
            )}

            {!hasChanges ? (
              <p className="copy-modal__hint">
                Aucune différence — les deux profils sont déjà identiques.
              </p>
            ) : (
              <div className="copy-modal__list">
                {copyState.modDiff
                  .filter((d) => d.kind === "added")
                  .map((d) => (
                    <p key={`added-${d.name}`} className="copy-modal__entry is-added">
                      + {d.name} <span>(v{d.toVersion})</span>
                    </p>
                  ))}
                {copyState.modDiff
                  .filter((d) => d.kind === "removed")
                  .map((d) => (
                    <p key={`removed-${d.name}`} className="copy-modal__entry is-removed">
                      − {d.name} <span>(v{d.fromVersion})</span>
                    </p>
                  ))}
                {copyState.modDiff
                  .filter((d) => d.kind === "updated")
                  .map((d) => (
                    <p key={`updated-${d.name}`} className="copy-modal__entry is-updated">
                      ~ {d.name}{" "}
                      <span>
                        (v{d.fromVersion} → v{d.toVersion})
                      </span>
                    </p>
                  ))}
                {copyState.bepinexDiff.kind === "added" && (
                  <p className="copy-modal__entry is-added">
                    + BepInEx <span>(v{copyState.bepinexDiff.toVersion})</span>
                  </p>
                )}
                {copyState.bepinexDiff.kind === "updated" && (
                  <p className="copy-modal__entry is-updated">
                    ~ BepInEx{" "}
                    <span>
                      (v{copyState.bepinexDiff.fromVersion} → v{copyState.bepinexDiff.toVersion})
                    </span>
                  </p>
                )}
                {copyState.bepinexDiff.kind === "skipped" && (
                  <p className="copy-modal__entry is-skipped">
                    BepInEx non configuré sur la source — pas copié, la destination garde
                    le sien.
                  </p>
                )}
              </div>
            )}

            <div className="copy-modal__actions">
              <button
                type="button"
                className="btn btn--ghost"
                onClick={() => setCopyState({ kind: "idle" })}
              >
                Annuler
              </button>
              <button
                type="button"
                className="btn btn--accent"
                onClick={handleConfirmCopy}
                disabled={!hasChanges}
              >
                Confirmer la copie
              </button>
            </div>
          </div>
        </div>
      )}

      {copyState.kind === "applying" && (
        <div className="copy-modal-overlay" role="presentation">
          <div className="copy-modal">
            <p className="copy-modal__hint">Copie en cours...</p>
          </div>
        </div>
      )}

      <div className="profiles-create">
        <h2>Nouveau profil</h2>
        <div className="profiles-create__row">
          <input
            className="profiles-create__input"
            placeholder="Nom (ex: Serveur de test)"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
          />
          <input
            className="profiles-create__input profiles-create__input--slug"
            placeholder="slug (ex: test-server)"
            value={newSlug}
            onChange={(e) => setNewSlug(e.target.value)}
          />
          <button
            type="button"
            className="btn btn--accent"
            onClick={handleCreate}
            disabled={creating || !newName.trim() || !newSlug.trim()}
          >
            {creating ? "Création..." : "Créer"}
          </button>
        </div>
        {createError && <p className="profiles-page__status is-error">{createError}</p>}
      </div>
    </div>
  );
}
