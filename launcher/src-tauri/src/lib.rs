mod active_profile;
mod announcements;
mod auth;
mod config;
mod content;
mod modpack;
mod online;
mod session;
mod settings;
mod valheim;

use announcements::{Announcement, AnnouncementDraft, AnnouncementPage};
use auth::PendingLogin;
use content::FaqEntry;
use modpack::{
    BepinexStatus, BepinexWrite, BulkUpload, ConfigFileBulkUpload, ConfigFileUpload,
    ConfigFileWrite, FileUpload, ModInfo, ModWrite, ModpackProfile,
};
use online::{OnlinePlayers, PlayerStatsResponse};
use serde::Serialize;
use session::{Session, UserInfo};
use settings::Settings;
use std::sync::Mutex;
use tauri::{AppHandle, State};

struct AppState {
    http: reqwest::Client,
    session: Mutex<Option<Session>>,
    pending_login: PendingLogin,
}

fn current_token(state: &State<'_, AppState>) -> Result<String, String> {
    state
        .session
        .lock()
        .unwrap()
        .as_ref()
        .map(|s| s.token.clone())
        .ok_or_else(|| "Not logged in".to_string())
}

// Met à jour les infos utilisateur stockées (mémoire + disque) en gardant le même
// token, après une action qui modifie le profil (accepter le règlement, Steam ID...).
fn persist_updated_user(
    app: &AppHandle,
    state: &State<'_, AppState>,
    token: String,
    user: UserInfo,
) -> Result<UserInfo, String> {
    let session = Session {
        token,
        user: user.clone(),
    };
    session::save(app, &session)?;
    *state.session.lock().unwrap() = Some(session);
    Ok(user)
}

#[tauri::command]
fn restore_session(app: AppHandle, state: State<'_, AppState>) -> Option<UserInfo> {
    let session = session::load(&app)?;
    let user = session.user.clone();
    *state.session.lock().unwrap() = Some(session);
    Some(user)
}

#[tauri::command]
async fn login(app: AppHandle, state: State<'_, AppState>) -> Result<UserInfo, String> {
    let session = auth::login(&state.http, &state.pending_login).await?;
    session::save(&app, &session)?;
    let user = session.user.clone();
    *state.session.lock().unwrap() = Some(session);
    Ok(user)
}

// Annule une connexion Discord en cours (débloque le serveur loopback local). Sans
// effet si aucune connexion n'est en cours — le bouton "Annuler" ne peut de toute
// façon apparaître que pendant que login() est en vol côté frontend.
#[tauri::command]
fn cancel_login(state: State<'_, AppState>) {
    auth::cancel_login(&state.pending_login);
}

#[tauri::command]
fn logout(app: AppHandle, state: State<'_, AppState>) -> Result<(), String> {
    *state.session.lock().unwrap() = None;
    session::clear(&app)
}

#[derive(Debug, Clone, Serialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
enum RefreshOutcome {
    Ok { user: UserInfo },
    LoggedOut { message: String },
    Error { message: String },
}

// Appelé périodiquement par le frontend tant qu'un joueur est connecté, pour vérifier
// en tâche de fond que son rôle Discord est toujours valide sans jamais lui redemander
// de se reconnecter manuellement.
#[tauri::command]
async fn refresh_session(app: AppHandle, state: State<'_, AppState>) -> Result<RefreshOutcome, ()> {
    let token = match current_token(&state) {
        Ok(t) => t,
        Err(e) => return Ok(RefreshOutcome::Error { message: e }),
    };

    match auth::refresh(&state.http, &token).await {
        Ok(user) => {
            let session = Session {
                token,
                user: user.clone(),
            };
            let _ = session::save(&app, &session);
            *state.session.lock().unwrap() = Some(session);
            Ok(RefreshOutcome::Ok { user })
        }
        Err(auth::RefreshError::Unauthorized(message)) => {
            *state.session.lock().unwrap() = None;
            let _ = session::clear(&app);
            Ok(RefreshOutcome::LoggedOut { message })
        }
        Err(auth::RefreshError::Other(message)) => Ok(RefreshOutcome::Error { message }),
    }
}

