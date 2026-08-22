import { useCallback, useEffect, useRef, useState } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { openUrl } from "@tauri-apps/plugin-opener";
import { confirm } from "@tauri-apps/plugin-dialog";
import { check as checkForAppUpdate, type Update } from "@tauri-apps/plugin-updater";
import { relaunch } from "@tauri-apps/plugin-process";
import { CursorWisp } from "./components/CursorWisp";
import { ParticleField } from "./components/ParticleField";
import { Sidebar, type Page } from "./components/Sidebar";
import { AcceptRulesGate } from "./components/onboarding/AcceptRulesGate";
import { SteamIdGate } from "./components/onboarding/SteamIdGate";
import { PRODUCTION_MODPACK_SLUG } from "./data/mock";
import { hexToRgba } from "./utils/color";
import { HomePage } from "./pages/HomePage";
import { AnnouncementsPage } from "./pages/AnnouncementsPage";
import { ModsPage } from "./pages/ModsPage";
import { RulesPage } from "./pages/RulesPage";
import { FaqPage } from "./pages/FaqPage";
import { ProfilesPage } from "./pages/ProfilesPage";
import { SettingsPage } from "./pages/SettingsPage";
import type { UserInfo } from "./types";
import "./styles/tokens.css";
import "./styles/shell.css";

type RefreshOutcome =
  | { kind: "ok"; user: UserInfo }
  | { kind: "loggedOut"; message: string }
  | { kind: "error"; message: string };

// Revalidation du rôle Discord en tâche de fond, tant que le launcher reste ouvert.
const SESSION_REFRESH_INTERVAL_MS = 5 * 60 * 1000;

// Ping de l'API pour savoir si elle est joignable — plus réactif que le refresh de
// session (l'admin peut redémarrer l'API pendant que le launcher reste ouvert).
const API_HEALTH_CHECK_INTERVAL_MS = 15 * 1000;

// Durée d'affichage du message "✓ Modpack à jour" après une synchronisation (voir
// handleUpdate) avant de faire disparaître le bouton "Mettre à jour" — sans cette pause,
// une mise à jour rapide (peu de mods à resynchroniser) fait passer instantanément de
// deux boutons désactivés à un seul, sans qu'aucun changement ne soit lisible à l'oeil.
const UPDATE_SUCCESS_DISPLAY_MS = 900;

interface Settings {
  buyMeACoffeeUrl: string;
  heroEyebrow: string;
  heroTagline: string;
}

const DEFAULT_SETTINGS: Settings = {
  buyMeACoffeeUrl: "https://buymeacoffee.com/fedoheim",
  heroEyebrow: "Serveur communautaire",
  heroTagline: "Le feu brûle, les portes sont ouvertes.",
};

type PlayState =
  | { kind: "idle"; detail: string }
  | { kind: "busy"; detail: string }
  // Autorisation manquante (rôle Discord/ban) — pas un vrai souci technique, donc pas
  // le ton rouge/alarmant de "error" (voir AUTH_WARNING_PREFIX et le CSS associé).
  | { kind: "warning"; detail: string }
  | { kind: "error"; detail: string };

// Préfixe utilisé côté Rust (voir auth.rs) pour marquer un message d'auth comme
// "warning" plutôt que "error" — jamais affiché tel quel, retiré avant affichage.
const AUTH_WARNING_PREFIX = "AUTH_WARNING:";

// Émis par les commandes Rust `play`/`sync_modpack`/`repair_modpack` pendant la
// synchronisation (BepInEx puis chaque mod), ou une seule fois pour signaler le repli
// hors ligne — granularité par étape (BepInEx = 1 étape, chaque mod = 1 étape), pas par
// octet téléchargé. `total` compte déjà BepInEx dans le total côté Rust (voir
// `ensure_bepinex`/`sync_mods`), donc current/total forme une progression continue sur
// toute l'opération plutôt que de repartir de zéro à chaque phase.
interface SyncProgress {
  phase: "bepinex" | "mod" | "offline";
  label: string;
  current: number;
  total: number;
}

