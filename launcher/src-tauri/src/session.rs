use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;
use tauri::{AppHandle, Manager};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserInfo {
    pub id: i64,
    #[serde(rename = "discordUsername")]
    pub discord_username: String,
    #[serde(rename = "discordAvatar")]
    pub discord_avatar: Option<String>,
    #[serde(rename = "isAdmin")]
    pub is_admin: bool,
    #[serde(rename = "hasAcceptedRules")]
    pub has_accepted_rules: bool,
    // Brut, peut correspondre à une version dépassée du règlement — voir
    // `serializeUser` côté API. Affiché seulement si `has_accepted_rules` est vrai.
    // `default` : un `session.json` mis en cache par une version antérieure du
    // launcher (avant ce champ) n'aurait pas cette clé — sans quoi la désérialisation
    // entière échouerait et forcerait une reconnexion inutile (voir `load` ci-dessous,
    // qui avale déjà les erreurs de désérialisation).
    #[serde(rename = "rulesAcceptedAt", default)]
    pub rules_accepted_at: Option<String>,
    #[serde(rename = "steamId")]
    pub steam_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Session {
    pub token: String,
    pub user: UserInfo,
}

fn session_file_path(app: &AppHandle) -> Result<PathBuf, String> {
    let dir = app
        .path()
        .app_data_dir()
        .map_err(|e| format!("Could not resolve app data dir: {e}"))?;
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    Ok(dir.join("session.json"))
    // NOTE: stocké en clair sur disque pour ce premier jet. À terme, migrer vers le
    // keychain OS (crate `keyring`) pour un stockage plus sûr du JWT.
}

pub fn load(app: &AppHandle) -> Option<Session> {
    let path = session_file_path(app).ok()?;
    let data = fs::read_to_string(path).ok()?;
    serde_json::from_str(&data).ok()
}

pub fn save(app: &AppHandle, session: &Session) -> Result<(), String> {
    let path = session_file_path(app)?;
    let data = serde_json::to_string_pretty(session).map_err(|e| e.to_string())?;
    fs::write(path, data).map_err(|e| e.to_string())
}

pub fn clear(app: &AppHandle) -> Result<(), String> {
    let path = session_file_path(app)?;
    if path.exists() {
        fs::remove_file(path).map_err(|e| e.to_string())?;
    }
    Ok(())
}