// Une seule action "Jouer" : s'assure que BepInEx et les mods sont à jour dans le
// dossier profil externe (voir valheim::profile_dir), progression émise au fil de
// l'eau via l'event "sync-progress", puis lance le jeu via Steam. Remplace les
// anciennes commandes séparées `sync_modpack`/`launch_game`.
//
// Si l'API est injoignable (ou toute autre erreur au moment de récupérer le manifest)
// mais qu'un manifest a déjà été synchronisé avec succès par le passé (voir
// `modpack::save_local_manifest`/`load_local_manifest` — pas juste "un dossier BepInEx
// existe", une vraie trace de ce qui a été installé), on lance quand même avec cette
// installation plutôt que de bloquer le joueur pour une coupure réseau passagère — pas
// de sync, pas de vérification de mise à jour dans ce cas, juste "jouer avec ce qu'on
// a". Sans aucun manifest local connu, l'erreur d'origine (ex: API injoignable) est
// remontée telle quelle, il n'y a rien de fiable sur quoi se rabattre.
#[tauri::command]
async fn play(
    app: AppHandle,
    state: State<'_, AppState>,
    slug: String,
    mode: String,
) -> Result<(), String> {
    let token = current_token(&state)?;
    let profile_dir = valheim::profile_dir(&app)?;

    match modpack::fetch_manifest(&state.http, &token, &slug, &mode).await {
        Ok(manifest) => {
            let Some(bepinex) = &manifest.bepinex else {
                return Err(
                    "BepInEx n'est pas configuré pour ce modpack — contacte un admin.".to_string(),
                );
            };

            let total_steps = 1 + manifest.mods.len() as u32;
            modpack::ensure_bepinex(&state.http, &app, &profile_dir, bepinex, total_steps).await?;
            modpack::sync_mods(&state.http, &app, &profile_dir, &manifest.mods, total_steps)
                .await?;
            modpack::sync_config_files(&state.http, &profile_dir, &manifest.config_files).await?;
            modpack::save_local_manifest(&profile_dir, &manifest);
        }
        Err(err) => {
            let Some(manifest) = modpack::load_local_manifest(&profile_dir) else {
                return Err(err);
            };
            modpack::emit_progress(
                &app,
                "offline",
                &format!(
                    "Serveur injoignable — lancement avec {} mod(s) déjà installés",
                    manifest.mods.len()
                ),
                1,
                1,
            );
        }
    }

    let install_dir = valheim::find_install_dir()?;
    valheim::launch(&install_dir, &profile_dir)
}

// Le frontend s'en sert pour savoir si le bouton doit afficher "Télécharger" (rien
// d'installé pour l'instant) ou "Jouer", et pour le désactiver si l'API est injoignable
// et qu'il n'y a justement rien à lancer hors ligne (voir `play` ci-dessus).
#[tauri::command]
fn has_local_manifest(app: AppHandle) -> Result<bool, String> {
    let profile_dir = valheim::profile_dir(&app)?;
    Ok(modpack::load_local_manifest(&profile_dir).is_some())
}

// Compare le manifest actuellement servi par l'API à celui de la dernière installation
// réussie (voir `modpack::manifest_needs_update`) — permet à App.tsx de scinder le
// bouton "Jouer" en "Mettre à jour" + "Jouer" plutôt que de resynchroniser à chaque
// clic. `false` s'il n'y a rien d'installé localement (le bouton "Télécharger" gère déjà
// ce cas via `has_local_manifest`) ou si l'API est injoignable — pas la peine de
// remonter une erreur ici, "pas de mise à jour connue" est un repli raisonnable.
#[tauri::command]
async fn check_update_available(
    app: AppHandle,
    state: State<'_, AppState>,
    slug: String,
    mode: String,
) -> Result<bool, ()> {
    let Ok(profile_dir) = valheim::profile_dir(&app) else {
        return Ok(false);
    };
    let Some(local) = modpack::load_local_manifest(&profile_dir) else {
        return Ok(false);
    };
    let Ok(token) = current_token(&state) else {
        return Ok(false);
    };
    let remote = modpack::fetch_manifest(&state.http, &token, &slug, &mode).await;
    Ok(remote.is_ok_and(|remote| modpack::manifest_needs_update(&local, &remote)))
}