function App() {
  const [page, setPage] = useState<Page>("home");
  const [user, setUser] = useState<UserInfo | null>(null);
  const [loggingIn, setLoggingIn] = useState(false);
  const [settings, setSettings] = useState<Settings>(DEFAULT_SETTINGS);
  const [playState, setPlayState] = useState<PlayState>({
    kind: "idle",
    detail: "Connecte-toi pour rejoindre notre serveur.",
  });
  // Progression de la synchronisation en cours (voir SyncProgress) — `null` en dehors
  // d'une synchro, sert à afficher la barre/pourcentage dans la playbar.
  const [progress, setProgress] = useState<{ current: number; total: number } | null>(null);
  // Optimiste (true) tant que le premier ping n'est pas revenu, pour ne pas afficher le
  // bandeau en flash à chaque démarrage.
  const [apiReachable, setApiReachable] = useState(true);
  const [secondsUntilCheck, setSecondsUntilCheck] = useState(API_HEALTH_CHECK_INTERVAL_MS / 1000);
  // Pessimiste (false) par défaut : évite d'afficher "Jouer" puis de basculer sur
  // "Télécharger" une fois la vraie réponse arrivée (effet de clignotement).
  const [hasLocalManifest, setHasLocalManifest] = useState(false);
  // Mise à jour du launcher lui-même (pas du modpack, voir `updateAvailable` plus bas) —
  // détectée une fois au démarrage via le plugin updater Tauri, contre le manifest signé
  // (`latest.json`) publié sur la dernière release GitHub. `null` tant qu'aucune mise à
  // jour n'est disponible ou que la vérification a échoué (offline, pas encore de
  // release publiée...) — best-effort, ne bloque jamais le lancement du jeu.
  const [appUpdate, setAppUpdate] = useState<Update | null>(null);
  const [installingAppUpdate, setInstallingAppUpdate] = useState(false);
  // Mods/BepInEx différents côté API par rapport à la dernière install locale — fait
  // apparaître "Mettre à jour" à côté de "Jouer" plutôt qu'un seul bouton qui
  // resynchroniserait en silence à chaque clic (voir `check_update_available`).
  const [updateAvailable, setUpdateAvailable] = useState(false);
  // Modpack "Joueur" (mods communs) ou "Admin" (mods communs + mods réservés aux
  // admins, voir CLAUDE.md) — un joueur normal reste toujours en "player" (le choix ne
  // lui est jamais proposé, voir requestPrimaryAction). Ne change que quand un admin
  // confirme un choix dans la popup (voir pendingAction ci-dessous) : "Jouer" une
  // deuxième fois de suite ne redemande rien tant que le choix n'a pas explicitement
  // été redéclenché.
  const [launchMode, setLaunchMode] = useState<"player" | "admin">("player");
  // Profil de modpack ciblé par "Jouer"/"Mettre à jour"/"Réparer" et par l'éditeur de
  // mods (voir ProfilesPage) — un admin peut le changer pour tester un modpack sur un
  // serveur séparé avant de le répliquer en production. Toujours le profil production
  // pour un non-admin (voir `effectiveModpackSlug` ci-dessous, et remis à zéro si le
  // rôle admin est perdu en cours de session). Persisté sur disque (voir selectProfile
  // et l'effet de restauration ci-dessous) pour survivre à un redémarrage du launcher —
  // seule la valeur initiale par défaut est la production, avant que la restauration
  // n'ait eu le temps de s'appliquer.
  const [activeProfileSlug, setActiveProfileSlug] = useState(PRODUCTION_MODPACK_SLUG);
  // Couleur choisie par un admin pour ce profil (voir ProfilesPage) — purement
  // cosmétique (teinte le badge "Profil" ci-dessous), jamais utilisée pour cibler quoi
  // que ce soit. `null` pour le profil production ou tant qu'aucune couleur n'a été
  // choisie.
  const [activeProfileColor, setActiveProfileColor] = useState<string | null>(null);
  const isAdmin = user?.isAdmin ?? false;
  const effectiveModpackSlug = isAdmin ? activeProfileSlug : PRODUCTION_MODPACK_SLUG;

  function selectProfile(profile: { slug: string; color: string | null }) {
    setActiveProfileSlug(profile.slug);
    setActiveProfileColor(profile.slug === PRODUCTION_MODPACK_SLUG ? null : profile.color);
    invoke("save_active_profile", { slug: profile.slug, color: profile.color }).catch(() => {});
  }

  useEffect(() => {
    if (!isAdmin) {
      setActiveProfileSlug(PRODUCTION_MODPACK_SLUG);
      setActiveProfileColor(null);
    }
  }, [isAdmin]);

  // Restaure le profil actif persisté (voir active_profile.rs) une fois qu'on sait que
  // l'utilisateur est admin — jamais pour un joueur normal, qui ne passe de toute façon
  // jamais par `selectProfile`. Revalidé contre `list_modpacks` avant d'être appliqué :
  // le profil persisté a pu être supprimé depuis la dernière session, dans quel cas on
  // reste sur la production (repli déjà en place par défaut) plutôt que de cibler un
  // slug qui n'existe plus. `restoredProfileRef` évite de réappliquer si le rôle admin
  // est perdu puis retrouvé en cours de session (l'admin a pu changer de profil entre
  // les deux, pas la peine d'écraser son choix avec l'ancien état persisté).
  const restoredProfileRef = useRef(false);
  useEffect(() => {
    if (!isAdmin || restoredProfileRef.current) return;
    restoredProfileRef.current = true;
    (async () => {
      try {
        const persisted = await invoke<{ slug: string; color: string | null } | null>(
          "load_active_profile",
        );
        if (!persisted || persisted.slug === PRODUCTION_MODPACK_SLUG) return;
        const profiles = await invoke<{ slug: string; color: string | null }[]>("list_modpacks");
        const match = profiles.find((p) => p.slug === persisted.slug);
        if (match) {
          setActiveProfileSlug(match.slug);
          setActiveProfileColor(match.color);
        }
      } catch {
        // best-effort : reste sur le profil production par défaut si la restauration échoue
        // (API injoignable au démarrage, par exemple).
      }
    })();
  }, [isAdmin]);
  // Action en attente du choix de mode (voir requestPrimaryAction) — seuls "play",
  // "update" et "repair" resynchronisent le modpack et ont donc besoin de savoir dans
  // quel mode ; "launch_only" lance l'installation existante telle quelle, son
  // comportement ne dépend pas du mode choisi.
  const [pendingAction, setPendingAction] = useState<"play" | "update" | "repair" | null>(null);
  // Édition de mods en cours (voir ModsPage.onDirtyChange) — sert à confirmer avant de
  // changer de page ou de fermer le launcher plutôt que de perdre le brouillon en silence.
  const [modsDirty, setModsDirty] = useState(false);
  const modsDirtyRef = useRef(modsDirty);
  useEffect(() => {
    modsDirtyRef.current = modsDirty;
  }, [modsDirty]);

  // Petit menu d'options avancées à côté de "Jouer" (voir bouton settings dans la
  // playbar) — pour l'instant seulement "Réparer", d'autres choix viendront ensuite.
  const [settingsMenuOpen, setSettingsMenuOpen] = useState(false);
  const settingsMenuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!settingsMenuOpen) return;
    function handleClickOutside(event: MouseEvent) {
      if (settingsMenuRef.current && !settingsMenuRef.current.contains(event.target as Node)) {
        setSettingsMenuOpen(false);
      }
    }
    function handleEscape(event: KeyboardEvent) {
      if (event.key === "Escape") setSettingsMenuOpen(false);
    }
    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleEscape);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleEscape);
    };
  }, [settingsMenuOpen]);

  // Confirmation à la fermeture de la fenêtre si des mods sont en cours d'édition — un
  // seul listener enregistré une fois, qui lit toujours la dernière valeur via la ref
  // (évite de le ré-enregistrer à chaque changement de modsDirty).
  useEffect(() => {
    const unlistenPromise = getCurrentWindow().onCloseRequested(async (event) => {
      if (modsDirtyRef.current) {
        try {
          const confirmed = await confirm(
            "Tu as des modifications de mods non enregistrées — fermer quand même ?",
          );
          if (!confirmed) {
            event.preventDefault();
          }
        } catch {
          // Si la boîte de confirmation échoue, ne jamais bloquer la fermeture
          // indéfiniment — mieux vaut fermer sans avertissement qu'une fenêtre
          // qu'on ne peut plus jamais fermer.
        }
      }
    });
    return () => {
      unlistenPromise.then((unlisten) => unlisten());
    };
  }, []);

  useEffect(() => {
    invoke<boolean>("has_local_manifest")
      .then(setHasLocalManifest)
      .catch(() => {});
  }, []);

  useEffect(() => {
    checkForAppUpdate()
      .then((update) => {
        if (update?.available) setAppUpdate(update);
      })
      .catch(() => {});
  }, []);

  const handleInstallAppUpdate = useCallback(async () => {
    if (!appUpdate) return;
    setInstallingAppUpdate(true);
    try {
      await appUpdate.downloadAndInstall();
      await relaunch();
    } catch {
      // Best-effort : si le téléchargement/l'install échoue (réseau coupé en cours de
      // route...), on retombe simplement sur le bandeau tel quel, réessayable au clic.
      setInstallingAppUpdate(false);
    }
  }, [appUpdate]);

  // Un seul minuteur (tick à la seconde) qui sert à la fois de compte à rebours affiché
  // dans le bandeau et de déclencheur du ping réel une fois à zéro — évite deux
  // intervalles séparés qui dériveraient l'un par rapport à l'autre.
  useEffect(() => {
    let cancelled = false;
    const intervalSeconds = API_HEALTH_CHECK_INTERVAL_MS / 1000;

    async function check() {
      const reachable = await invoke<boolean>("check_api_reachable");
      if (!cancelled) setApiReachable(reachable);
    }

    check();

    const tick = setInterval(() => {
      setSecondsUntilCheck((prev) => {
        if (prev <= 1) {
          check();
          return intervalSeconds;
        }
        return prev - 1;
      });
    }, 1000);

    return () => {
      cancelled = true;
      clearInterval(tick);
    };
  }, []);

  // Si l'API tombe pendant que le joueur est sur une autre page que l'accueil (qui, lui,
  // gère déjà l'absence de données réseau), on le ramène automatiquement — pas
  // seulement un blocage au clic sur la sidebar.
  useEffect(() => {
    if (!apiReachable) setPage("home");
  }, [apiReachable]);

  useEffect(() => {
    invoke<UserInfo | null>("restore_session").then((restored) => {
      if (restored) {
        setUser(restored);
        setPlayState({ kind: "idle", detail: "" });
      }
    });
    invoke<Settings>("fetch_settings")
      .then(setSettings)
      .catch(() => {});
  }, []);

  // Dépend de la présence d'un user (booléen), pas de l'objet lui-même : sinon chaque
  // refresh réussi (qui appelle setUser avec un nouvel objet) redéclencherait cet
  // effet et relancerait un refresh immédiat en boucle.
  const isLoggedIn = user !== null;

  // Extrait de l'effet ci-dessous pour pouvoir aussi être appelé immédiatement après un
  // `save_mods`/`save_bepinex` réussi (voir ModsPage.onModpackUpdated) — sinon un admin
  // qui vient d'enregistrer devrait attendre jusqu'à SESSION_REFRESH_INTERVAL_MS avant de
  // voir le bouton se scinder en "Mettre à jour", même sur son propre launcher.
  const checkForUpdate = useCallback(() => {
    if (!isLoggedIn || !hasLocalManifest || !apiReachable) return;
    invoke<boolean>("check_update_available", { slug: effectiveModpackSlug, mode: launchMode })
      .then(setUpdateAvailable)
      .catch(() => {});
  }, [isLoggedIn, hasLocalManifest, apiReachable, launchMode, effectiveModpackSlug]);

  // Revérifié à chaque fois que l'API redevient joignable (pas seulement à l'ouverture),
  // et en continu tant que le launcher reste ouvert (même cadence que la revalidation de
  // session ci-dessous) — un admin a pu modifier la liste de mods (ajout ou retrait, pas
  // seulement une mise à jour de fichier) depuis un autre launcher, alors que l'API est
  // restée joignable en permanence sur celui-ci, donc `apiReachable` ne serait jamais
  // retombé pour redéclencher ce check.
  useEffect(() => {
    if (!isLoggedIn || !hasLocalManifest || !apiReachable) return;
    checkForUpdate();
    const interval = setInterval(checkForUpdate, SESSION_REFRESH_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [isLoggedIn, hasLocalManifest, apiReachable, checkForUpdate]);

  useEffect(() => {
    if (!isLoggedIn) return;

    let cancelled = false;

    async function refresh() {
      const outcome = await invoke<RefreshOutcome>("refresh_session");
      if (cancelled) return;

      if (outcome.kind === "ok") {
        setUser(outcome.user);
      } else if (outcome.kind === "loggedOut") {
        setUser(null);
        setPlayState({
          kind: "warning",
          detail:
            "Ton accès a été retiré (rôle Discord manquant ou compte banni). Demande à un " +
            "admin du Discord Fedoheim si besoin, puis reconnecte-toi.",
        });
      }
      // "error" = souci transitoire (réseau, Discord indisponible...) : on garde la
      // session actuelle et on retentera au prochain intervalle.
    }

    // Vérif immédiate à l'ouverture du launcher / juste après connexion (le règlement
    // ou le rôle Discord ont pu changer depuis la dernière session), puis en continu.
    refresh();
    const interval = setInterval(refresh, SESSION_REFRESH_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [isLoggedIn]);

  async function handleLogin() {
    setLoggingIn(true);
    try {
      const loggedInUser = await invoke<UserInfo>("login");
      setUser(loggedInUser);
      setPlayState({ kind: "idle", detail: "" });
    } catch (err) {
      const message = String(err);
      if (message.includes("Login cancelled")) {
        setPlayState({
          kind: "idle",
          detail: "Connexion annulée. Connecte-toi pour rejoindre notre serveur.",
        });
      } else if (message.startsWith(AUTH_WARNING_PREFIX)) {
        setPlayState({ kind: "warning", detail: message.slice(AUTH_WARNING_PREFIX.length) });
      } else {
        setPlayState({ kind: "error", detail: message });
      }
    } finally {
      setLoggingIn(false);
    }
  }

  async function handleCancelLogin() {
    await invoke("cancel_login");
  }

  async function handleLogout() {
    await invoke("logout");
    setUser(null);
    setPlayState({ kind: "idle", detail: "Connecte-toi pour rejoindre notre serveur." });
  }

  // Écoute commune à `handlePlay`/`handleUpdate`/`handleRepair` : met à jour le texte de
  // statut et la progression (label/current/total, voir SyncProgress) à chaque event. La
  // phase "offline" n'a pas de progression à afficher (repli instantané, pas une synchro
  // en plusieurs étapes) — seul `play` peut l'émettre, et repasse par le texte de statut
  // classique plutôt que par la barre.
  function listenToSyncProgress() {
    return listen<SyncProgress>("sync-progress", (event) => {
      const { phase, label, current, total } = event.payload;
      if (phase === "offline") {
        setPlayState({ kind: "busy", detail: label });
        setProgress(null);
        return;
      }
      setPlayState({ kind: "busy", detail: label });
      setProgress({ current, total });
    });
  }

  // Une seule action : synchronise BepInEx + les mods (progression via l'event
  // "sync-progress"), puis lance le jeu — voir la commande Rust `play`. Si l'API est
  // injoignable mais qu'une installation existait déjà, `play` bascule en mode hors
  // ligne (phase "offline") plutôt que d'échouer.
  async function handlePlay(mode: "player" | "admin") {
    setLaunchMode(mode);
    setPlayState({ kind: "busy", detail: "Préparation..." });
    setProgress(null);
    const unlisten = await listenToSyncProgress();
    try {
      await invoke("play", { slug: effectiveModpackSlug, mode });
      setPlayState({ kind: "idle", detail: "" });
      setHasLocalManifest(true);
      setUpdateAvailable(false);
    } catch (err) {
      setPlayState({ kind: "error", detail: String(err) });
    } finally {
      unlisten();
      setProgress(null);
    }
  }

  // Synchronise sans lancer — proposé à côté de "Jouer" seulement quand une mise à jour
  // est détectée (voir `check_update_available`), pour laisser le choix entre mettre à
  // jour maintenant ou continuer avec l'installation actuelle.
  async function handleUpdate(mode: "player" | "admin") {
    setLaunchMode(mode);
    setPlayState({ kind: "busy", detail: "Mise à jour..." });
    setProgress(null);
    const unlisten = await listenToSyncProgress();
    try {
      await invoke("sync_modpack", { slug: effectiveModpackSlug, mode });
      setHasLocalManifest(true);
      // Reste "busy" (boutons désactivés) pendant ce court message de confirmation —
      // sinon le passage de deux boutons à un seul est instantané et illisible.
      setPlayState({ kind: "busy", detail: "✓ Modpack à jour" });
      await new Promise((resolve) => setTimeout(resolve, UPDATE_SUCCESS_DISPLAY_MS));
      setUpdateAvailable(false);
      setPlayState({ kind: "idle", detail: "" });
    } catch (err) {
      setPlayState({ kind: "error", detail: String(err) });
    } finally {
      unlisten();
      setProgress(null);
    }
  }

  // Efface BepInEx + tous les mods installés localement puis retélécharge tout depuis
  // zéro (voir la commande Rust `repair_modpack`) — pour une install locale incohérente
  // (mod mal extrait, plantage en cours de sync...) que "Mettre à jour" ne résoudrait
  // pas puisqu'elle ne retouche que les fichiers dont le sha256 a changé. Confirmation
  // requise : ça supprime des fichiers locaux et peut prendre un moment à retélécharger.
  async function handleRepair(mode: "player" | "admin") {
    setSettingsMenuOpen(false);
    const confirmed = await confirm(
      "Réparer supprime BepInEx et tous les mods installés localement, puis retélécharge " +
        "tout depuis le serveur. Ça peut prendre un moment selon ta connexion — continuer ?",
    );
    if (!confirmed) return;

    setLaunchMode(mode);
    setPlayState({ kind: "busy", detail: "Réparation..." });
    setProgress(null);
    const unlisten = await listenToSyncProgress();
    try {
      await invoke("repair_modpack", { slug: effectiveModpackSlug, mode });
      setHasLocalManifest(true);
      setUpdateAvailable(false);
      setPlayState({ kind: "busy", detail: "✓ Réparation terminée" });
      await new Promise((resolve) => setTimeout(resolve, UPDATE_SUCCESS_DISPLAY_MS));
      setPlayState({ kind: "idle", detail: "" });
    } catch (err) {
      setPlayState({ kind: "error", detail: String(err) });
    } finally {
      unlisten();
      setProgress(null);
    }
  }

  // Envoie le LogOutput.log du profil actif au salon Discord de support (voir la
  // commande Rust `send_log_to_discord`) — pas de choix de mode ici (contrairement à
  // Jouer/Mettre à jour/Réparer), c'est juste une lecture locale + un envoi, pas une
  // resynchronisation qui dépendrait du modpack ciblé.
  async function handleSendLog() {
    setSettingsMenuOpen(false);
    setPlayState({ kind: "busy", detail: "Envoi du log..." });
    try {
      await invoke("send_log_to_discord");
      setPlayState({ kind: "busy", detail: "✓ Log envoyé" });
      await new Promise((resolve) => setTimeout(resolve, UPDATE_SUCCESS_DISPLAY_MS));
      setPlayState({ kind: "idle", detail: "" });
    } catch (err) {
      setPlayState({ kind: "error", detail: String(err) });
    }
  }

  // Lance directement avec l'installation existante, sans revérifier ni télécharger de
  // mise à jour — pendant qu'une mise à jour est disponible, le joueur garde le choix
  // de continuer avec ce qu'il a déjà plutôt que d'attendre.
  async function handleLaunchOnly() {
    setPlayState({ kind: "busy", detail: "Lancement..." });
    try {
      await invoke("launch_only");
      setPlayState({ kind: "idle", detail: "" });
    } catch (err) {
      setPlayState({ kind: "error", detail: String(err) });
    }
  }

  // Point d'entrée commun à "Jouer"/"Télécharger", "Mettre à jour" et "Réparer" — les
  // trois actions qui resynchronisent le modpack et ont donc besoin de savoir dans
  // quel mode (voir CLAUDE.md). Un admin est reredemandé à chaque appel (pas de mode
  // mémorisé au-delà de l'appel en cours) ; un joueur normal reste toujours en "player"
  // sans jamais voir la popup.
  function requestPrimaryAction(action: "play" | "update" | "repair") {
    if (!user?.isAdmin) {
      runPrimaryAction(action, "player");
      return;
    }
    setPendingAction(action);
  }

  function runPrimaryAction(action: "play" | "update" | "repair", mode: "player" | "admin") {
    if (action === "play") handlePlay(mode);
    else if (action === "update") handleUpdate(mode);
    else handleRepair(mode);
  }

  function confirmModeChoice(mode: "player" | "admin") {
    const action = pendingAction;
    setPendingAction(null);
    if (action) runPrimaryAction(action, mode);
  }

  // Confirme avant de quitter la page Mods si une édition non enregistrée est en cours
  // (voir ModsPage.onDirtyChange) — un clic sur la sidebar ne doit pas la faire perdre
  // en silence.
  const handleNavigate = useCallback(
    async (target: Page) => {
      if (modsDirty && page === "mods" && target !== "mods") {
        const confirmed = await confirm(
          "Tu as des modifications de mods non enregistrées — quitter quand même ?",
        );
        if (!confirmed) return;
      }
      setPage(target);
    },
    [modsDirty, page],
  );

  const busy = playState.kind === "busy" || loggingIn;
  // Rien à faire hors ligne sans installation existante : l'API ne répond pas pour
  // télécharger quoi que ce soit, et il n'y a rien de local à lancer (voir `play` côté
  // Rust, qui refuserait de toute façon avec la même condition).
  const canPlay = apiReachable || hasLocalManifest;
  const progressPercent =
    progress && progress.total > 0
      ? Math.min(100, Math.round((progress.current / progress.total) * 100))
      : null;

  // Surcharge les variables d'accent de tokens.css pour tout ce qui vit sous ce
  // conteneur (boutons, onglet actif, focus, sélection...) quand un profil de test
  // coloré est actif — pour qu'un admin ne puisse jamais confondre visuellement un
  // profil de test avec la production, où l'accent Fedoheim par défaut reste inchangé.
  // `activeProfileColor` est déjà `null` pour le profil production (voir
  // `selectProfile`), donc cette condition suffit à elle seule.
  const profileThemeStyle = activeProfileColor
    ? ({
        "--accent": activeProfileColor,
        "--accent-soft": hexToRgba(activeProfileColor, 0.14),
        "--accent-strong": hexToRgba(activeProfileColor, 0.55),
      } as React.CSSProperties)
    : undefined;

  // Une fois connecté, le joueur doit valider le règlement puis renseigner son Steam ID
  // avant d'accéder au reste du launcher (voir CLAUDE.md / flow d'onboarding). L'API
  // impose la même contrainte côté manifest (requireOnboarded) — ceci n'est qu'un
  // guidage UI, la vraie barrière est côté serveur.
  if (user && !user.hasAcceptedRules) {
    return (
      <div className="shell shell--onboarding" style={profileThemeStyle}>
        <ParticleField />
        <AcceptRulesGate onAccepted={setUser} onLogout={handleLogout} />
      </div>
    );
  }

  if (user && !user.steamId) {
    return (
      <div className="shell shell--onboarding" style={profileThemeStyle}>
        <ParticleField />
        <SteamIdGate onSaved={setUser} onLogout={handleLogout} />
      </div>
    );
  }

  return (
    <div className="shell" style={profileThemeStyle}>
      <ParticleField />
      <CursorWisp />

      {pendingAction && (
        <div className="mode-modal-overlay" role="presentation" onClick={() => setPendingAction(null)}>
          <div
            className="mode-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="mode-modal-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 id="mode-modal-title">Dans quel mode ?</h2>
            <p className="mode-modal__hint">
              Mode Joueur : le modpack commun à tous les joueurs. Mode Admin : le modpack
              commun + les mods réservés aux admins.
            </p>
            <div className="mode-modal__options">
              <button type="button" className="btn btn--ghost" onClick={() => confirmModeChoice("player")}>
                Joueur
              </button>
              <button type="button" className="btn btn--accent" onClick={() => confirmModeChoice("admin")}>
                Admin
              </button>
            </div>
            <button
              type="button"
              className="mode-modal__cancel"
              onClick={() => setPendingAction(null)}
            >
              Annuler
            </button>
          </div>
        </div>
      )}

      {(!apiReachable || appUpdate) && (
        <div className="shell__banners">
          {!apiReachable && (
            <div className="shell__banner" role="alert">
              <span className="shell__banner-icon" aria-hidden="true">
                😅
              </span>
              Notre serveur ne répond pas pour le moment — quelqu'un a sûrement oublié de
              payer la facture.
              <span className="shell__banner-countdown">
                Nouvelle tentative dans {secondsUntilCheck}s...
              </span>
            </div>
          )}

          {appUpdate && (
            <div className="shell__banner shell__banner--update" role="status">
              <span className="shell__banner-icon" aria-hidden="true">
                ✨
              </span>
              Nouvelle version du launcher disponible ({appUpdate.version}).
              <button
                type="button"
                className="shell__banner-action"
                onClick={handleInstallAppUpdate}
                disabled={installingAppUpdate}
              >
                {installingAppUpdate ? "Installation..." : "Mettre à jour"}
              </button>
            </div>
          )}
        </div>
      )}

      <Sidebar
        current={page}
        onNavigate={handleNavigate}
        onSupport={() => openUrl(settings.buyMeACoffeeUrl)}
        isAdmin={user?.isAdmin ?? false}
        navLocked={!apiReachable}
        isLoggedIn={isLoggedIn}
        onLogout={handleLogout}
      />

      <main className="shell__content">
        {page === "home" && (
          <HomePage heroEyebrow={settings.heroEyebrow} heroTagline={settings.heroTagline} />
        )}
        {page === "announcements" && <AnnouncementsPage isAdmin={user?.isAdmin ?? false} />}
        {page === "mods" && (
          <ModsPage
            slug={effectiveModpackSlug}
            isAdmin={user?.isAdmin ?? false}
            onDirtyChange={setModsDirty}
            onModpackUpdated={checkForUpdate}
          />
        )}
        {page === "rules" && (
          <RulesPage
            isAdmin={user?.isAdmin ?? false}
            hasAcceptedRules={user?.hasAcceptedRules ?? false}
            rulesAcceptedAt={user?.rulesAcceptedAt ?? null}
          />
        )}
        {page === "faq" && <FaqPage isAdmin={user?.isAdmin ?? false} />}
        {page === "profiles" && user?.isAdmin && (
          <ProfilesPage
            activeSlug={activeProfileSlug}
            onSelect={selectProfile}
            onModpackUpdated={checkForUpdate}
          />
        )}
        {page === "settings" && user?.isAdmin && <SettingsPage onSaved={setSettings} />}
      </main>

      <footer className="shell__playbar">
        <div className="shell__playbar-status">
          {user ? (
            <div className="shell__playbar-identity">
              <div className="shell__playbar-avatar" aria-hidden="true">
                {user.discordAvatar ? (
                  <img className="shell__playbar-avatar-img" src={user.discordAvatar} alt="" />
                ) : (
                  user.discordUsername.slice(0, 1).toUpperCase()
                )}
              </div>
              <div>
                <p className="shell__playbar-connected-as">Connecté en tant que</p>
                <p className="shell__playbar-username">
                  {user.discordUsername}
                  {user.isAdmin && <span className="shell__playbar-admin-badge">Admin</span>}
                  {user.isAdmin && launchMode === "admin" && (
                    <span
                      className="shell__playbar-admin-badge"
                      title="Le modpack actuellement synchronisé/à jour inclut les mods réservés aux admins"
                    >
                      Mode admin
                    </span>
                  )}
                  {user.isAdmin && (
                    <span
                      className="shell__playbar-admin-badge"
                      title='"Jouer"/"Mettre à jour"/"Réparer" ciblent ce profil — voir la page Profils'
                    >
                      {activeProfileSlug === PRODUCTION_MODPACK_SLUG
                        ? "Production"
                        : activeProfileSlug}
                    </span>
                  )}
                </p>
              </div>
            </div>
          ) : (
            <span className="shell__playbar-title">Fedoheim</span>
          )}
        </div>
        <div className="shell__playbar-actions-wrap">
          {playState.kind === "busy" && progress && progressPercent !== null ? (
            <div
              className="shell__playbar-progress"
              role="progressbar"
              aria-valuenow={progressPercent}
              aria-valuemin={0}
              aria-valuemax={100}
            >
              <div className="shell__playbar-progress-track">
                <div
                  className="shell__playbar-progress-fill"
                  style={{ width: `${progressPercent}%` }}
                />
              </div>
              <span className="shell__playbar-progress-pct">{progressPercent}%</span>
            </div>
          ) : (
            playState.detail && (
              <span
                className={`shell__playbar-detail ${
                  playState.kind === "error"
                    ? "is-error"
                    : playState.kind === "warning"
                      ? "is-warning"
                      : playState.detail.startsWith("✓")
                        ? "is-success"
                        : ""
                }`}
              >
                {playState.detail}
              </span>
            )
          )}
          <div className="shell__playbar-actions">
            {user ? (
              updateAvailable ? (
                <>
                  <button
                    type="button"
                    className="btn btn--ghost"
                    onClick={handleLaunchOnly}
                    disabled={busy || !canPlay}
                  >
                    Jouer
                  </button>
                  <button
                    type="button"
                    className="btn btn--accent"
                    onClick={() => requestPrimaryAction("update")}
                    disabled={busy || !apiReachable}
                  >
                    Mettre à jour
                  </button>
                </>
              ) : (
                <button
                  type="button"
                  className="btn btn--accent"
                  onClick={() => requestPrimaryAction("play")}
                  disabled={busy || !canPlay}
                  title={
                    !canPlay ? "Indisponible hors ligne sans installation existante" : undefined
                  }
                >
                  {hasLocalManifest ? "Jouer" : "Télécharger"}
                </button>
              )
            ) : (
              <>
                <button
                  type="button"
                  className="btn btn--discord"
                  onClick={handleLogin}
                  disabled={loggingIn || !apiReachable}
                  title={
                    !apiReachable ? "Indisponible tant que le serveur ne répond pas" : undefined
                  }
                >
                  <svg viewBox="0 0 24 24" width="18" height="18" fill="currentColor">
                    <path d="M20.3 5.3A17.5 17.5 0 0 0 15.9 4l-.2.4a12 12 0 0 1 3.6 1.8 13.9 13.9 0 0 0-11.6 0A12 12 0 0 1 11.3 4l-.2-.4a17.5 17.5 0 0 0-4.4 1.3C3.7 8.6 3 12.6 3.3 16.6a17.7 17.7 0 0 0 4.9 2.5l.6-1a11 11 0 0 1-1.7-.8l.4-.3a13 13 0 0 0 9 0l.4.3a11 11 0 0 1-1.7.8l.6 1a17.7 17.7 0 0 0 4.9-2.5c.4-4.6-.6-8.6-2.4-11.3ZM9.7 14.9c-.8 0-1.5-.8-1.5-1.7s.7-1.7 1.5-1.7 1.5.8 1.5 1.7-.7 1.7-1.5 1.7Zm4.6 0c-.8 0-1.5-.8-1.5-1.7s.7-1.7 1.5-1.7 1.5.8 1.5 1.7-.7 1.7-1.5 1.7Z" />
                  </svg>
                  {loggingIn ? "Connexion en cours..." : "Se connecter avec Discord"}
                </button>
                {loggingIn && (
                  <button type="button" className="btn btn--ghost" onClick={handleCancelLogin}>
                    Annuler
                  </button>
                )}
              </>
            )}
            {user && (
              <div className="shell__playbar-settings" ref={settingsMenuRef}>
                <button
                  type="button"
                  className="shell__playbar-settings-btn"
                  onClick={() => setSettingsMenuOpen((open) => !open)}
                  disabled={busy}
                  aria-haspopup="menu"
                  aria-expanded={settingsMenuOpen}
                  title="Options avancées"
                >
                  <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="1.8">
                    <circle cx="12" cy="12" r="3" />
                    <path d="M19.4 15a1.7 1.7 0 0 0 .34 1.87l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.7 1.7 0 0 0-1.87-.34 1.7 1.7 0 0 0-1.03 1.56V21a2 2 0 1 1-4 0v-.09A1.7 1.7 0 0 0 8.98 19.3a1.7 1.7 0 0 0-1.87.34l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.7 1.7 0 0 0 .34-1.87 1.7 1.7 0 0 0-1.56-1.03H3a2 2 0 1 1 0-4h.09A1.7 1.7 0 0 0 4.7 8.98a1.7 1.7 0 0 0-.34-1.87l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.7 1.7 0 0 0 1.87.34H9.06A1.7 1.7 0 0 0 10.09 3.09V3a2 2 0 1 1 4 0v.09a1.7 1.7 0 0 0 1.03 1.56 1.7 1.7 0 0 0 1.87-.34l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.7 1.7 0 0 0-.34 1.87V9.06a1.7 1.7 0 0 0 1.56 1.03H21a2 2 0 1 1 0 4h-.09a1.7 1.7 0 0 0-1.51 1.03Z" />
                  </svg>
                </button>
                {settingsMenuOpen && (
                  <div className="shell__playbar-menu" role="menu">
                    <button
                      type="button"
                      role="menuitem"
                      className="shell__playbar-menu-item"
                      onClick={() => requestPrimaryAction("repair")}
                      disabled={busy || !apiReachable}
                      title={
                        !apiReachable
                          ? "Nécessite une connexion au serveur pour retélécharger l'installation"
                          : undefined
                      }
                    >
                      Réparer
                    </button>
                    <button
                      type="button"
                      role="menuitem"
                      className="shell__playbar-menu-item"
                      onClick={handleSendLog}
                      disabled={busy || !apiReachable}
                      title={
                        !apiReachable
                          ? "Nécessite une connexion au serveur pour envoyer le log"
                          : undefined
                      }
                    >
                      Envoyer log
                    </button>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </footer>
    </div>
  );
}

export default App;
