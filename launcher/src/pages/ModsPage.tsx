import { useEffect, useMemo, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { confirm } from "@tauri-apps/plugin-dialog";
import { PRODUCTION_MODPACK_SLUG } from "../data/mock";
import { getApiBaseUrl } from "../utils/apiBaseUrl";
import { formatDate } from "../utils/date";
import "./ModsPage.css";

interface ModInfo {
  name: string;
  version: string;
  description: string;
  category: string;
  iconUrl: string;
  // Absent de la réponse publique (GET /mods, jamais de mods admin dedans) — présent
  // seulement quand la liste est chargée via fetch_mods_full pour un admin, voir
  // loadMods ci-dessous.
  adminOnly?: boolean;
  // Idem : un mod désactivé n'apparaît jamais dans la réponse publique (voir
  // ModWrite.enabled), donc toujours `true`/absent ici sauf via fetch_mods_full.
  enabled?: boolean;
}

interface ModWrite {
  name: string;
  version: string;
  downloadUrl: string;
  sha256: string;
  description: string;
  category: string;
  dependencies: string[];
  iconUrl: string;
  // Réservé au modpack "Admin" (voir CLAUDE.md) — invisible du modpack "Joueur" et de
  // la liste publique quand coché.
  adminOnly: boolean;
  // Décoché = désactivé pour tout le monde (joueur comme admin) : absent du manifest et
  // de la liste publique, mais la fiche reste en base pour être réactivée plus tard.
  enabled: boolean;
  // Gérés par l'API, jamais saisis à la main — voir CLAUDE.md. `null` pour un mod pas
  // encore enregistré (vient d'être ajouté dans ce brouillon).
  createdAt: string | null;
  updatedAt: string | null;
}

interface ConfigFileWrite {
  filename: string;
  downloadUrl: string;
  sha256: string;
  updatedAt: string | null;
}

interface ConfigFileUpload {
  url: string;
  sha256: string;
  filename: string;
}

interface ConfigFileBulkUpload {
  uploads: ConfigFileUpload[];
  errors: string[];
}

interface FileUpload {
  url: string;
  sha256: string;
  version: string | null;
  name: string | null;
  description: string | null;
  dependencies: string[];
  iconUrl: string | null;
}

interface BulkUpload {
  uploads: FileUpload[];
  errors: string[];
}

interface BepinexConfig {
  url: string;
  sha256: string;
  version: string;
  description: string;
  iconUrl: string;
}

// Vu par tout le monde (BepInEx est un mod comme un autre pour le joueur), contrairement
// à BepinexConfig ci-dessus qui inclut url/sha256 et n'est chargé que pour un admin.
interface BepinexStatus {
  configured: boolean;
  version: string | null;
  description: string | null;
  iconUrl: string | null;
}

interface ModsPageProps {
  // Profil de modpack édité/affiché (voir ProfilesPage) — toujours le profil
  // production pour un non-admin, voir App.tsx::effectiveModpackSlug.
  slug: string;
  isAdmin: boolean;
  // Signale à App.tsx qu'une édition est en cours (pour confirmer avant de changer de
  // page ou de fermer le launcher) — voir onCancelEdit ci-dessous pour le nettoyage des
  // fichiers importés mais jamais enregistrés.
  onDirtyChange: (dirty: boolean) => void;
  // Appelé juste après un enregistrement réussi (mods ou BepInEx) pour redéclencher
  // immédiatement `check_update_available` côté App.tsx — sinon un admin qui vient
  // d'enregistrer devrait attendre le prochain check périodique pour voir le bouton
  // "Jouer" se scinder en "Mettre à jour", même sur son propre launcher.
  onModpackUpdated: () => void;
}

const CATEGORY_CLASS: Record<string, string> = {
  Gameplay: "is-gameplay",
  QoL: "is-qol",
  Visuel: "is-visuel",
  Serveur: "is-serveur",
};

const ALL_CATEGORIES = "Tous";
// Onglet pseudo-catégorie (pas une vraie catégorie de mod, voir `categories`
// ci-dessous) qui filtre uniquement les mods "admin only", visible seulement pour un
// admin et seulement s'il en existe au moins un — indépendant de la catégorie "libre"
// choisie pour chaque mod (un mod admin peut être rangé dans n'importe quelle
// catégorie, ex: "Gameplay").
const ADMIN_ONLY_TAB = "Réservé admin";

type LoadState = { kind: "loading" } | { kind: "error"; message: string } | { kind: "loaded" };
type SaveState = { kind: "idle" } | { kind: "saving" } | { kind: "error"; message: string };
type BepinexState =
  | { kind: "idle" }
  | { kind: "uploading" }
  | { kind: "error"; message: string };

const EMPTY_MOD: ModWrite = {
  name: "",
  version: "",
  downloadUrl: "",
  sha256: "",
  description: "",
  category: "Gameplay",
  dependencies: [],
  iconUrl: "",
  adminOnly: false,
  enabled: true,
  createdAt: null,
  updatedAt: null,
};

// `version`/`dependencies` viennent du zip qu'on vient de choisir pour ce mod —
// toujours à jour avec le fichier. `name`/`description` ne sont remplis que si le champ
// est vide, pour ne jamais écraser une fiche déjà personnalisée (ex: description
// traduite en français). `iconUrl` garde la précédente si la nouvelle archive n'en a
// pas (ex: mise à jour vers un zip qui a oublié l'icône).
function applyUpload(mod: ModWrite, upload: FileUpload): ModWrite {
  return {
    ...mod,
    downloadUrl: upload.url,
    sha256: upload.sha256,
    version: upload.version ?? mod.version,
    dependencies: upload.dependencies,
    iconUrl: upload.iconUrl ?? mod.iconUrl,
    name: mod.name.trim() ? mod.name : (upload.name ?? mod.name),
    description: mod.description.trim() ? mod.description : (upload.description ?? mod.description),
  };
}

// Un identifiant de dépendance Thunderstore ressemble à "Auteur-NomDuPackage-Version" —
// on isole le nom du package (en ignorant auteur/version) pour le comparer, en
// insensible à la casse et aux espaces/tirets, aux mods déjà configurés dans ce
// modpack.
function normalizePackageName(name: string): string {
  return name.toLowerCase().replace(/[^a-z0-9]/g, "");
}

function dependencyPackageName(dependency: string): string {
  const parts = dependency.split("-");
  const middle = parts.length >= 3 ? parts.slice(1, -1).join("-") : dependency;
  return normalizePackageName(middle);
}

// BepInEx lui-même est une dépendance très courante (quasi tous les mods BepInEx en
// listent une variante) — vérifiée contre la config BepInEx du modpack plutôt que
// contre la liste des mods.
function isDependencySatisfied(
  dependency: string,
  draft: ModWrite[],
  bepinexConfigured: boolean,
): boolean {
  const pkg = dependencyPackageName(dependency);
  if (pkg.includes("bepinex")) {
    return bepinexConfigured;
  }
  return draft.some((mod) => normalizePackageName(mod.name) === pkg);
}

export function ModsPage({ slug, isAdmin, onDirtyChange, onModpackUpdated }: ModsPageProps) {
  const [mods, setMods] = useState<ModInfo[]>([]);
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [activeCategory, setActiveCategory] = useState(ALL_CATEGORIES);

  const [editing, setEditing] = useState(false);
  // Sous-onglet de l'éditeur — sépare mods et fichiers de config pour ne pas tout
  // afficher à la suite (les deux sections mélangées obligeaient à scroller loin pour
  // retrouver les boutons "+ Ajouter" de l'une ou l'autre).
  const [editorView, setEditorView] = useState<"mods" | "config">("mods");
  // Recherche par nom (mods) / nom de fichier (config) dans l'onglet actif — remise à
  // zéro en changeant d'onglet pour ne pas laisser un filtre de l'un masquer l'autre.
  const [editorSearch, setEditorSearch] = useState("");
  const [draft, setDraft] = useState<ModWrite[]>([]);
  // Instantané de draft/configFiles pris au chargement de l'éditeur — comparé au
  // contenu courant pour savoir si "Enregistrer" a une raison d'être affiché (voir
  // hasChanges plus bas). Clone profond nécessaire : draft/configFiles sont réassignés
  // avec de nouveaux objets à chaque édition, mais on veut figer l'état de référence.
  const [initialDraft, setInitialDraft] = useState<ModWrite[]>([]);
  const [initialConfigFiles, setInitialConfigFiles] = useState<ConfigFileWrite[]>([]);
  const [editLoadError, setEditLoadError] = useState<string | null>(null);
  // Séparés : un changement sur l'onglet Mods s'enregistre depuis l'onglet Mods, un
  // changement sur l'onglet Fichiers de config s'enregistre depuis cet onglet-là — pas
  // un bouton "Enregistrer" global qui écrirait les deux à chaque clic.
  const [modsSaveState, setModsSaveState] = useState<SaveState>({ kind: "idle" });
  const [configSaveState, setConfigSaveState] = useState<SaveState>({ kind: "idle" });
  const [pickingIndex, setPickingIndex] = useState<number | null>(null);
  const [addingMod, setAddingMod] = useState(false);
  // Fichiers (zip + icône) uploadés à l'API pendant cette session d'édition — si
  // "Annuler" est cliqué, ce sont des candidats à la suppression (voir handleCancelEdit)
  // puisqu'ils ne seront référencés par aucun mod enregistré.
  const [sessionUploads, setSessionUploads] = useState<FileUpload[]>([]);

  // Fichiers de config bruts (ex: FastLink.cfg pré-rempli avec l'adresse/mdp du
  // serveur), copiés tels quels dans BepInEx/config/ par le launcher — indépendants de
  // tout mod, voir CLAUDE.md. `sessionConfigFileUploads` : mêmes URLs que
  // `sessionUploads` ci-dessus mais pour ces fichiers, suivies séparément pour ne pas
  // forcer le typage `FileUpload` (zip) sur un upload de fichier brut.
  const [configFiles, setConfigFiles] = useState<ConfigFileWrite[]>([]);
  const [pickingConfigFile, setPickingConfigFile] = useState(false);
  // Index du fichier de config en cours de remplacement (voir handleReplaceConfigFile) —
  // distinct de `pickingConfigFile` ci-dessus, qui ne sert qu'au bouton "+ Ajouter".
  const [pickingConfigFileIndex, setPickingConfigFileIndex] = useState<number | null>(null);
  const [sessionConfigFileUploads, setSessionConfigFileUploads] = useState<string[]>([]);
  // Édition inline du contenu d'un fichier de config (voir "Éditer" ci-dessous) — un seul
  // à la fois, `editingConfigFileIndex` pointe vers son index dans `configFiles`.
  const [editingConfigFileIndex, setEditingConfigFileIndex] = useState<number | null>(null);
  const [configFileContent, setConfigFileContent] = useState("");
  const [configFileContentState, setConfigFileContentState] = useState<
    { kind: "idle" } | { kind: "loading" } | { kind: "saving" } | { kind: "error"; message: string }
  >({ kind: "idle" });

  const [bepinex, setBepinex] = useState<BepinexConfig | null>(null);
  const [bepinexStatus, setBepinexStatus] = useState<BepinexStatus | null>(null);
  const [bepinexState, setBepinexState] = useState<BepinexState>({ kind: "idle" });

  const [apiBaseUrl, setApiBaseUrl] = useState("");

  useEffect(() => {
    loadMods();
    getApiBaseUrl().then(setApiBaseUrl);
  }, [isAdmin, slug]);

  // L'admin a besoin de url/sha256 pour préremplir l'éditeur ; un joueur n'a besoin que
  // de savoir si c'est configuré (voir BepinexStatus, endpoint public).
  useEffect(() => {
    if (isAdmin) {
      invoke<BepinexConfig | null>("fetch_bepinex", { slug })
        .then(setBepinex)
        .catch((err) => setBepinexState({ kind: "error", message: String(err) }));
    } else {
      invoke<BepinexStatus>("fetch_bepinex_status", { slug })
        .then(setBepinexStatus)
        .catch(() => {});
    }
  }, [isAdmin, slug]);

  const bepinexConfigured = isAdmin ? !!bepinex : !!bepinexStatus?.configured;
  const bepinexVersion = isAdmin ? bepinex?.version : bepinexStatus?.version;
  const bepinexDescription = isAdmin ? bepinex?.description : bepinexStatus?.description;
  const bepinexIconUrl = isAdmin ? bepinex?.iconUrl : bepinexStatus?.iconUrl;

  // Un admin voit aussi les mods "admin only" dans cette liste (badge "Admin", voir
  // rendu ci-dessous) — invisibles d'un joueur normal, voir CLAUDE.md. On réutilise
  // fetch_mods_full (déjà utilisé pour l'éditeur) plutôt que d'ajouter un endpoint : un
  // admin a de toute façon accès à ces champs via "Éditer".
  function loadMods() {
    setState({ kind: "loading" });
    const request = isAdmin
      ? invoke<ModWrite[]>("fetch_mods_full", { slug })
      : invoke<ModInfo[]>("fetch_mods", { slug });

    request
      .then((fetched) => {
        setMods(fetched);
        setState({ kind: "loaded" });
      })
      .catch((err) => setState({ kind: "error", message: String(err) }));
  }

  const categories = useMemo(() => {
    const unique = Array.from(new Set(mods.map((m) => m.category)));
    const tabs = [ALL_CATEGORIES, ...unique];
    if (isAdmin && mods.some((m) => m.adminOnly)) {
      tabs.push(ADMIN_ONLY_TAB);
    }
    return tabs;
  }, [mods, isAdmin]);

  // Catégories déjà utilisées dans le brouillon en cours d'édition, proposées via
  // <datalist> pour sélection — reste un champ texte libre (voir CLAUDE.md : taper une
  // nouvelle catégorie fait apparaître un nouvel onglet, pas de liste figée).
  const categoryOptions = useMemo(() => {
    return Array.from(new Set(draft.map((m) => m.category).filter(Boolean))).sort();
  }, [draft]);

  // Deux mods avec le même nom cassent le slug d'extraction côté launcher (les deux
  // s'installeraient dans le même dossier BepInEx/plugins/<slug>/) et l'affichage
  // public (clé de liste dupliquée) — jamais autorisé, ni à l'enregistrement ni en
  // silence.
  const duplicateNameKeys = useMemo(() => {
    const counts = new Map<string, number>();
    for (const mod of draft) {
      const key = normalizePackageName(mod.name);
      if (!key) continue;
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
    return new Set([...counts].filter(([, count]) => count > 1).map(([key]) => key));
  }, [draft]);

  // Dépendance manquante par mod, dans le même ordre que `draft` — un mod qui en manque
  // une ne s'installerait pas correctement chez le joueur (BepInEx ou un autre mod requis
  // absent du modpack), donc bloque l'enregistrement au même titre qu'un nom dupliqué
  // (voir handleSave) tant qu'elle n'est pas ajoutée ou que le mod n'est pas supprimé.
  const missingDepsByMod = useMemo(
    () =>
      draft.map((mod) =>
        mod.dependencies.filter((dep) => !isDependencySatisfied(dep, draft, bepinexConfigured)),
      ),
    [draft, bepinexConfigured],
  );

  const visibleMods =
    activeCategory === ALL_CATEGORIES
      ? mods
      : activeCategory === ADMIN_ONLY_TAB
        ? mods.filter((m) => m.adminOnly)
        : mods.filter((m) => m.category === activeCategory);

  async function handlePickBepinex() {
    setBepinexState({ kind: "uploading" });
    try {
      const upload = await invoke<FileUpload | null>("pick_zip_and_upload");
      if (!upload) {
        setBepinexState({ kind: "idle" });
        return;
      }
      const config: BepinexConfig = {
        url: upload.url,
        sha256: upload.sha256,
        version: upload.version ?? "",
        description: upload.description ?? "",
        iconUrl: upload.iconUrl ?? "",
      };
      await invoke("save_bepinex", { slug, bepinex: config });
      setBepinex(config);
      setBepinexState({ kind: "idle" });
      onModpackUpdated();
    } catch (err) {
      setBepinexState({ kind: "error", message: String(err) });
    }
  }

  async function startEditing() {
    setEditLoadError(null);
    setEditing(true);
    setEditorView("mods");
    setEditorSearch("");
    setSessionUploads([]);
    setSessionConfigFileUploads([]);
    setModsSaveState({ kind: "idle" });
    setConfigSaveState({ kind: "idle" });
    closeConfigFileEditor();
    try {
      const [full, files] = await Promise.all([
        invoke<ModWrite[]>("fetch_mods_full", { slug }),
        invoke<ConfigFileWrite[]>("fetch_config_files_full", { slug }),
      ]);
      setDraft(full);
      setConfigFiles(files);
      setInitialDraft(JSON.parse(JSON.stringify(full)));
      setInitialConfigFiles(JSON.parse(JSON.stringify(files)));
    } catch (err) {
      setEditLoadError(String(err));
    }
  }

  // Deux fichiers de config avec le même nom entreraient en collision au même chemin de
  // destination (BepInEx/config/<filename>) chez le joueur — bloqué à l'enregistrement,
  // même principe que duplicateNameKeys pour les mods.
  const duplicateConfigFilenames = useMemo(() => {
    const counts = new Map<string, number>();
    for (const file of configFiles) {
      const key = file.filename.trim().toLowerCase();
      if (!key) continue;
      counts.set(key, (counts.get(key) ?? 0) + 1);
    }
    return new Set([...counts].filter(([, count]) => count > 1).map(([key]) => key));
  }, [configFiles]);

  function updateConfigFileName(index: number, filename: string) {
    setConfigFiles((prev) => prev.map((f, i) => (i === index ? { ...f, filename } : f)));
  }

  async function removeConfigFile(index: number) {
    const name = configFiles[index]?.filename.trim();
    const confirmed = await confirm(
      name ? `Supprimer le fichier "${name}" ?` : "Supprimer ce fichier de config ?",
    );
    if (!confirmed) return;
    setConfigFiles((prev) => prev.filter((_, i) => i !== index));
    // Les index des entrées suivantes se décalent — plus simple de fermer l'éditeur
    // inline que de le suivre.
    if (editingConfigFileIndex !== null) {
      closeConfigFileEditor();
    }
  }

  // Sélecteur de fichier natif multi-sélection (pas de zip, des fichiers bruts déjà
  // prêts à être copiés dans BepInEx/config/) — même principe que handleAddMods : chaque
  // fichier est uploadé l'un après l'autre côté Rust (voir pick_config_files_and_upload),
  // un échec sur l'un n'annule pas les autres, juste remonté dans `errors`.
  async function handleAddConfigFile() {
    setPickingConfigFile(true);
    setEditLoadError(null);
    try {
      const result = await invoke<ConfigFileBulkUpload>("pick_config_files_and_upload");
      if (result.uploads.length > 0) {
        setConfigFiles((prev) => [
          ...prev,
          ...result.uploads.map((upload) => ({
            filename: upload.filename,
            downloadUrl: upload.url,
            sha256: upload.sha256,
            updatedAt: null,
          })),
        ]);
        setSessionConfigFileUploads((prev) => [...prev, ...result.uploads.map((u) => u.url)]);
      }
      if (result.errors.length > 0) {
        setEditLoadError(result.errors.join("\n"));
      }
    } catch (err) {
      setEditLoadError(String(err));
    } finally {
      setPickingConfigFile(false);
    }
  }

  // Remplace le contenu (downloadUrl/sha256) d'un fichier de config déjà dans le
  // brouillon, sans toucher au nom de destination déjà choisi (le fichier repris sur
  // disque peut avoir un nom différent, ex: "FastLink (1).cfg" — seul le nom de
  // destination dans BepInEx/config/ compte). Même sha256 différent = détecté comme
  // mise à jour côté joueur (voir manifest_needs_update).
  async function handleReplaceConfigFile(index: number) {
    setPickingConfigFileIndex(index);
    setEditLoadError(null);
    try {
      const upload = await invoke<ConfigFileUpload | null>("pick_config_file_and_upload");
      if (upload) {
        setConfigFiles((prev) =>
          prev.map((f, i) =>
            i === index ? { ...f, downloadUrl: upload.url, sha256: upload.sha256 } : f,
          ),
        );
        setSessionConfigFileUploads((prev) => [...prev, upload.url]);
      }
    } catch (err) {
      setEditLoadError(String(err));
    } finally {
      setPickingConfigFileIndex(null);
    }
  }

  // Charge le contenu texte du fichier pour l'éditer directement dans le launcher,
  // sans passer par le sélecteur de fichier natif — pratique pour un petit .cfg où
  // rouvrir un éditeur externe juste pour changer une valeur est plus lourd que le jeu.
  async function handleEditConfigFile(index: number) {
    setEditingConfigFileIndex(index);
    setConfigFileContent("");
    setConfigFileContentState({ kind: "loading" });
    try {
      const content = await invoke<string>("fetch_config_file_content", {
        url: configFiles[index].downloadUrl,
      });
      setConfigFileContent(content);
      setConfigFileContentState({ kind: "idle" });
    } catch (err) {
      setConfigFileContentState({ kind: "error", message: String(err) });
    }
  }

  function closeConfigFileEditor() {
    setEditingConfigFileIndex(null);
    setConfigFileContent("");
    setConfigFileContentState({ kind: "idle" });
  }

  // Réenregistre le contenu édité comme un nouvel upload (même endpoint que le
  // sélecteur de fichier, voir `upload_config_file_text` côté Rust) — le nom de
  // destination dans BepInEx/config/ n'est jamais touché, seuls downloadUrl/sha256 sont
  // mis à jour pour cette entrée.
  async function handleSaveConfigFileContent() {
    if (editingConfigFileIndex === null) return;
    const index = editingConfigFileIndex;
    setConfigFileContentState({ kind: "saving" });
    try {
      const upload = await invoke<ConfigFileUpload>("save_config_file_text", {
        filename: configFiles[index].filename,
        content: configFileContent,
      });
      setConfigFiles((prev) =>
        prev.map((f, i) =>
          i === index ? { ...f, downloadUrl: upload.url, sha256: upload.sha256 } : f,
        ),
      );
      setSessionConfigFileUploads((prev) => [...prev, upload.url]);
      closeConfigFileEditor();
    } catch (err) {
      setConfigFileContentState({ kind: "error", message: String(err) });
    }
  }

  function updateDraft(index: number, field: keyof ModWrite, value: string) {
    setDraft((prev) => prev.map((mod, i) => (i === index ? { ...mod, [field]: value } : mod)));
  }

  function toggleAdminOnly(index: number) {
    setDraft((prev) =>
      prev.map((mod, i) => (i === index ? { ...mod, adminOnly: !mod.adminOnly } : mod)),
    );
  }

  function toggleEnabled(index: number) {
    setDraft((prev) =>
      prev.map((mod, i) => (i === index ? { ...mod, enabled: !mod.enabled } : mod)),
    );
  }

  async function removeDraftEntry(index: number) {
    const name = draft[index]?.name.trim();
    const confirmed = await confirm(
      name
        ? `Supprimer le mod "${name}" du modpack ?`
        : "Supprimer ce mod du modpack ?",
    );
    if (!confirmed) return;
    setDraft((prev) => prev.filter((_, i) => i !== index));
  }

  // Sélection multiple : chaque zip choisi crée directement sa fiche de mod, préremplie
  // depuis son manifest.json Thunderstore quand il y en a un (nom/version/
  // description) — pas d'étape "carte vide" à remplir à la main avant. Pour l'envoi en
  // masse (plusieurs zips d'un coup), les archives sont uploadées une par une côté Rust
  // (voir pick_zips_and_upload) ; un échec sur l'une n'annule pas les autres, il est
  // juste remonté dans `errors`. Si le nom détecté correspond à un mod déjà présent
  // dans le brouillon (ou à un autre zip du même lot déjà traité), on propose de
  // remplacer ses fichiers plutôt que de créer une deuxième carte avec le même nom
  // (voir duplicateNameKeys, qui bloquerait de toute façon l'enregistrement — mais
  // autant ne jamais laisser apparaître le doublon).
  async function handleAddMods() {
    setAddingMod(true);
    setEditLoadError(null);
    try {
      const result = await invoke<BulkUpload>("pick_zips_and_upload");
      if (result.uploads.length > 0) {
        setSessionUploads((prev) => [...prev, ...result.uploads]);
        // Boucle séquentielle (pas de setDraft((prev) => ...)) : le confirm() natif est
        // asynchrone et un updater React doit rester synchrone — on part donc de l'état
        // `draft` courant, ce qui est sûr ici puisque le sélecteur de fichiers/les
        // popups de confirmation bloquent déjà toute autre interaction avec le formulaire
        // pendant cette boucle.
        let next = draft;
        for (const upload of result.uploads) {
          const uploadKey = upload.name ? normalizePackageName(upload.name) : "";
          const existingIndex = uploadKey
            ? next.findIndex((mod) => normalizePackageName(mod.name) === uploadKey)
            : -1;

          if (existingIndex === -1) {
            next = [...next, applyUpload(EMPTY_MOD, upload)];
          } else {
            const existingName = next[existingIndex].name;
            const replace = await confirm(
              `Un mod nommé "${existingName}" existe déjà dans la liste — remplacer ses fichiers avec ce zip plutôt que d'en créer un deuxième ?`,
            );
            if (replace) {
              next = next.map((mod, i) => (i === existingIndex ? applyUpload(mod, upload) : mod));
            }
            // Si annulé : cette carte n'est pas touchée. Le fichier reste tracké dans
            // sessionUploads et pourra être nettoyé via "Annuler" si besoin.
          }
        }
        setDraft(next);
      }
      if (result.errors.length > 0) {
        setEditLoadError(result.errors.join("\n"));
      }
    } catch (err) {
      setEditLoadError(String(err));
    } finally {
      setAddingMod(false);
    }
  }

  async function handlePickModFiles(index: number) {
    setPickingIndex(index);
    setEditLoadError(null);
    try {
      const upload = await invoke<FileUpload | null>("pick_zip_and_upload");
      if (upload) {
        setDraft((prev) => prev.map((mod, i) => (i === index ? applyUpload(mod, upload) : mod)));
        setSessionUploads((prev) => [...prev, upload]);
      }
    } catch (err) {
      setEditLoadError(String(err));
    } finally {
      setPickingIndex(null);
    }
  }

  // "Annuler" jette le brouillon, mais les fichiers déjà uploadés à l'API pendant cette
  // session (voir sessionUploads) restent orphelins côté serveur si on ne les nettoie
  // pas — proposé à la suppression plutôt que fait silencieusement.
  async function handleCancelEdit() {
    // Un onglet peut avoir été enregistré (voir handleSaveMods/handleSaveConfig, qui ne
    // ferment plus l'éditeur) pendant que l'autre a encore des changements en attente —
    // prévenir avant de les perdre, distinct de l'avertissement sur les fichiers
    // importés ci-dessous qui ne couvre que les uploads, pas les champs texte édités.
    if (modsDirty || configDirty) {
      const confirmed = await confirm(
        "Des changements non enregistrés seront perdus (sur l'onglet Mods et/ou Fichiers de config) — continuer ?",
      );
      if (!confirmed) return;
    }
    const importedCount = sessionUploads.length + sessionConfigFileUploads.length;
    if (importedCount > 0) {
      const confirmed = await confirm(
        `${importedCount} fichier(s) importé(s) ne seront pas utilisés si tu annules — les supprimer du serveur ?`,
      );
      if (confirmed) {
        const urls = [
          ...sessionUploads.flatMap((u) => [u.url, u.iconUrl].filter((x): x is string => !!x)),
          ...sessionConfigFileUploads,
        ];
        try {
          await invoke("delete_uploaded_files", { urls });
        } catch {
          // best-effort : on annule quand même l'édition même si le nettoyage échoue.
        }
      }
    }
    setSessionUploads([]);
    setSessionConfigFileUploads([]);
    closeConfigFileEditor();
    setEditing(false);
  }

  // true seulement si le brouillon diffère de l'instantané pris à l'ouverture de
  // l'éditeur (ou depuis le dernier enregistrement de cet onglet) — c'est ce qui décide
  // si le bouton "Enregistrer" de l'onglet Mods s'affiche.
  const modsDirty = useMemo(
    () => JSON.stringify(draft) !== JSON.stringify(initialDraft),
    [draft, initialDraft],
  );
  const configDirty = useMemo(
    () => JSON.stringify(configFiles) !== JSON.stringify(initialConfigFiles),
    [configFiles, initialConfigFiles],
  );

  // Un gros zip (BepInEx, mod "AIO"...) peut prendre du temps à uploader -- le seul
  // texte "Import..." sur le bouton cliqué passe facilement inaperçu. Vrai tant que
  // n'importe quel import est en cours, peu importe lequel, pour bloquer tout le reste
  // de l'éditeur pendant ce temps (voir l'overlay dans le JSX ci-dessous).
  const isImporting =
    pickingIndex !== null ||
    addingMod ||
    bepinexState.kind === "uploading" ||
    pickingConfigFile ||
    pickingConfigFileIndex !== null;

  // Reflète les vrais changements non enregistrés, pas juste "l'éditeur est ouvert" —
  // "Enregistrer" ne ferme pas l'éditeur (voir handleSaveMods/handleSaveConfig, pour
  // pouvoir continuer sur l'autre onglet), donc `editing` seul resterait `true` après un
  // enregistrement réussi et redemanderait confirmation avant de quitter pour rien.
  useEffect(() => {
    onDirtyChange(editing && (modsDirty || configDirty));
  }, [editing, modsDirty, configDirty, onDirtyChange]);

  // N'écrit que les mods — ne touche jamais aux fichiers de config, même si l'autre
  // onglet a des changements non enregistrés en attente. Reste dans l'éditeur après
  // succès (contrairement à l'ancien handleSave unique) pour laisser l'admin continuer
  // sur l'autre onglet.
  async function handleSaveMods() {
    if (duplicateNameKeys.size > 0) {
      setModsSaveState({
        kind: "error",
        message:
          "Deux mods portent le même nom — supprime ou renomme l'un des deux avant d'enregistrer.",
      });
      return;
    }
    const modsWithMissingDeps = draft
      .filter((_, i) => missingDepsByMod[i]?.length > 0)
      .map((mod) => mod.name.trim() || "(sans nom)");
    if (modsWithMissingDeps.length > 0) {
      setModsSaveState({
        kind: "error",
        message: `Dépendance(s) manquante(s) pour : ${modsWithMissingDeps.join(", ")} — ajoute le(s) mod(s)/BepInEx correspondant(s) au modpack ou supprime ce(s) mod(s) avant d'enregistrer.`,
      });
      return;
    }
    setModsSaveState({ kind: "saving" });
    try {
      await invoke("save_mods", { slug, mods: draft });
      // Les zips/icônes uploadés pendant cette session sont désormais référencés par un
      // mod enregistré — ne plus les proposer à la suppression si "Annuler" est cliqué
      // ensuite (voir handleCancelEdit).
      setSessionUploads([]);
      setInitialDraft(JSON.parse(JSON.stringify(draft)));
      setModsSaveState({ kind: "idle" });
      setActiveCategory(ALL_CATEGORIES);
      loadMods();
      onModpackUpdated();
    } catch (err) {
      setModsSaveState({ kind: "error", message: String(err) });
    }
  }

  // N'écrit que les fichiers de config — symétrique de handleSaveMods ci-dessus.
  async function handleSaveConfig() {
    if (duplicateConfigFilenames.size > 0) {
      setConfigSaveState({
        kind: "error",
        message:
          "Deux fichiers de config portent le même nom — renomme l'un des deux avant d'enregistrer.",
      });
      return;
    }
    setConfigSaveState({ kind: "saving" });
    try {
      await invoke("save_config_files", { slug, files: configFiles });
      setSessionConfigFileUploads([]);
      setInitialConfigFiles(JSON.parse(JSON.stringify(configFiles)));
      setConfigSaveState({ kind: "idle" });
      onModpackUpdated();
    } catch (err) {
      setConfigSaveState({ kind: "error", message: String(err) });
    }
  }

  return (
    <div className="mods-page">
      <header className="mods-page__header">
        <div className="mods-page__title-row">
          <h1>Mods du serveur</h1>
          {isAdmin && slug !== PRODUCTION_MODPACK_SLUG && (
            <span
              className="mods-list__admin-badge"
              title="Ce n'est pas le profil production — voir la page Profils pour changer"
            >
              Profil : {slug}
            </span>
          )}
          {isAdmin && !editing && (
            <button type="button" className="btn btn--ghost" onClick={startEditing}>
              Éditer
            </button>
          )}
        </div>
        <p>
          {state.kind === "loading"
            ? "Chargement..."
            : `${mods.length + (bepinexConfigured ? 1 : 0)} mods installés et maintenus à jour automatiquement par le launcher.`}
        </p>
      </header>

      {state.kind === "error" && <p className="mods-page__error">{state.message}</p>}

      {editing ? (
        <div className="mods-editor">
          {isImporting && (
            <div className="mods-editor__overlay">
              <div className="mods-editor__spinner" />
              <span className="mods-editor__overlay-text">Import en cours...</span>
            </div>
          )}

          {editLoadError && <p className="mods-page__error">{editLoadError}</p>}

          <div className="mods-page__tabs">
            <button
              type="button"
              className={`mods-page__tab ${editorView === "mods" ? "is-active" : ""}`}
              onClick={() => {
                setEditorView("mods");
                setEditorSearch("");
              }}
            >
              Mods{draft.length > 0 ? ` (${draft.length})` : ""}
            </button>
            <button
              type="button"
              className={`mods-page__tab ${editorView === "config" ? "is-active" : ""}`}
              onClick={() => {
                setEditorView("config");
                setEditorSearch("");
              }}
            >
              Fichiers de config{configFiles.length > 0 ? ` (${configFiles.length})` : ""}
            </button>
          </div>

          {editorView === "mods" && (
            <>
              <div className="mods-editor__section-header">
                <input
                  type="search"
                  className="mods-editor__search"
                  placeholder="Rechercher un mod..."
                  value={editorSearch}
                  onChange={(e) => setEditorSearch(e.target.value)}
                />
                <button
                  type="button"
                  className="btn btn--ghost"
                  onClick={handleAddMods}
                  disabled={addingMod}
                >
                  {addingMod ? "Import..." : "+ Ajouter des mods"}
                </button>
              </div>

              {(!editorSearch.trim() ||
                "bepinex".includes(editorSearch.trim().toLowerCase())) && (
                <div
                  className={`mods-editor__card ${!bepinexConfigured ? "mods-editor__card--warning" : ""}`}
                >
                  <div className="mods-editor__row">
                    {bepinexIconUrl && (
                      <img
                        className="mods-editor__icon"
                        src={`${apiBaseUrl}${bepinexIconUrl}`}
                        alt=""
                      />
                    )}
                    <span className="mods-editor__bepinex-name">BepInEx</span>
                    {bepinexVersion && (
                      <span className="mods-list__version">v{bepinexVersion}</span>
                    )}
                  </div>
                  {!bepinexConfigured ? (
                    <p className="mods-page__warning">
                      Non configuré — "Jouer" refusera de lancer le jeu tant que ce n'est pas
                      fait.
                    </p>
                  ) : (
                    <p className="mods-list__description">{bepinexDescription}</p>
                  )}
                  <div className="mods-editor__row">
                    <button
                      type="button"
                      className="btn btn--ghost"
                      onClick={handlePickBepinex}
                      disabled={bepinexState.kind === "uploading"}
                    >
                      {bepinexState.kind === "uploading"
                        ? "Import..."
                        : bepinexConfigured
                          ? "Mettre à jour"
                          : "Choisir le zip"}
                    </button>
                    {bepinexState.kind === "error" && (
                      <span className="mods-page__error">{bepinexState.message}</span>
                    )}
                  </div>
                </div>
              )}

              {draft.map((mod, i) => {
                if (
                  editorSearch.trim() &&
                  !mod.name.toLowerCase().includes(editorSearch.trim().toLowerCase())
                ) {
                  return null;
                }
                const missingDeps = missingDepsByMod[i] ?? [];
                const isDuplicate = duplicateNameKeys.has(normalizePackageName(mod.name));
                return (
                  <div
                    className={`mods-editor__card ${missingDeps.length > 0 || isDuplicate ? "mods-editor__card--warning" : ""} ${!mod.enabled ? "mods-editor__card--disabled" : ""}`}
                    key={i}
                  >
                    <div className="mods-editor__row">
                      {mod.iconUrl && (
                        <img
                          className="mods-editor__icon"
                          src={`${apiBaseUrl}${mod.iconUrl}`}
                          alt=""
                        />
                      )}
                      <input
                        className="mods-editor__input"
                        placeholder="Nom"
                        value={mod.name}
                        onChange={(e) => updateDraft(i, "name", e.target.value)}
                      />
                      <input
                        className="mods-editor__input mods-editor__input--small"
                        placeholder="Version"
                        value={mod.version}
                        onChange={(e) => updateDraft(i, "version", e.target.value)}
                      />
                      <input
                        className="mods-editor__input mods-editor__input--small"
                        placeholder="Catégorie"
                        list="mod-categories"
                        value={mod.category}
                        onChange={(e) => updateDraft(i, "category", e.target.value)}
                      />
                    </div>
                    <textarea
                      className="mods-editor__textarea"
                      placeholder="Description"
                      rows={2}
                      value={mod.description}
                      onChange={(e) => updateDraft(i, "description", e.target.value)}
                    />
                    <label className="mods-editor__checkbox">
                      <input
                        type="checkbox"
                        checked={mod.adminOnly}
                        onChange={() => toggleAdminOnly(i)}
                      />
                      Réservé aux admins (invisible du modpack Joueur)
                    </label>
                    <label className="mods-editor__checkbox">
                      <input
                        type="checkbox"
                        checked={mod.enabled}
                        onChange={() => toggleEnabled(i)}
                      />
                      Activé (décoché = désactivé pour tout le monde, sans supprimer la fiche)
                    </label>
                    <div className="mods-editor__row">
                      <button
                        type="button"
                        className="btn btn--ghost"
                        onClick={() => handlePickModFiles(i)}
                        disabled={pickingIndex === i}
                      >
                        {pickingIndex === i
                          ? "Import..."
                          : mod.sha256
                            ? "Mettre à jour"
                            : "Choisir le zip du mod"}
                      </button>
                      <span className="mods-page__hint">
                        {mod.sha256 ? "✓ Fichiers importés" : "Aucun fichier importé"}
                      </span>
                    </div>
                    {(mod.createdAt || mod.updatedAt) && (
                      <p className="mods-page__hint">
                        {mod.createdAt && `Ajouté le ${formatDate(mod.createdAt)}`}
                        {mod.createdAt && mod.updatedAt && " — "}
                        {mod.updatedAt && `Dernier zip mis à jour le ${formatDate(mod.updatedAt)}`}
                      </p>
                    )}
                    {missingDeps.length > 0 && (
                      <p className="mods-page__warning">
                        Dépendance{missingDeps.length > 1 ? "s" : ""} manquante
                        {missingDeps.length > 1 ? "s" : ""} : {missingDeps.join(", ")}
                      </p>
                    )}
                    <button
                      type="button"
                      className="btn btn--ghost mods-editor__remove"
                      onClick={() => removeDraftEntry(i)}
                    >
                      Supprimer
                    </button>
                  </div>
                );
              })}

              <datalist id="mod-categories">
                {categoryOptions.map((category) => (
                  <option key={category} value={category} />
                ))}
              </datalist>
            </>
          )}

          {editorView === "config" && (
            <>
              <p className="mods-list__description">
                Copiés tels quels dans BepInEx/config/ à la prochaine synchronisation des
                joueurs (ex: le .cfg d'un mod pré-rempli avec l'adresse du serveur).
              </p>

              <div className="mods-editor__section-header">
                <input
                  type="search"
                  className="mods-editor__search"
                  placeholder="Rechercher un fichier..."
                  value={editorSearch}
                  onChange={(e) => setEditorSearch(e.target.value)}
                />
                <button
                  type="button"
                  className="btn btn--ghost"
                  onClick={handleAddConfigFile}
                  disabled={pickingConfigFile}
                >
                  {pickingConfigFile ? "Import..." : "+ Ajouter un fichier de config"}
                </button>
              </div>

              {duplicateConfigFilenames.size > 0 && (
                <p className="mods-page__warning">
                  Deux fichiers de config portent le même nom — renomme l'un des deux.
                </p>
              )}

              {configFiles.map((file, i) => {
                if (
                  editorSearch.trim() &&
                  !file.filename.toLowerCase().includes(editorSearch.trim().toLowerCase())
                ) {
                  return null;
                }
                return (
                <div className="mods-editor__card" key={i}>
                  <div className="mods-editor__row">
                    <input
                      className="mods-editor__input"
                      placeholder="Nom du fichier (ex: FastLink.cfg)"
                      value={file.filename}
                      onChange={(e) => updateConfigFileName(i, e.target.value)}
                    />
                    <button
                      type="button"
                      className="btn btn--ghost"
                      onClick={() => handleEditConfigFile(i)}
                      disabled={editingConfigFileIndex === i}
                    >
                      Éditer
                    </button>
                    <button
                      type="button"
                      className="btn btn--ghost"
                      onClick={() => handleReplaceConfigFile(i)}
                      disabled={pickingConfigFileIndex === i}
                    >
                      {pickingConfigFileIndex === i ? "Import..." : "Mettre à jour"}
                    </button>
                    <button
                      type="button"
                      className="btn btn--ghost"
                      onClick={() => removeConfigFile(i)}
                    >
                      Supprimer
                    </button>
                  </div>
                  {editingConfigFileIndex === i && (
                    <div className="mods-editor__config-content">
                      {configFileContentState.kind === "loading" ? (
                        <p className="mods-page__hint">Chargement...</p>
                      ) : (
                        <textarea
                          className="mods-editor__textarea mods-editor__textarea--code"
                          rows={12}
                          value={configFileContent}
                          onChange={(e) => setConfigFileContent(e.target.value)}
                          disabled={configFileContentState.kind === "saving"}
                        />
                      )}
                      {configFileContentState.kind === "error" && (
                        <p className="mods-page__error">{configFileContentState.message}</p>
                      )}
                      <div className="mods-editor__row">
                        <button
                          type="button"
                          className="btn btn--ghost"
                          onClick={closeConfigFileEditor}
                          disabled={configFileContentState.kind === "saving"}
                        >
                          Annuler
                        </button>
                        <button
                          type="button"
                          className="btn btn--accent"
                          onClick={handleSaveConfigFileContent}
                          disabled={
                            configFileContentState.kind === "saving" ||
                            configFileContentState.kind === "loading"
                          }
                        >
                          {configFileContentState.kind === "saving"
                            ? "Enregistrement..."
                            : "Enregistrer le contenu"}
                        </button>
                      </div>
                    </div>
                  )}
                </div>
                );
              })}
            </>
          )}

          {editorView === "mods" && modsSaveState.kind === "error" && (
            <p className="mods-page__error">{modsSaveState.message}</p>
          )}
          {editorView === "config" && configSaveState.kind === "error" && (
            <p className="mods-page__error">{configSaveState.message}</p>
          )}

          <div className="mods-editor__actions">
            <button
              type="button"
              className="btn btn--ghost"
              onClick={handleCancelEdit}
              disabled={modsSaveState.kind === "saving" || configSaveState.kind === "saving"}
            >
              Fermer
            </button>
            {editorView === "mods" && modsDirty && (
              <button
                type="button"
                className="btn btn--accent"
                onClick={handleSaveMods}
                disabled={modsSaveState.kind === "saving"}
              >
                {modsSaveState.kind === "saving" ? "Enregistrement..." : "Enregistrer les mods"}
              </button>
            )}
            {editorView === "config" && configDirty && (
              <button
                type="button"
                className="btn btn--accent"
                onClick={handleSaveConfig}
                disabled={configSaveState.kind === "saving"}
              >
                {configSaveState.kind === "saving"
                  ? "Enregistrement..."
                  : "Enregistrer les fichiers de config"}
              </button>
            )}
          </div>
        </div>
      ) : (
        <>
          <div className="mods-page__tabs">
            {categories.map((category) => (
              <button
                key={category}
                type="button"
                className={`mods-page__tab ${activeCategory === category ? "is-active" : ""}`}
                onClick={() => setActiveCategory(category)}
              >
                {category}
              </button>
            ))}
          </div>

          <ul className="mods-list">
            {activeCategory === ALL_CATEGORIES && (
              <li
                className={`mods-list__item ${!bepinexConfigured ? "mods-list__item--warning" : ""}`}
              >
                <div className="mods-list__main">
                  {bepinexIconUrl && (
                    <img className="mods-list__icon" src={`${apiBaseUrl}${bepinexIconUrl}`} alt="" />
                  )}
                  <span className="mods-list__name">BepInEx</span>
                  {bepinexVersion && <span className="mods-list__version">v{bepinexVersion}</span>}
                </div>
                {!bepinexConfigured ? (
                  <p className="mods-page__warning">
                    {isAdmin
                      ? "Non configuré — \"Jouer\" refusera de lancer le jeu tant que ce n'est pas fait."
                      : "Non configuré pour l'instant — contacte un administrateur."}
                  </p>
                ) : (
                  <p className="mods-list__description">{bepinexDescription}</p>
                )}
                <span className="mods-list__tag is-serveur">Système</span>
              </li>
            )}
            {visibleMods.map((mod) => (
              <li
                key={mod.name}
                className={`mods-list__item ${mod.enabled === false ? "mods-list__item--disabled" : ""}`}
              >
                <div className="mods-list__main">
                  {mod.iconUrl && (
                    <img className="mods-list__icon" src={`${apiBaseUrl}${mod.iconUrl}`} alt="" />
                  )}
                  <span className="mods-list__name">{mod.name}</span>
                  <span className="mods-list__version">v{mod.version}</span>
                  {mod.adminOnly && <span className="mods-list__admin-badge">Admin</span>}
                  {mod.enabled === false && (
                    <span className="mods-list__admin-badge mods-list__admin-badge--disabled">
                      Désactivé
                    </span>
                  )}
                </div>
                <p className="mods-list__description">{mod.description}</p>
                <span className={`mods-list__tag ${CATEGORY_CLASS[mod.category] ?? "is-serveur"}`}>
                  {mod.category}
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