// Synchronise BepInEx + les mods sans lancer le jeu ensuite — action "Mettre à jour"
// explicite, distincte de `play` (qui lance après), pour laisser le joueur choisir entre
// mettre à jour maintenant ou continuer avec l'installation actuelle via `launch_only`.
#[tauri::command]
async fn sync_modpack(
    app: AppHandle,
    state: State<'_, AppState>,
    slug: String,
    mode: String,
) -> Result<(), String> {
    let token = current_token(&state)?;
    let profile_dir = valheim::profile_dir(&app)?;

    let manifest = modpack::fetch_manifest(&state.http, &token, &slug, &mode).await?;
    let Some(bepinex) = &manifest.bepinex else {
        return Err("BepInEx n'est pas configuré pour ce modpack — contacte un admin.".to_string());
    };

    let total_steps = 1 + manifest.mods.len() as u32;
    modpack::ensure_bepinex(&state.http, &app, &profile_dir, bepinex, total_steps).await?;
    modpack::sync_mods(&state.http, &app, &profile_dir, &manifest.mods, total_steps).await?;
    modpack::sync_config_files(&state.http, &profile_dir, &manifest.config_files).await?;
    modpack::save_local_manifest(&profile_dir, &manifest);
    Ok(())
}

// "Réparer" : efface complètement l'installation locale (dossier BepInEx + tous les
// mods, voir `modpack::wipe_local_install`) puis resynchronise depuis zéro — même
// séquence que `sync_modpack`, appelée après le nettoyage. Utile quand une install
// locale est incohérente/corrompue (mod mal extrait, marker désynchronisé...) sans que
// l'admin ait besoin de republier le modpack ; le joueur doit avoir du réseau pour que
// cette action ait un sens (voir la désactivation du bouton côté App.tsx quand l'API
// est injoignable).
#[tauri::command]
async fn repair_modpack(
    app: AppHandle,
    state: State<'_, AppState>,
    slug: String,
    mode: String,
) -> Result<(), String> {
    let token = current_token(&state)?;
    let profile_dir = valheim::profile_dir(&app)?;

    modpack::wipe_local_install(&profile_dir)?;

    let manifest = modpack::fetch_manifest(&state.http, &token, &slug, &mode).await?;
    let Some(bepinex) = &manifest.bepinex else {
        return Err("BepInEx n'est pas configuré pour ce modpack — contacte un admin.".to_string());
    };

    let total_steps = 1 + manifest.mods.len() as u32;
    modpack::ensure_bepinex(&state.http, &app, &profile_dir, bepinex, total_steps).await?;
    modpack::sync_mods(&state.http, &app, &profile_dir, &manifest.mods, total_steps).await?;
    modpack::sync_config_files(&state.http, &profile_dir, &manifest.config_files).await?;
    modpack::save_local_manifest(&profile_dir, &manifest);
    Ok(())
}

// Envoie le LogOutput.log du profil actif vers le salon Discord de support (bouton
// "Envoyer log", à côté de "Réparer" dans le menu Options avancées).
#[tauri::command]
async fn send_log_to_discord(app: AppHandle, state: State<'_, AppState>) -> Result<(), String> {
    let token = current_token(&state)?;
    let profile_dir = valheim::profile_dir(&app)?;
    let log_path = valheim::bepinex_dir(&profile_dir).join("LogOutput.log");
    if !log_path.exists() {
        return Err(
            "Aucun fichier de log trouvé — lance le jeu au moins une fois avant d'envoyer un log."
                .to_string(),
        );
    }
    modpack::send_log(&state.http, &token, &log_path).await
}

