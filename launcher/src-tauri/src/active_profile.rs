use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;
use tauri::{AppHandle, Manager};

// Profil de modpack actif (voir ProfilesPage/ModsPage) persisté sur disque pour survivre
// à un redémarrage du launcher — un admin qui teste un profil ne doit pas avoir à le
// resélectionner à chaque ouverture. Un joueur normal n'écrit jamais ce fichier (voir
// App.tsx::effectiveModpackSlug, qui retombe toujours sur la production si non admin,
// indépendamment de ce qui est persisté ici) ; App.tsx revalide aussi que ce slug existe
// encore côté API avant de l'appliquer au démarrage (profil supprimé depuis).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ActiveProfile {
    pub slug: String,
    pub color: Option<String>,
}

fn active_profile_file_path(app: &AppHandle) -> Result<PathBuf, String> {
    let dir = app
        .path()
        .app_data_dir()
        .map_err(|e| format!("Could not resolve app data dir: {e}"))?;
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    Ok(dir.join("active-profile.json"))
}

pub fn load(app: &AppHandle) -> Option<ActiveProfile> {
    let path = active_profile_file_path(app).ok()?;
    let data = fs::read_to_string(path).ok()?;
    serde_json::from_str(&data).ok()
}

pub fn save(app: &AppHandle, profile: &ActiveProfile) -> Result<(), String> {
    let path = active_profile_file_path(app)?;
    let data = serde_json::to_string_pretty(profile).map_err(|e| e.to_string())?;
    fs::write(path, data).map_err(|e| e.to_string())
}