// Lance le jeu avec l'installation locale actuelle, sans vérifier ni télécharger de mise
// à jour — utilisé par le bouton "Jouer" quand une mise à jour est disponible mais que le
// joueur préfère continuer avec ce qu'il a déjà (voir App.tsx).
#[tauri::command]
fn launch_only(app: AppHandle) -> Result<(), String> {
    let profile_dir = valheim::profile_dir(&app)?;
    let install_dir = valheim::find_install_dir()?;
    valheim::launch(&install_dir, &profile_dir)
}

#[tauri::command]
async fn fetch_mods(state: State<'_, AppState>, slug: String) -> Result<Vec<ModInfo>, String> {
    modpack::fetch_mods(&state.http, &slug).await
}

#[tauri::command]
async fn fetch_rules(state: State<'_, AppState>) -> Result<Vec<String>, String> {
    content::fetch_rules(&state.http).await
}

#[tauri::command]
async fn fetch_faq(state: State<'_, AppState>) -> Result<Vec<FaqEntry>, String> {
    content::fetch_faq(&state.http).await
}

#[tauri::command]
async fn save_rules(state: State<'_, AppState>, rules: Vec<String>) -> Result<(), String> {
    let token = current_token(&state)?;
    content::save_rules(&state.http, &token, rules).await
}

#[tauri::command]
async fn save_faq(state: State<'_, AppState>, faq: Vec<FaqEntry>) -> Result<(), String> {
    let token = current_token(&state)?;
    content::save_faq(&state.http, &token, faq).await
}

#[tauri::command]
async fn fetch_announcements(
    state: State<'_, AppState>,
    limit: Option<u32>,
    offset: u32,
) -> Result<AnnouncementPage, String> {
    announcements::fetch_announcements(&state.http, limit, offset).await
}

#[tauri::command]
async fn post_announcement(
    state: State<'_, AppState>,
    title: Option<String>,
    message: String,
    images: Vec<String>,
) -> Result<Announcement, String> {
    let token = current_token(&state)?;
    let draft = AnnouncementDraft {
        title,
        message,
        images,
    };
    announcements::post_announcement(&state.http, &token, &draft).await
}

#[tauri::command]
async fn update_announcement(
    state: State<'_, AppState>,
    id: i64,
    title: Option<String>,
    message: String,
    images: Vec<String>,
) -> Result<Announcement, String> {
    let token = current_token(&state)?;
    let draft = AnnouncementDraft {
        title,
        message,
        images,
    };
    announcements::update_announcement(&state.http, &token, id, &draft).await
}

#[tauri::command]
async fn delete_announcement(state: State<'_, AppState>, id: i64) -> Result<(), String> {
    let token = current_token(&state)?;
    announcements::delete_announcement(&state.http, &token, id).await
}

// Ouvre le sélecteur de fichier natif, puis upload l'image choisie. Renvoie None si
// l'utilisateur annule la sélection.
#[tauri::command]
async fn pick_and_upload_image(
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<Option<String>, String> {
    use tauri_plugin_dialog::DialogExt;

    let (tx, rx) = tokio::sync::oneshot::channel();
    app.dialog()
        .file()
        .add_filter("Images", &["png", "jpg", "jpeg", "webp", "gif"])
        .pick_file(move |file| {
            let _ = tx.send(file);
        });

    let picked = rx.await.map_err(|e| e.to_string())?;
    let Some(file) = picked else {
        return Ok(None);
    };
    let path = file.into_path().map_err(|e| e.to_string())?;

    let token = current_token(&state)?;
    let url = announcements::upload_image(&state.http, &token, &path.to_string_lossy()).await?;
    Ok(Some(url))
}

#[tauri::command]
async fn fetch_mods_full(
    state: State<'_, AppState>,
    slug: String,
) -> Result<Vec<ModWrite>, String> {
    let token = current_token(&state)?;
    modpack::fetch_mods_full(&state.http, &token, &slug).await
}

#[tauri::command]
async fn save_mods(
    state: State<'_, AppState>,
    slug: String,
    mods: Vec<ModWrite>,
) -> Result<(), String> {
    let token = current_token(&state)?;
    modpack::save_mods(&state.http, &token, &slug, mods).await
}

#[tauri::command]
async fn accept_rules(app: AppHandle, state: State<'_, AppState>) -> Result<UserInfo, String> {
    let token = current_token(&state)?;
    let user = auth::accept_rules(&state.http, &token).await?;
    persist_updated_user(&app, &state, token, user)
}

#[tauri::command]
async fn set_steam_id(
    app: AppHandle,
    state: State<'_, AppState>,
    steam_id: String,
) -> Result<UserInfo, String> {
    let token = current_token(&state)?;
    let user = auth::set_steam_id(&state.http, &token, &steam_id).await?;
    persist_updated_user(&app, &state, token, user)
}

// Sélecteur de fichier natif (filtré .zip) → upload à l'API. Sert aussi bien à un mod
// qu'au package BepInEx (même mécanique), sur le modèle de `pick_and_upload_image`
// ci-dessus. `None` si l'admin annule la sélection.
#[tauri::command]
async fn pick_zip_and_upload(
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<Option<FileUpload>, String> {
    use tauri_plugin_dialog::DialogExt;

    let (tx, rx) = tokio::sync::oneshot::channel();
    app.dialog()
        .file()
        .add_filter("Archive zip", &["zip"])
        .pick_file(move |file| {
            let _ = tx.send(file);
        });

    let picked = rx.await.map_err(|e| e.to_string())?;
    let Some(file) = picked else {
        return Ok(None);
    };
    let path = file.into_path().map_err(|e| e.to_string())?;

    let token = current_token(&state)?;
    let upload = modpack::upload_zip(&state.http, &token, path).await?;
    Ok(Some(upload))
}

// Même sélecteur que `pick_zip_and_upload` ci-dessus, mais multi-sélection — pour l'envoi
// en masse de plusieurs mods d'un coup depuis "+ Ajouter des mods". Chaque archive est
// uploadée l'une après l'autre ; un échec sur l'une (réseau, zip corrompu...) n'annule
// pas les autres, il est juste remonté dans `errors` (voir `BulkUpload`) pour que
// l'admin sache lequel a échoué sans perdre les mods déjà importés avec succès.
#[tauri::command]
async fn pick_zips_and_upload(
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<BulkUpload, String> {
    use tauri_plugin_dialog::DialogExt;

    let (tx, rx) = tokio::sync::oneshot::channel();
    app.dialog()
        .file()
        .add_filter("Archive zip", &["zip"])
        .pick_files(move |files| {
            let _ = tx.send(files);
        });

    let picked = rx.await.map_err(|e| e.to_string())?;
    let Some(files) = picked else {
        return Ok(BulkUpload {
            uploads: Vec::new(),
            errors: Vec::new(),
        });
    };

    let token = current_token(&state)?;
    let mut uploads = Vec::with_capacity(files.len());
    let mut errors = Vec::new();
    for file in files {
        let path = match file.into_path() {
            Ok(p) => p,
            Err(e) => {
                errors.push(e.to_string());
                continue;
            }
        };
        let label = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| path.to_string_lossy().into_owned());
        match modpack::upload_zip(&state.http, &token, path).await {
            Ok(upload) => uploads.push(upload),
            Err(e) => errors.push(format!("{label} : {e}")),
        }
    }
    Ok(BulkUpload { uploads, errors })
}

// Sélecteur de fichier natif (pas de filtre d'extension, un fichier de config peut être
// .cfg/.yml/.json/...) → upload à l'API. Sur le modèle de `pick_zip_and_upload`, mais
// pour un fichier brut (pas un zip) — voir `modpack::upload_config_file`.
#[tauri::command]
async fn pick_config_file_and_upload(
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<Option<ConfigFileUpload>, String> {
    use tauri_plugin_dialog::DialogExt;

    let (tx, rx) = tokio::sync::oneshot::channel();
    app.dialog().file().pick_file(move |file| {
        let _ = tx.send(file);
    });

    let picked = rx.await.map_err(|e| e.to_string())?;
    let Some(file) = picked else {
        return Ok(None);
    };
    let path = file.into_path().map_err(|e| e.to_string())?;

    let token = current_token(&state)?;
    let upload = modpack::upload_config_file(&state.http, &token, path).await?;
    Ok(Some(upload))
}

// Même sélecteur que `pick_config_file_and_upload` ci-dessus, mais multi-sélection —
// même principe que `pick_zips_and_upload` pour les mods : chaque fichier est uploadé
// l'un après l'autre, un échec sur l'un n'annule pas les autres (voir
// `ConfigFileBulkUpload`).
#[tauri::command]
async fn pick_config_files_and_upload(
    app: AppHandle,
    state: State<'_, AppState>,
) -> Result<ConfigFileBulkUpload, String> {
    use tauri_plugin_dialog::DialogExt;

    let (tx, rx) = tokio::sync::oneshot::channel();
    app.dialog().file().pick_files(move |files| {
        let _ = tx.send(files);
    });

    let picked = rx.await.map_err(|e| e.to_string())?;
    let Some(files) = picked else {
        return Ok(ConfigFileBulkUpload {
            uploads: Vec::new(),
            errors: Vec::new(),
        });
    };

    let token = current_token(&state)?;
    let mut uploads = Vec::with_capacity(files.len());
    let mut errors = Vec::new();
    for file in files {
        let path = match file.into_path() {
            Ok(p) => p,
            Err(e) => {
                errors.push(e.to_string());
                continue;
            }
        };
        let label = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| path.to_string_lossy().into_owned());
        match modpack::upload_config_file(&state.http, &token, path).await {
            Ok(upload) => uploads.push(upload),
            Err(e) => errors.push(format!("{label} : {e}")),
        }
    }
    Ok(ConfigFileBulkUpload { uploads, errors })
}

// Récupère le contenu texte d'un fichier de config déjà uploadé, pour préremplir la
// zone d'édition inline (voir ModsPage "Éditer") — pas besoin de token, voir
// `modpack::fetch_config_file_content`.
#[tauri::command]
async fn fetch_config_file_content(
    state: State<'_, AppState>,
    url: String,
) -> Result<String, String> {
    modpack::fetch_config_file_content(&state.http, &url).await
}

// Enregistre un contenu édité directement dans le launcher — même résultat qu'un
// nouveau `pick_config_file_and_upload`, mais à partir du texte déjà chargé plutôt que
// d'un nouveau fichier choisi sur disque.
#[tauri::command]
async fn save_config_file_text(
    state: State<'_, AppState>,
    filename: String,
    content: String,
) -> Result<ConfigFileUpload, String> {
    let token = current_token(&state)?;
    modpack::upload_config_file_text(&state.http, &token, &filename, content).await
}

#[tauri::command]
async fn fetch_config_files_full(
    state: State<'_, AppState>,
    slug: String,
) -> Result<Vec<ConfigFileWrite>, String> {
    let token = current_token(&state)?;
    modpack::fetch_config_files(&state.http, &token, &slug).await
}

#[tauri::command]
async fn save_config_files(
    state: State<'_, AppState>,
    slug: String,
    files: Vec<ConfigFileWrite>,
) -> Result<(), String> {
    let token = current_token(&state)?;
    modpack::save_config_files(&state.http, &token, &slug, files).await
}

#[tauri::command]
async fn fetch_bepinex(
    state: State<'_, AppState>,
    slug: String,
) -> Result<Option<BepinexWrite>, String> {
    let token = current_token(&state)?;
    modpack::fetch_bepinex(&state.http, &token, &slug).await
}

// Pas d'auth requise : BepInEx est un mod comme un autre du point de vue du joueur, il
// doit pouvoir voir son statut sans être admin (voir modpack::BepinexStatus).
#[tauri::command]
async fn fetch_bepinex_status(
    state: State<'_, AppState>,
    slug: String,
) -> Result<BepinexStatus, String> {
    modpack::fetch_bepinex_status(&state.http, &slug).await
}

// Pas d'auth requise : qui est en ligne doit être visible sans compte, comme le statut
// BepInEx ci-dessus (voir CLAUDE.md, ce sera à terme un mod serveur qui l'alimente).
#[tauri::command]
async fn fetch_online_players(
    state: State<'_, AppState>,
    slug: String,
) -> Result<OnlinePlayers, String> {
    online::fetch_online_players(&state.http, &slug).await
}

// Pas d'auth requise, même principe que fetch_online_players ci-dessus (page "Joueurs").
#[tauri::command]
async fn fetch_player_stats(
    state: State<'_, AppState>,
    slug: String,
) -> Result<PlayerStatsResponse, String> {
    online::fetch_player_stats(&state.http, &slug).await
}

#[tauri::command]
async fn fetch_report_token(
    state: State<'_, AppState>,
    slug: String,
) -> Result<Option<String>, String> {
    let token = current_token(&state)?;
    modpack::fetch_report_token(&state.http, &token, &slug).await
}

#[tauri::command]
async fn regenerate_report_token(
    state: State<'_, AppState>,
    slug: String,
) -> Result<String, String> {
    let token = current_token(&state)?;
    modpack::regenerate_report_token(&state.http, &token, &slug).await
}

// Nettoyage des fichiers importés (zip/icône) qui ne seront finalement pas utilisés —
// voir le bouton "Annuler" de l'éditeur de mods.
#[tauri::command]
async fn delete_uploaded_files(
    state: State<'_, AppState>,
    urls: Vec<String>,
) -> Result<(), String> {
    let token = current_token(&state)?;
    modpack::delete_files(&state.http, &token, &urls).await
}

// Profils de modpack (production + profils de test) — voir modpack::ModpackProfile.
// Réservé aux admins, comme le reste de l'édition.
#[tauri::command]
async fn list_modpacks(state: State<'_, AppState>) -> Result<Vec<ModpackProfile>, String> {
    let token = current_token(&state)?;
    modpack::list_modpacks(&state.http, &token).await
}

#[tauri::command]
async fn create_modpack(
    state: State<'_, AppState>,
    slug: String,
    name: String,
) -> Result<ModpackProfile, String> {
    let token = current_token(&state)?;
    modpack::create_modpack(&state.http, &token, &slug, &name).await
}

#[tauri::command]
async fn rename_modpack(
    state: State<'_, AppState>,
    slug: String,
    name: String,
) -> Result<(), String> {
    let token = current_token(&state)?;
    modpack::rename_modpack(&state.http, &token, &slug, &name).await
}

#[tauri::command]
async fn delete_modpack(state: State<'_, AppState>, slug: String) -> Result<(), String> {
    let token = current_token(&state)?;
    modpack::delete_modpack(&state.http, &token, &slug).await
}

// Profil actif persisté sur disque (voir active_profile.rs) — chargé au démarrage,
// revalidé côté frontend contre `list_modpacks` avant d'être appliqué (le profil a pu
// être supprimé depuis, ou l'admin n'est peut-être plus admin).
#[tauri::command]
fn load_active_profile(app: AppHandle) -> Option<active_profile::ActiveProfile> {
    active_profile::load(&app)
}

#[tauri::command]
fn save_active_profile(app: AppHandle, slug: String, color: Option<String>) -> Result<(), String> {
    active_profile::save(&app, &active_profile::ActiveProfile { slug, color })
}

#[tauri::command]
async fn set_modpack_color(
    state: State<'_, AppState>,
    slug: String,
    color: Option<String>,
) -> Result<(), String> {
    let token = current_token(&state)?;
    modpack::set_modpack_color(&state.http, &token, &slug, color.as_deref()).await
}

#[tauri::command]
async fn save_bepinex(
    state: State<'_, AppState>,
    slug: String,
    bepinex: BepinexWrite,
) -> Result<(), String> {
    let token = current_token(&state)?;
    modpack::save_bepinex(&state.http, &token, &slug, &bepinex).await
}

// Permet au frontend de résoudre les URLs relatives renvoyées par l'API (ex: images
// d'annonces en "/uploads/xxx.png") sans dupliquer la config d'URL côté JS.
#[tauri::command]
fn api_base_url() -> String {
    config::api_base_url()
}

// Renvoie le chemin absolu du dossier profil (BepInEx + mods, voir
// valheim::profile_dir) pour que le frontend puisse l'ouvrir dans Finder/l'explorateur
// de fichiers Windows (page Paramètres) — via `openPath` de @tauri-apps/plugin-opener,
// pas besoin de dupliquer la logique d'ouverture ici côté Rust.
#[tauri::command]
fn profile_dir_path(app: AppHandle) -> Result<String, String> {
    Ok(valheim::profile_dir(&app)?.to_string_lossy().into_owned())
}

// Ping léger (`GET /health`, public) pour que le frontend sache si l'API est joignable
// avant même toute action utilisateur — sert à afficher un bandeau global et à
// restreindre la navigation (voir App.tsx). Jamais d'erreur renvoyée : une API
// injoignable donne juste `false`, pas un Err qui casserait l'appel `invoke`.
#[tauri::command]
async fn check_api_reachable(state: State<'_, AppState>) -> Result<bool, ()> {
    let reachable = state
        .http
        .get(format!("{}/health", config::api_base_url()))
        .send()
        .await
        .map(|res| res.status().is_success())
        .unwrap_or(false);
    Ok(reachable)
}

#[tauri::command]
async fn fetch_settings(state: State<'_, AppState>) -> Result<Settings, String> {
    settings::fetch_settings(&state.http).await
}

#[tauri::command]
async fn save_settings(
    state: State<'_, AppState>,
    buy_me_a_coffee_url: String,
    hero_eyebrow: String,
    hero_tagline: String,
) -> Result<Settings, String> {
    let token = current_token(&state)?;
    let settings = Settings {
        buy_me_a_coffee_url,
        hero_eyebrow,
        hero_tagline,
    };
    settings::save_settings(&state.http, &token, &settings).await
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_process::init())
        .plugin(tauri_plugin_window_state::Builder::default().build())
        .manage(AppState {
            http: reqwest::Client::new(),
            session: Mutex::new(None),
            pending_login: Mutex::new(None),
        })
        .invoke_handler(tauri::generate_handler![
            restore_session,
            login,
            cancel_login,
            logout,
            refresh_session,
            play,
            has_local_manifest,
            check_update_available,
            sync_modpack,
            repair_modpack,
            send_log_to_discord,
            launch_only,
            fetch_mods,
            fetch_mods_full,
            fetch_bepinex,
            fetch_bepinex_status,
            pick_config_file_and_upload,
            pick_config_files_and_upload,
            fetch_config_file_content,
            save_config_file_text,
            fetch_config_files_full,
            save_config_files,
            delete_uploaded_files,
            save_bepinex,
            list_modpacks,
            create_modpack,
            rename_modpack,
            delete_modpack,
            set_modpack_color,
            fetch_online_players,
            fetch_player_stats,
            fetch_report_token,
            regenerate_report_token,
            load_active_profile,
            save_active_profile,
            fetch_rules,
            fetch_faq,
            save_rules,
            save_faq,
            save_mods,
            accept_rules,
            set_steam_id,
            fetch_announcements,
            post_announcement,
            update_announcement,
            delete_announcement,
            pick_and_upload_image,
            pick_zip_and_upload,
            pick_zips_and_upload,
            api_base_url,
            profile_dir_path,
            check_api_reachable,
            fetch_settings,
            save_settings
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
