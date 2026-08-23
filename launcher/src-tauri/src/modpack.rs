use crate::config;
use crate::valheim;
use futures_util::StreamExt;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::HashSet;
use std::io::{Cursor, Read};
use std::path::{Path, PathBuf};
use tauri::{AppHandle, Emitter};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ModEntry {
    pub name: String,
    pub version: String,
    #[serde(rename = "downloadUrl")]
    pub download_url: String,
    pub sha256: String,
}

// Package BepInEx (structure officielle BepInExPack_Valheim dézippée) configuré une
// fois par un admin au niveau du modpack, pas par mod — voir CLAUDE.md.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BepinexEntry {
    #[serde(rename = "downloadUrl")]
    pub download_url: String,
    pub sha256: String,
    pub version: String,
}

// Fichier de config brut envoyé par un admin indépendamment de tout mod (ex:
// FastLink.cfg pré-rempli avec l'adresse/mot de passe du serveur) — voir
// `sync_config_files`. `filename` fait autorité pour le nom de destination dans
// BepInEx/config/, pas le nom du fichier tel que stocké côté API.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConfigFileEntry {
    pub filename: String,
    #[serde(rename = "downloadUrl")]
    pub download_url: String,
    pub sha256: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Manifest {
    pub slug: String,
    pub name: String,
    pub version: String,
    pub bepinex: Option<BepinexEntry>,
    pub mods: Vec<ModEntry>,
    // `default` : un manifest local déjà sauvegardé avant l'ajout de cette
    // fonctionnalité (voir `load_local_manifest`) n'en a pas encore.
    #[serde(rename = "configFiles", default)]
    pub config_files: Vec<ConfigFileEntry>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ModInfo {
    pub name: String,
    pub version: String,
    pub description: String,
    pub category: String,
    #[serde(rename = "iconUrl")]
    pub icon_url: String,
}

// Liste publique pour affichage (page "Mods"), pas d'auth requise contrairement au manifest.
pub async fn fetch_mods(http: &reqwest::Client, slug: &str) -> Result<Vec<ModInfo>, String> {
    let res = http
        .get(format!("{}/modpacks/{slug}/mods", config::api_base_url()))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!("Failed to fetch mods ({})", res.status()));
    }

    res.json().await.map_err(|e| e.to_string())
}

// Version complète d'un mod pour l'édition admin (inclut l'archive zip, contrairement à
// ModInfo qui ne sert qu'à l'affichage public).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ModWrite {
    pub name: String,
    pub version: String,
    #[serde(rename = "downloadUrl")]
    pub download_url: String,
    pub sha256: String,
    pub description: String,
    pub category: String,
    // Dépendances Thunderstore ("Auteur-NomDuPackage-Version"), voir
    // `FileUpload::dependencies` — affichage/avertissement admin seulement.
    #[serde(default)]
    pub dependencies: Vec<String>,
    // icon.png de l'archive (voir `FileUpload::icon_url`) — vide si l'archive n'en avait
    // pas. Affichage seulement, jamais utilisé pour l'installation.
    #[serde(rename = "iconUrl", default)]
    pub icon_url: String,
    // Réservé au modpack "Admin" — voir schema.ts::mods.adminOnly côté API.
    #[serde(rename = "adminOnly")]
    pub admin_only: bool,
    // Décoché par un admin pour désactiver ce mod pour tout le monde sans perdre sa
    // fiche — voir schema.ts::mods.enabled côté API.
    pub enabled: bool,
    // Gérés par l'API (voir routes.ts), jamais fixés par le launcher — présents dans la
    // réponse de fetch_mods_full, absents/ignorés pour un mod pas encore enregistré.
    #[serde(rename = "createdAt", default)]
    pub created_at: Option<String>,
    #[serde(rename = "updatedAt", default)]
    pub updated_at: Option<String>,
}

// Réservé aux admins, pour préremplir l'écran d'édition avec l'archive zip (absente de
// fetch_mods, qui ne sert qu'à l'affichage public).
pub async fn fetch_mods_full(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
) -> Result<Vec<ModWrite>, String> {
    let res = http
        .get(format!(
            "{}/modpacks/{slug}/mods/full",
            config::api_base_url()
        ))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to fetch full mods ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

// Réservé aux admins : l'API revérifie le rôle Discord en direct et renvoie 403 sinon.
pub async fn save_mods(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
    mods: Vec<ModWrite>,
) -> Result<(), String> {
    let res = http
        .put(format!("{}/modpacks/{slug}/mods", config::api_base_url()))
        .bearer_auth(token)
        .json(&serde_json::json!({ "mods": mods }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to save mods ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

// Version admin d'un fichier de config (préremplit l'éditeur) — voir CLAUDE.md pour le
// principe général de synchronisation des fichiers de config.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConfigFileWrite {
    pub filename: String,
    #[serde(rename = "downloadUrl")]
    pub download_url: String,
    pub sha256: String,
    #[serde(rename = "updatedAt", default)]
    pub updated_at: Option<String>,
}

pub async fn fetch_config_files(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
) -> Result<Vec<ConfigFileWrite>, String> {
    let res = http
        .get(format!(
            "{}/modpacks/{slug}/config-files",
            config::api_base_url()
        ))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to fetch config files ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

// Réservé aux admins, comme save_mods.
pub async fn save_config_files(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
    files: Vec<ConfigFileWrite>,
) -> Result<(), String> {
    let res = http
        .put(format!(
            "{}/modpacks/{slug}/config-files",
            config::api_base_url()
        ))
        .bearer_auth(token)
        .json(&serde_json::json!({ "files": files }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to save config files ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BepinexWrite {
    pub url: String,
    pub sha256: String,
    pub version: String,
    #[serde(default)]
    pub description: String,
    #[serde(rename = "iconUrl", default)]
    pub icon_url: String,
}

// Statut public de BepInEx (configuré ou non, version/description/icône) — pas d'auth
// requise, BepInEx est un mod comme un autre du point de vue du joueur. Contrairement à
// `fetch_bepinex` ci-dessous (réservé aux admins), pas d'URL/sha256 exposés ici.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BepinexStatus {
    pub configured: bool,
    pub version: Option<String>,
    pub description: Option<String>,
    #[serde(rename = "iconUrl")]
    pub icon_url: Option<String>,
}

pub async fn fetch_bepinex_status(
    http: &reqwest::Client,
    slug: &str,
) -> Result<BepinexStatus, String> {
    let res = http
        .get(format!(
            "{}/modpacks/{slug}/bepinex/status",
            config::api_base_url()
        ))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!("Failed to fetch bepinex status ({})", res.status()));
    }

    res.json().await.map_err(|e| e.to_string())
}

pub async fn fetch_bepinex(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
) -> Result<Option<BepinexWrite>, String> {
    let res = http
        .get(format!(
            "{}/modpacks/{slug}/bepinex",
            config::api_base_url()
        ))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to fetch bepinex config ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

pub async fn save_bepinex(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
    bepinex: &BepinexWrite,
) -> Result<(), String> {
    let res = http
        .put(format!(
            "{}/modpacks/{slug}/bepinex",
            config::api_base_url()
        ))
        .bearer_auth(token)
        .json(bepinex)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to save bepinex config ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

// `mode` : "player" (mods admin-only exclus) ou "admin" (liste complète, revérifiée
// côté API en direct sur Discord — voir routes.ts). Un joueur normal n'envoie jamais
// que "player" (choix non proposé côté UI, voir App.tsx).
pub async fn fetch_manifest(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
    mode: &str,
) -> Result<Manifest, String> {
    let res = http
        .get(format!(
            "{}/modpacks/{slug}/manifest?mode={mode}",
            config::api_base_url()
        ))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to fetch manifest ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

// Profil de modpack (voir schema.ts::modpacks côté API) — `isDefault` marque le
// profil production reçu par tout joueur normal ; les autres sont des profils de
// test créés librement par un admin. Réservé aux admins (voir routes.ts).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ModpackProfile {
    pub slug: String,
    pub name: String,
    pub version: String,
    #[serde(rename = "isDefault")]
    pub is_default: bool,
    // Couleur hex ("#rrggbb") choisie par un admin pour distinguer ce profil dans le
    // launcher — `None` tant qu'aucune n'a été choisie. Toujours `None`/ignorée pour
    // le profil production, voir schema.ts::modpacks.color côté API.
    pub color: Option<String>,
    // Jamais la valeur du jeton lui-même (voir fetch_report_token/regenerate_report_token
    // ci-dessous) — juste de quoi savoir si ce profil en a déjà un configuré.
    #[serde(rename = "hasReportToken")]
    pub has_report_token: bool,
    #[serde(rename = "modCount")]
    pub mod_count: i64,
    #[serde(rename = "updatedAt")]
    pub updated_at: String,
}

pub async fn list_modpacks(
    http: &reqwest::Client,
    token: &str,
) -> Result<Vec<ModpackProfile>, String> {
    let res = http
        .get(format!("{}/modpacks", config::api_base_url()))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to list modpacks ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

pub async fn create_modpack(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
    name: &str,
) -> Result<ModpackProfile, String> {
    let res = http
        .post(format!("{}/modpacks", config::api_base_url()))
        .bearer_auth(token)
        .json(&serde_json::json!({ "slug": slug, "name": name }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to create modpack ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

pub async fn rename_modpack(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
    name: &str,
) -> Result<(), String> {
    let res = http
        .patch(format!("{}/modpacks/{slug}", config::api_base_url()))
        .bearer_auth(token)
        .json(&serde_json::json!({ "name": name }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to rename modpack ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

// `color` à `None` réinitialise (retour à l'apparence par défaut) — voir
// routes.ts::updateModpackSchema, qui distingue explicitement `null` d'absent.
// Jeton partagé donné au mod serveur FedoServerTools (voir /mods/FedoServerTools) pour
// qu'il puisse poster qui est en ligne sur ce profil précis — révélé ici en clair, un
// admin devant le recopier dans le `.cfg` du mod sur le serveur Valheim concerné.
pub async fn fetch_report_token(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
) -> Result<Option<String>, String> {
    #[derive(Deserialize)]
    struct ReportTokenResponse {
        #[serde(rename = "reportToken")]
        report_token: Option<String>,
    }

    let res = http
        .get(format!(
            "{}/modpacks/{slug}/report-token",
            config::api_base_url()
        ))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to fetch report token ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    let parsed: ReportTokenResponse = res.json().await.map_err(|e| e.to_string())?;
    Ok(parsed.report_token)
}

// Génère un nouveau jeton (et invalide l'ancien, s'il existait) — le mod continuera de
// reporter avec l'ancien jusqu'à ce que son `.cfg` soit mis à jour, ses rapports étant
// alors rejetés par l'API entre-temps.
pub async fn regenerate_report_token(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
) -> Result<String, String> {
    #[derive(Deserialize)]
    struct ReportTokenResponse {
        #[serde(rename = "reportToken")]
        report_token: String,
    }

    let res = http
        .post(format!(
            "{}/modpacks/{slug}/report-token/regenerate",
            config::api_base_url()
        ))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to regenerate report token ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    let parsed: ReportTokenResponse = res.json().await.map_err(|e| e.to_string())?;
    Ok(parsed.report_token)
}

pub async fn set_modpack_color(
    http: &reqwest::Client,
    token: &str,
    slug: &str,
    color: Option<&str>,
) -> Result<(), String> {
    let res = http
        .patch(format!("{}/modpacks/{slug}", config::api_base_url()))
        .bearer_auth(token)
        .json(&serde_json::json!({ "color": color }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to set modpack color ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

// Refusé côté API pour le profil production (isDefault) — voir routes.ts.
pub async fn delete_modpack(http: &reqwest::Client, token: &str, slug: &str) -> Result<(), String> {
    let res = http
        .delete(format!("{}/modpacks/{slug}", config::api_base_url()))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to delete modpack ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

// `POST /modpacks/files` (voir api/src/modpacks/files.ts) renvoie une URL relative
// ("/uploads/xxx.zip"), pas absolue — même schéma que les images d'annonces. Un
// admin peut aussi avoir saisi une URL externe absolue (hébergement tiers). Résout le
// premier cas contre l'URL d'API du launcher, laisse le second tel quel.
fn resolve_url(url: &str) -> String {
    if url.starts_with("http://") || url.starts_with("https://") {
        url.to_string()
    } else {
        format!("{}{}", config::api_base_url(), url)
    }
}

// Dérive un nom de dossier stable et sûr depuis le nom d'un mod (ex: "Epic Loot" ->
// "epic-loot") — utilisé à la fois pour le dossier d'extraction et le nettoyage des
// mods retirés du modpack.
fn slugify(name: &str) -> String {
    let mut slug = String::new();
    let mut last_was_dash = false;
    for c in name.to_lowercase().chars() {
        if c.is_ascii_alphanumeric() {
            slug.push(c);
            last_was_dash = false;
        } else if !last_was_dash && !slug.is_empty() {
            slug.push('-');
            last_was_dash = true;
        }
    }
    while slug.ends_with('-') {
        slug.pop();
    }
    if slug.is_empty() {
        "mod".to_string()
    } else {
        slug
    }
}

// Les archives Thunderstore (BepInExPack_Valheim en particulier) enveloppent le vrai
// contenu dans un sous-dossier, à côté de fichiers de métadonnées (manifest.json,
// README.md, icon.png...) qui ne doivent pas atterrir dans l'install du jeu. Cherche
// le préfixe (chemin du dossier englobant) de la première entrée dont un composant de
// chemin correspond exactement à `marker` — vide si le marqueur est déjà à la racine
// de l'archive ou totalement absent. Le composant `marker` lui-même reste dans le
// chemin extrait (on veut `<dest>/BepInEx/...`, pas son contenu remonté d'un niveau).
fn find_zip_root(archive: &mut zip::ZipArchive<Cursor<&[u8]>>, marker: &str) -> PathBuf {
    for i in 0..archive.len() {
        let Ok(file) = archive.by_index(i) else {
            continue;
        };
        let Some(name) = file.enclosed_name() else {
            continue;
        };

        let mut prefix = PathBuf::new();
        for component in name.components() {
            if let std::path::Component::Normal(part) = component {
                if part == marker {
                    return prefix;
                }
                prefix.push(part);
            }
        }
    }
    PathBuf::new()
}

// Extrait une archive zip déjà vérifiée (sha256) dans `dest_dir`. Écrase
// systématiquement les fichiers déjà présents (dll comme cfg — la config livrée par
// l'admin fait autorité, voir CLAUDE.md). Protection zip-slip via `enclosed_name()` :
// toute entrée qui tenterait de sortir de `dest_dir` (`../..`, chemin absolu) est
// simplement ignorée plutôt qu'extraite.
//
// `root_marker` : si fourni (ex: `Some("BepInEx")`), l'archive est d'abord "re-rootée"
// sur le dossier qui contient ce marqueur (voir `find_zip_root`) — tout ce qui est en
// dehors (métadonnées Thunderstore au vrai niveau racine) est ignoré plutôt qu'extrait.
fn extract_zip(bytes: &[u8], dest_dir: &Path, root_marker: Option<&str>) -> Result<(), String> {
    std::fs::create_dir_all(dest_dir).map_err(|e| e.to_string())?;
    let mut archive =
        zip::ZipArchive::new(Cursor::new(bytes)).map_err(|e| format!("Invalid archive: {e}"))?;

    let root_prefix = match &root_marker {
        Some(marker) => find_zip_root(&mut archive, marker),
        None => PathBuf::new(),
    };

    for i in 0..archive.len() {
        let mut file = archive.by_index(i).map_err(|e| e.to_string())?;
        let Some(full_path) = file.enclosed_name() else {
            continue;
        };
        let Ok(relative) = full_path.strip_prefix(&root_prefix) else {
            continue;
        };
        if relative.as_os_str().is_empty() {
            continue;
        }
        let dest_path = dest_dir.join(relative);

        if file.is_dir() {
            std::fs::create_dir_all(&dest_path).map_err(|e| e.to_string())?;
            continue;
        }

        if let Some(parent) = dest_path.parent() {
            std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        }

        let mut out = std::fs::File::create(&dest_path).map_err(|e| e.to_string())?;
        std::io::copy(&mut file, &mut out).map_err(|e| e.to_string())?;
    }

    Ok(())
}

// Fichiers de métadonnées Thunderstore à la racine d'un package — jamais installés
// chez le joueur, quel que soit le dossier `BepInEx/*` visé (voir `extract_mod_zip`) :
// ils n'ont aucune utilité pour BepInEx/le jeu, seulement pour l'admin (déjà lus à
// part au moment de l'upload, voir `read_manifest_info`/`find_icon_bytes`).
fn is_thunderstore_metadata(name: &str) -> bool {
    matches!(
        name.to_ascii_lowercase().as_str(),
        "manifest.json" | "icon.png" | "readme.md" | "changelog.md" | "changelog.txt"
    )
}

// Dossier `BepInEx/*` de destination pour un dossier top-level `plugins`/`config`/
// `patchers`/`monomod` trouvé dans le zip d'un mod (convention Thunderstore reprise
// par r2modman/Gale — voir la doc citée dans la conversation qui a motivé ce
// correctif). `plugins`/`patchers`/`monomod` sont namespacés par mod (sous-dossier
// `slug`, comme le launcher le fait déjà pour `plugins` par défaut) pour éviter les
// collisions entre mods ; `config` est partagé entre tous les mods, sans sous-dossier
// — c'est la seule exception documentée. `None` si `top_level` n'est aucun de ces
// noms (le fichier suit alors le comportement par défaut, voir `extract_mod_zip`).
fn mod_override_dir(profile_dir: &Path, slug: &str, top_level: &str) -> Option<PathBuf> {
    match top_level.to_ascii_lowercase().as_str() {
        "plugins" => Some(valheim::bepinex_plugins_dir(profile_dir).join(slug)),
        "patchers" => Some(valheim::bepinex_patchers_dir(profile_dir).join(slug)),
        "monomod" => Some(valheim::bepinex_monomod_dir(profile_dir).join(slug)),
        "config" => Some(valheim::bepinex_config_dir(profile_dir)),
        _ => None,
    }
}

// Extrait le zip d'un mod en respectant les dossiers "override" Thunderstore
// (`plugins`/`config`/`patchers`/`monomod`, voir `mod_override_dir`) ainsi que les
// fichiers `*.mm.dll` (toujours routés vers `monomod/<slug>/`, quel que soit leur
// dossier d'origine dans l'archive — spécial-cas par extension propre à MonoMod,
// indépendant du nom de dossier) — même convention que r2modman/Gale. Un mod qui ne
// suit aucune de ces conventions (dll à plat à la racine de l'archive, cas le plus
// courant) est extrait tel quel dans `plugins/<slug>/`, comportement inchangé par
// rapport à avant ce correctif.
fn extract_mod_zip(bytes: &[u8], profile_dir: &Path, slug: &str) -> Result<(), String> {
    let mut archive =
        zip::ZipArchive::new(Cursor::new(bytes)).map_err(|e| format!("Invalid archive: {e}"))?;

    let default_dir = valheim::bepinex_plugins_dir(profile_dir).join(slug);
    let monomod_dir = valheim::bepinex_monomod_dir(profile_dir).join(slug);

    for i in 0..archive.len() {
        let mut file = archive.by_index(i).map_err(|e| e.to_string())?;
        if file.is_dir() {
            continue;
        }
        let Some(relative) = file.enclosed_name() else {
            continue;
        };

        let components: Vec<&str> = relative
            .components()
            .filter_map(|c| match c {
                std::path::Component::Normal(part) => part.to_str(),
                _ => None,
            })
            .collect();
        let Some((&top_level, rest)) = components.split_first() else {
            continue;
        };

        let Some(file_name) = relative.file_name().and_then(|n| n.to_str()) else {
            continue;
        };
        let is_monomod_dll = file_name.to_ascii_lowercase().ends_with(".mm.dll");

        let dest_path = if is_monomod_dll {
            monomod_dir.join(file_name)
        } else if rest.is_empty() && is_thunderstore_metadata(top_level) {
            continue;
        } else if let Some(override_dir) = mod_override_dir(profile_dir, slug, top_level) {
            let mut dest = override_dir;
            dest.extend(rest.iter().copied());
            dest
        } else {
            default_dir.join(&relative)
        };

        if let Some(parent) = dest_path.parent() {
            std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        }

        let mut out = std::fs::File::create(&dest_path).map_err(|e| e.to_string())?;
        std::io::copy(&mut file, &mut out).map_err(|e| e.to_string())?;
    }

    Ok(())
}

// Télécharge une archive entière en mémoire (nécessaire pour l'ouvrir en zip, qui a
// besoin de `Seek`) et vérifie son sha256 avant qu'appelant n'en fasse quoi que ce
// soit — rien n'est jamais écrit sur disque avant cette vérification.
async fn download_verified(
    http: &reqwest::Client,
    url: &str,
    sha256: &str,
    label: &str,
) -> Result<Vec<u8>, String> {
    let res = http
        .get(resolve_url(url))
        .send()
        .await
        .map_err(|e| format!("Failed to download {label}: {e}"))?;

    if !res.status().is_success() {
        return Err(format!("Failed to download {label} ({})", res.status()));
    }

    let mut bytes = Vec::new();
    let mut hasher = Sha256::new();
    let mut stream = res.bytes_stream();
    while let Some(chunk) = stream.next().await {
        let chunk = chunk.map_err(|e| format!("Download error for {label}: {e}"))?;
        hasher.update(&chunk);
        bytes.extend_from_slice(&chunk);
    }

    let actual_hash = hex::encode(hasher.finalize());
    if !actual_hash.eq_ignore_ascii_case(sha256) {
        return Err(format!(
            "Checksum mismatch for {label}: expected {sha256}, got {actual_hash}"
        ));
    }

    Ok(bytes)
}

// Si `marker_path` contient déjà `sha256`, rien à faire. Sinon télécharge+vérifie
// (voir `download_verified`), extrait dans `dest_dir`, puis met à jour le marker.
// Retourne `true` si quelque chose a été (ré)installé. Utilisé pour BepInEx lui-même
// (destination unique) — voir `download_and_extract_mod` pour les mods, qui peuvent
// se répartir sur plusieurs dossiers `BepInEx/*`.
async fn download_and_extract(
    http: &reqwest::Client,
    url: &str,
    sha256: &str,
    dest_dir: &Path,
    marker_path: &Path,
    label: &str,
    root_marker: Option<&str>,
) -> Result<bool, String> {
    if let Ok(existing) = std::fs::read_to_string(marker_path) {
        if existing.trim().eq_ignore_ascii_case(sha256) {
            return Ok(false);
        }
    }

    let bytes = download_verified(http, url, sha256, label).await?;
    extract_zip(&bytes, dest_dir, root_marker)?;
    std::fs::write(marker_path, sha256).map_err(|e| e.to_string())?;
    Ok(true)
}

// Équivalent de `download_and_extract` pour un mod (voir `extract_mod_zip`) : le
// contenu peut se répartir sur plusieurs dossiers `BepInEx/*` selon sa structure
// interne, donc pas de `dest_dir` unique — seulement `profile_dir` + le slug du mod.
async fn download_and_extract_mod(
    http: &reqwest::Client,
    url: &str,
    sha256: &str,
    profile_dir: &Path,
    slug: &str,
    marker_path: &Path,
    label: &str,
) -> Result<bool, String> {
    if let Ok(existing) = std::fs::read_to_string(marker_path) {
        if existing.trim().eq_ignore_ascii_case(sha256) {
            return Ok(false);
        }
    }

    let bytes = download_verified(http, url, sha256, label).await?;
    extract_mod_zip(&bytes, profile_dir, slug)?;
    if let Some(parent) = marker_path.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }
    std::fs::write(marker_path, sha256).map_err(|e| e.to_string())?;
    Ok(true)
}

#[derive(Debug, Clone, Serialize)]
pub struct SyncProgress {
    pub phase: String,
    pub label: String,
    pub current: u32,
    pub total: u32,
}

pub(crate) fn emit_progress(app: &AppHandle, phase: &str, label: &str, current: u32, total: u32) {
    let _ = app.emit(
        "sync-progress",
        SyncProgress {
            phase: phase.to_string(),
            label: label.to_string(),
            current,
            total,
        },
    );
}

// Compare le dernier manifest synchronisé avec succès à celui que l'API sert
// actuellement — sert à savoir si le bouton "Jouer" doit se scinder en "Mettre à jour" +
// "Jouer" (voir App.tsx) plutôt que de resynchroniser silencieusement à chaque clic.
// Comparaison par sha256 (BepInEx + chaque mod, par nom, indépendante de l'ordre) : un
// changement de nom sans changement de fichier n'a aucun impact sur ce qui est
// physiquement installé, donc ne compte pas comme une mise à jour disponible.
pub fn manifest_needs_update(local: &Manifest, remote: &Manifest) -> bool {
    let local_bepinex = local.bepinex.as_ref().map(|b| b.sha256.as_str());
    let remote_bepinex = remote.bepinex.as_ref().map(|b| b.sha256.as_str());
    if local_bepinex != remote_bepinex {
        return true;
    }

    let mut local_mods: Vec<(&str, &str)> = local
        .mods
        .iter()
        .map(|m| (m.name.as_str(), m.sha256.as_str()))
        .collect();
    let mut remote_mods: Vec<(&str, &str)> = remote
        .mods
        .iter()
        .map(|m| (m.name.as_str(), m.sha256.as_str()))
        .collect();
    local_mods.sort_unstable();
    remote_mods.sort_unstable();
    if local_mods != remote_mods {
        return true;
    }

    // Même comparaison pour les fichiers de config (voir `sync_config_files`) : un
    // nouveau fichier, un retrait, ou un sha256 changé (ex: admin qui met à jour le mot
    // de passe serveur dans FastLink.cfg) doit aussi proposer "Mettre à jour" plutôt que
    // de laisser "Jouer" lancer avec l'ancienne version.
    let mut local_config_files: Vec<(&str, &str)> = local
        .config_files
        .iter()
        .map(|f| (f.filename.as_str(), f.sha256.as_str()))
        .collect();
    let mut remote_config_files: Vec<(&str, &str)> = remote
        .config_files
        .iter()
        .map(|f| (f.filename.as_str(), f.sha256.as_str()))
        .collect();
    local_config_files.sort_unstable();
    remote_config_files.sort_unstable();
    local_config_files != remote_config_files
}

fn local_manifest_path(profile_dir: &Path) -> PathBuf {
    profile_dir.join(".fedoheim-manifest.json")
}

fn bepinex_marker_path(profile_dir: &Path) -> PathBuf {
    profile_dir.join(".fedoheim-bepinex-sha256")
}

// Copie locale du dernier manifest effectivement synchronisé avec succès (BepInEx +
// tous les mods installés sans erreur) — écrite après coup, jamais avant, pour ne
// jamais refléter un état partiel. Best-effort : un échec d'écriture ne doit pas faire
// échouer le lancement du jeu.
pub fn save_local_manifest(profile_dir: &Path, manifest: &Manifest) {
    if let Ok(json) = serde_json::to_string(manifest) {
        let _ = std::fs::write(local_manifest_path(profile_dir), json);
    }
}

// Relit ce dernier manifest connu — sert à savoir précisément ce qui est installé
// localement (pas juste "un dossier BepInEx existe") quand l'API est injoignable (voir
// `play` dans lib.rs). `None` si jamais synchronisé avec succès, ou fichier
// manquant/corrompu : dans ce cas il n'y a rien de fiable sur quoi se rabattre.
pub fn load_local_manifest(profile_dir: &Path) -> Option<Manifest> {
    let contents = std::fs::read_to_string(local_manifest_path(profile_dir)).ok()?;
    serde_json::from_str(&contents).ok()
}

// `std::fs::remove_dir_all` sur un gros dossier (des centaines de fichiers pour un mod
// comme More_World_Locations_AIO) peut échouer avec "Directory not empty" (ENOTEMPTY) —
// une race connue sur macOS où Spotlight/le Finder retouchent le dossier pendant qu'on
// le vide (liste les entrées, puis `rmdir` échoue si une réapparaît entre-temps).
// Transitoire dans l'immense majorité des cas : quelques tentatives avec un court délai
// suffisent.
fn remove_dir_all_retrying(path: &Path) -> std::io::Result<()> {
    let mut last_err: Option<std::io::Error> = None;
    for attempt in 0..5u32 {
        match std::fs::remove_dir_all(path) {
            Ok(()) => return Ok(()),
            Err(_) if !path.exists() => return Ok(()),
            Err(e) => {
                last_err = Some(e);
                std::thread::sleep(std::time::Duration::from_millis(
                    150 * u64::from(attempt + 1),
                ));
            }
        }
    }
    Err(last_err.unwrap())
}

// Efface toute trace d'une installation locale (dossier `BepInEx` entier — plugins,
// patchers, monomod, config, core —, marker sha256 de BepInEx, manifest local) pour
// repartir d'une resynchronisation complète depuis zéro. Utilisé par l'action
// "Réparer" (voir `repair_modpack` dans lib.rs), avant de rappeler la même séquence
// `ensure_bepinex`/`sync_mods`/`save_local_manifest` que `sync_modpack`. Chaque
// suppression est best-effort : un fichier/dossier déjà absent n'est pas une erreur,
// seul un échec de suppression d'un dossier *présent* (ex: fichier verrouillé) l'est —
// sinon "Réparer" laisserait croire à une resynchronisation propre alors que l'ancienne
// installation partiellement corrompue est toujours là.
pub fn wipe_local_install(profile_dir: &Path) -> Result<(), String> {
    let bepinex_dir = valheim::bepinex_dir(profile_dir);
    if bepinex_dir.exists() {
        remove_dir_all_retrying(&bepinex_dir).map_err(|e| e.to_string())?;
    }
    let _ = std::fs::remove_file(bepinex_marker_path(profile_dir));
    let _ = std::fs::remove_file(local_manifest_path(profile_dir));
    Ok(())
}

// Installe/met à jour le package BepInEx dans le dossier profil (voir
// `valheim::profile_dir`), à faire avant toute synchronisation de mod. `total_steps` =
// BepInEx (cette étape) + le nombre de mods à suivre — permet au frontend d'afficher une
// barre de progression unique sur l'ensemble de l'opération (voir `sync_mods` pour la
// suite de la numérotation) plutôt qu'une progression qui repartirait de zéro à chaque
// phase.
pub async fn ensure_bepinex(
    http: &reqwest::Client,
    app: &AppHandle,
    profile_dir: &Path,
    bepinex: &BepinexEntry,
    total_steps: u32,
) -> Result<bool, String> {
    emit_progress(app, "bepinex", "BepInEx", 1, total_steps);
    let marker = bepinex_marker_path(profile_dir);
    download_and_extract(
        http,
        &bepinex.download_url,
        &bepinex.sha256,
        profile_dir,
        &marker,
        "BepInEx",
        Some("BepInEx"),
    )
    .await
}

// Compare le manifest à l'état local du profil, télécharge/écrit les mods manquants ou
// obsolètes (répartis sur `BepInEx/plugins|patchers|monomod|config` selon la structure
// du zip, voir `extract_mod_zip`), puis nettoie les dossiers de mods retirés du
// modpack (best-effort, jamais bloquant). Retourne le nombre de mods effectivement
// (ré)installés. `total_steps` : voir `ensure_bepinex` — la numérotation continue ici à
// partir de l'étape 2 (l'étape 1, BepInEx, est déjà passée avant cet appel).
pub async fn sync_mods(
    http: &reqwest::Client,
    app: &AppHandle,
    profile_dir: &Path,
    mods: &[ModEntry],
    total_steps: u32,
) -> Result<u32, String> {
    let plugins_dir = valheim::bepinex_plugins_dir(profile_dir);
    std::fs::create_dir_all(&plugins_dir).map_err(|e| e.to_string())?;

    let mut updated = 0;
    let mut expected_slugs = HashSet::new();

    for (i, entry) in mods.iter().enumerate() {
        let slug = slugify(&entry.name);
        expected_slugs.insert(slug.clone());

        emit_progress(app, "mod", &entry.name, i as u32 + 2, total_steps);

        // Le marker sha256 vit toujours dans `plugins/<slug>/`, même si le contenu
        // réel du mod atterrit ailleurs (`patchers/`/`monomod`/`config` uniquement) —
        // ça garantit à chaque mod un dossier stable sous `plugins_dir` détecté par le
        // nettoyage ci-dessous, sans dépendre d'où son contenu a réellement été extrait.
        let marker = plugins_dir.join(&slug).join(".fedoheim-sha256");
        let changed = download_and_extract_mod(
            http,
            &entry.download_url,
            &entry.sha256,
            profile_dir,
            &slug,
            &marker,
            &entry.name,
        )
        .await?;
        if changed {
            updated += 1;
        }
    }

    let cleanup_dirs = [
        plugins_dir.clone(),
        valheim::bepinex_patchers_dir(profile_dir),
        valheim::bepinex_monomod_dir(profile_dir),
    ];
    // `config/` n'est volontairement pas nettoyé : ses fichiers ne sont pas namespacés
    // par mod (voir `mod_override_dir`), impossible de savoir sans risque lesquels
    // appartiennent à un mod retiré sans supprimer aussi ceux d'un autre mod ou une
    // config éditée par un joueur.
    for dir in cleanup_dirs {
        let Ok(entries) = std::fs::read_dir(&dir) else {
            continue;
        };
        for entry in entries.flatten() {
            let name = entry.file_name().to_string_lossy().into_owned();
            if entry.path().is_dir() && !expected_slugs.contains(&name) {
                let _ = std::fs::remove_dir_all(entry.path());
            }
        }
    }

    Ok(updated)
}

// Fichiers de config bruts (pas un zip de mod), envoyés par un admin indépendamment de
// tout mod — ex: FastLink.cfg pré-rempli avec l'adresse/mdp du serveur, pour que ce soit
// déjà en place au tout premier lancement du joueur (BepInEx charge alors ce fichier
// existant plutôt que de générer ses valeurs par défaut). Copiés tels quels dans
// BepInEx/config/ (partagé entre tous les mods, jamais namespacés — voir
// `bepinex_config_dir`), en écrasant systématiquement (admin authoritative, même
// principe que `extract_zip`). Marker sha256 par fichier (à côté du fichier lui-même)
// pour ne retélécharger/réécrire que ce qui a changé.
pub async fn sync_config_files(
    http: &reqwest::Client,
    profile_dir: &Path,
    files: &[ConfigFileEntry],
) -> Result<u32, String> {
    let config_dir = valheim::bepinex_config_dir(profile_dir);
    std::fs::create_dir_all(&config_dir).map_err(|e| e.to_string())?;

    let mut updated = 0;
    for entry in files {
        // `file_name()` ignore tout séparateur de chemin dans `filename` — défense en
        // profondeur en plus de la validation déjà faite côté API, même principe que la
        // protection zip-slip d'`extract_zip`.
        let Some(safe_name) = Path::new(&entry.filename).file_name() else {
            continue;
        };
        let dest_path = config_dir.join(safe_name);
        let marker_path =
            config_dir.join(format!(".{}.fedoheim-sha256", safe_name.to_string_lossy()));

        if let Ok(existing) = std::fs::read_to_string(&marker_path) {
            if existing.trim().eq_ignore_ascii_case(&entry.sha256) {
                continue;
            }
        }

        let bytes =
            download_verified(http, &entry.download_url, &entry.sha256, &entry.filename).await?;
        std::fs::write(&dest_path, &bytes).map_err(|e| e.to_string())?;
        std::fs::write(&marker_path, &entry.sha256).map_err(|e| e.to_string())?;
        updated += 1;
    }

    Ok(updated)
}

#[derive(Debug, Clone, Serialize)]
pub struct FileUpload {
    pub url: String,
    pub sha256: String,
    // Les champs suivants viennent du manifest.json Thunderstore éventuellement présent
    // dans l'archive (voir `read_manifest_info`) — absents/vides pour un zip qui n'en a
    // pas (mod maison sans packaging Thunderstore).
    pub version: Option<String>,
    pub name: Option<String>,
    pub description: Option<String>,
    // Identifiants "Auteur-NomDuPackage-Version" des packages requis (autres mods,
    // BepInExPack_Valheim...) — affichage/avertissement admin seulement, jamais utilisé
    // pour l'installation côté joueur.
    pub dependencies: Vec<String>,
    // icon.png de l'archive, uploadé séparément à `POST /modpacks/icons` — `None` si
    // l'archive n'en avait pas.
    #[serde(rename = "iconUrl")]
    pub icon_url: Option<String>,
}

// Résultat d'un envoi en masse (plusieurs zips choisis d'un coup, voir
// `pick_zips_and_upload`) — un échec sur une archive (réseau, zip corrompu...) n'annule
// pas les autres, il est juste remonté dans `errors` plutôt que de faire échouer tout
// l'appel.
#[derive(Debug, Clone, Serialize)]
pub struct BulkUpload {
    pub uploads: Vec<FileUpload>,
    pub errors: Vec<String>,
}

#[derive(Deserialize, Default)]
struct ThunderstoreManifest {
    name: Option<String>,
    version_number: Option<String>,
    description: Option<String>,
    #[serde(default)]
    dependencies: Vec<String>,
}

// Cherche un fichier `manifest.json` n'importe où dans l'archive (peut être niché sous
// un dossier d'enveloppe Thunderstore, voir `find_zip_root`) et en extrait nom/version/
// description. Champs à `None` si le manifest est absent, illisible, ou ne les
// renseigne pas — pas une erreur, juste une archive sans (toutes les) métadonnées
// Thunderstore.
fn read_manifest_info(bytes: &[u8]) -> ThunderstoreManifest {
    let Ok(mut archive) = zip::ZipArchive::new(Cursor::new(bytes)) else {
        return ThunderstoreManifest::default();
    };
    for i in 0..archive.len() {
        let Ok(mut file) = archive.by_index(i) else {
            continue;
        };
        let is_manifest = file
            .enclosed_name()
            .and_then(|p| p.file_name().map(|n| n.to_str() == Some("manifest.json")))
            .unwrap_or(false);
        if !is_manifest {
            continue;
        }

        let mut contents = String::new();
        if Read::read_to_string(&mut file, &mut contents).is_err() {
            continue;
        }
        // Certains manifest.json (générés via PowerShell `ConvertTo-Json | Out-File`,
        // courant chez des devs Windows) commencent par un BOM UTF-8 — invisible à
        // l'oeil mais que serde_json refuse de parser (le JSON valide ne le tolère pas),
        // faisant échouer l'extraction en silence sans que ce soit une vraie erreur de
        // format.
        let contents = contents.strip_prefix('\u{feff}').unwrap_or(&contents);
        if let Ok(manifest) = serde_json::from_str::<ThunderstoreManifest>(contents) {
            return manifest;
        }
    }
    ThunderstoreManifest::default()
}

// Cherche un fichier `icon.png` n'importe où dans l'archive (insensible à la casse,
// même logique de recherche que `read_manifest_info`) et renvoie ses octets bruts.
// `None` si absent — un mod maison n'en a pas forcément.
fn find_icon_bytes(bytes: &[u8]) -> Option<Vec<u8>> {
    let mut archive = zip::ZipArchive::new(Cursor::new(bytes)).ok()?;
    for i in 0..archive.len() {
        let Ok(mut file) = archive.by_index(i) else {
            continue;
        };
        let is_icon = file
            .enclosed_name()
            .and_then(|p| {
                p.file_name()
                    .and_then(|n| n.to_str())
                    .map(str::to_lowercase)
            })
            .is_some_and(|n| n == "icon.png");
        if !is_icon {
            continue;
        }

        let mut data = Vec::new();
        if Read::read_to_end(&mut file, &mut data).is_err() {
            continue;
        }
        return Some(data);
    }
    None
}

// Upload l'icône (icon.png) extraite d'une archive à `POST /modpacks/icons`. Distinct
// de l'upload du zip lui-même (voir `upload_zip`) — endpoint séparé, pas de sha256 à
// vérifier pour une image purement cosmétique.
async fn upload_icon(
    http: &reqwest::Client,
    token: &str,
    bytes: Vec<u8>,
) -> Result<String, String> {
    let part = reqwest::multipart::Part::bytes(bytes)
        .file_name("icon.png")
        .mime_str("image/png")
        .map_err(|e| e.to_string())?;
    let form = reqwest::multipart::Form::new().part("file", part);

    let res = http
        .post(format!("{}/modpacks/icons", config::api_base_url()))
        .bearer_auth(token)
        .multipart(form)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to upload icon ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    #[derive(Deserialize)]
    struct IconUploadResponse {
        url: String,
    }
    let parsed: IconUploadResponse = res.json().await.map_err(|e| e.to_string())?;
    Ok(parsed.url)
}

// Upload une archive zip déjà prête (choisie par l'admin) à l'API — sert aussi bien à
// un mod qu'au package BepInEx, même mécanique.
pub async fn upload_zip(
    http: &reqwest::Client,
    token: &str,
    path: PathBuf,
) -> Result<FileUpload, String> {
    let bytes = tokio::fs::read(&path).await.map_err(|e| e.to_string())?;
    let info = read_manifest_info(&bytes);
    let icon_bytes = find_icon_bytes(&bytes);

    let part = reqwest::multipart::Part::bytes(bytes)
        .file_name("archive.zip")
        .mime_str("application/zip")
        .map_err(|e| e.to_string())?;
    let form = reqwest::multipart::Form::new().part("file", part);

    let res = http
        .post(format!("{}/modpacks/files", config::api_base_url()))
        .bearer_auth(token)
        .multipart(form)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to upload archive ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    #[derive(Deserialize)]
    struct UploadResponse {
        url: String,
        sha256: String,
    }
    let parsed: UploadResponse = res.json().await.map_err(|e| e.to_string())?;

    // Best-effort : une icône est purement cosmétique, son échec ne doit pas faire
    // échouer l'upload du mod/de BepInEx lui-même.
    let icon_url = match icon_bytes {
        Some(data) => upload_icon(http, token, data).await.ok(),
        None => None,
    };

    Ok(FileUpload {
        url: parsed.url,
        sha256: parsed.sha256,
        version: info.version_number,
        name: info.name,
        dependencies: info.dependencies,
        description: info.description,
        icon_url,
    })
}

// Envoie le LogOutput.log du profil courant au salon Discord de support configuré côté
// API (voir `support/discord.ts`) — bouton "Envoyer log" à côté de "Réparer".
// Contrairement à `upload_zip`, ne stocke rien côté API : le fichier part directement
// vers Discord, pas de sha256/URL à récupérer en retour.
pub async fn send_log(http: &reqwest::Client, token: &str, log_path: &Path) -> Result<(), String> {
    let bytes = tokio::fs::read(log_path)
        .await
        .map_err(|e| format!("Impossible de lire le fichier de log : {e}"))?;

    let part = reqwest::multipart::Part::bytes(bytes)
        .file_name("LogOutput.log")
        .mime_str("text/plain")
        .map_err(|e| e.to_string())?;
    let form = reqwest::multipart::Form::new().part("file", part);

    let res = http
        .post(format!("{}/support/logs", config::api_base_url()))
        .bearer_auth(token)
        .multipart(form)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Échec de l'envoi du log ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

// Résultat de l'upload d'un fichier de config brut (voir `upload_config_file`) —
// `filename` est le nom d'origine du fichier choisi, à distinguer de `url` qui pointe
// vers son nom de stockage côté API (renommé pour éviter les collisions).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConfigFileUpload {
    pub url: String,
    pub sha256: String,
    pub filename: String,
}

// Résultat de l'envoi en masse de plusieurs fichiers de config d'un coup (voir
// `pick_config_files_and_upload`) — même principe que `BulkUpload` pour les zips de
// mods : chaque fichier est uploadé l'un après l'autre, un échec sur l'un n'annule pas
// les autres, juste remonté dans `errors`.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConfigFileBulkUpload {
    pub uploads: Vec<ConfigFileUpload>,
    pub errors: Vec<String>,
}

// Partagé entre `upload_config_file` (fichier choisi sur disque) et
// `upload_config_file_text` (contenu édité directement dans le launcher, voir
// `fetch_config_file_content`) — seule la provenance des octets diffère.
async fn post_config_file_bytes(
    http: &reqwest::Client,
    token: &str,
    filename: &str,
    bytes: Vec<u8>,
) -> Result<ConfigFileUpload, String> {
    let part = reqwest::multipart::Part::bytes(bytes)
        .file_name(filename.to_string())
        .mime_str("application/octet-stream")
        .map_err(|e| e.to_string())?;
    let form = reqwest::multipart::Form::new().part("file", part);

    let res = http
        .post(format!("{}/modpacks/config-files", config::api_base_url()))
        .bearer_auth(token)
        .multipart(form)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to upload config file ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

// Upload un fichier de config brut choisi par l'admin (pas un zip, contrairement à
// `upload_zip`) — ex: FastLink.cfg pré-rempli avec l'adresse/mdp du serveur.
pub async fn upload_config_file(
    http: &reqwest::Client,
    token: &str,
    path: PathBuf,
) -> Result<ConfigFileUpload, String> {
    let bytes = tokio::fs::read(&path).await.map_err(|e| e.to_string())?;
    let original_name = path
        .file_name()
        .map(|n| n.to_string_lossy().into_owned())
        .unwrap_or_else(|| "config.cfg".to_string());

    post_config_file_bytes(http, token, &original_name, bytes).await
}

// Enregistre un contenu édité directement dans le launcher (voir ModsPage "Éditer") —
// même endpoint que `upload_config_file`, mais à partir de texte déjà en mémoire plutôt
// que d'un fichier choisi sur disque. `filename` sert seulement de nom pour le multipart
// (le nom de destination réel dans BepInEx/config/ reste celui déjà choisi dans le
// brouillon, jamais celui renvoyé ici — voir handleSaveConfigFileContent côté frontend).
pub async fn upload_config_file_text(
    http: &reqwest::Client,
    token: &str,
    filename: &str,
    content: String,
) -> Result<ConfigFileUpload, String> {
    post_config_file_bytes(http, token, filename, content.into_bytes()).await
}

// Récupère le contenu texte d'un fichier déjà uploadé (zip de mod exclu, ceux-ci ne sont
// jamais affichés) — pour préremplir la zone d'édition inline d'un fichier de config
// (voir ModsPage "Éditer"). Public (pas de bearer_auth) comme tout /uploads/*, cohérent
// avec la façon dont les icônes sont déjà chargées directement par le frontend.
pub async fn fetch_config_file_content(
    http: &reqwest::Client,
    url: &str,
) -> Result<String, String> {
    let res = http
        .get(resolve_url(url))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to fetch config file content ({})",
            res.status()
        ));
    }

    res.text().await.map_err(|e| e.to_string())
}

// Supprime des fichiers uploadés qui ne seront finalement pas utilisés (ex: admin
// annule l'édition après avoir importé un zip/une icône) — voir DELETE /modpacks/files.
// No-op si `urls` est vide, pas besoin d'un aller-retour réseau pour rien.
pub async fn delete_files(
    http: &reqwest::Client,
    token: &str,
    urls: &[String],
) -> Result<(), String> {
    if urls.is_empty() {
        return Ok(());
    }

    let res = http
        .delete(format!("{}/modpacks/files", config::api_base_url()))
        .bearer_auth(token)
        .json(&serde_json::json!({ "urls": urls }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to delete files ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    fn unique_dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("fedoheim-test-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn extraction_overwrites_existing_files() {
        let dest = unique_dir("overwrite-dest");
        std::fs::write(dest.join("config.cfg"), "old").unwrap();

        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default();
        writer.start_file("config.cfg", options).unwrap();
        writer.write_all(b"new").unwrap();
        let bytes = writer.finish().unwrap().into_inner();

        extract_zip(&bytes, &dest, None).unwrap();

        let content = std::fs::read_to_string(dest.join("config.cfg")).unwrap();
        assert_eq!(content, "new");

        let _ = std::fs::remove_dir_all(&dest);
    }

    #[test]
    fn extraction_rejects_path_traversal() {
        let dest = unique_dir("zip-slip-dest");

        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default();
        writer.start_file("../evil.txt", options).unwrap();
        writer.write_all(b"pwned").unwrap();
        let bytes = writer.finish().unwrap().into_inner();

        extract_zip(&bytes, &dest, None).unwrap();

        assert!(!dest.join("evil.txt").exists());
        let escaped = dest.parent().unwrap().join("evil.txt");
        assert!(!escaped.exists());

        let _ = std::fs::remove_dir_all(&dest);
    }

    // Reproduit le zip Thunderstore réel de BepInExPack_Valheim : un dossier
    // d'enveloppe contenant BepInEx/ + des fichiers d'install, à côté de métadonnées
    // Thunderstore (manifest.json, README.md...) au vrai niveau racine.
    fn build_thunderstore_style_zip() -> Vec<u8> {
        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default();

        writer.start_file("manifest.json", options).unwrap();
        writer
            .write_all(
                br#"{"name": "EpicLoot", "version_number": "5.4.2333", "description": "Adds legendary loot.", "dependencies": ["denikson-BepInExPack_Valheim-5.4.2333"]}"#,
            )
            .unwrap();

        writer.start_file("README.md", options).unwrap();
        writer.write_all(b"metadonnees Thunderstore").unwrap();

        writer
            .start_file("BepInExPack_Valheim/winhttp.dll", options)
            .unwrap();
        writer.write_all(b"proxy").unwrap();

        writer
            .start_file("BepInExPack_Valheim/BepInEx/core/BepInEx.dll", options)
            .unwrap();
        writer.write_all(b"core").unwrap();

        writer.finish().unwrap().into_inner()
    }

    #[test]
    fn extraction_reroots_on_bepinex_and_skips_thunderstore_metadata() {
        let dest = unique_dir("reroot-dest");
        let bytes = build_thunderstore_style_zip();

        extract_zip(&bytes, &dest, Some("BepInEx")).unwrap();

        assert!(dest.join("winhttp.dll").exists());
        assert!(dest.join("BepInEx/core/BepInEx.dll").exists());
        assert!(!dest.join("manifest.json").exists());
        assert!(!dest.join("README.md").exists());
        assert!(!dest.join("BepInExPack_Valheim").exists());

        let _ = std::fs::remove_dir_all(&dest);
    }

    // Reproduit la convention Thunderstore (utilisée par certains mods, ex:
    // warpalicious-More_World_Locations_AIO) où le contenu réel du mod est enveloppé
    // dans un dossier `plugins/` au niveau racine de l'archive, à côté des métadonnées
    // — r2modman/Gale fusionnent directement ce dossier dans `BepInEx/plugins/<mod>/`,
    // sans garder `plugins/` en trop dans le chemin, et jettent les métadonnées.
    #[test]
    fn mod_extraction_reroots_on_plugins_folder() {
        let profile = unique_dir("mod-plugins-profile");

        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default();

        writer.start_file("manifest.json", options).unwrap();
        writer
            .write_all(br#"{"name": "More_World_Locations_AIO", "version_number": "5.0.8"}"#)
            .unwrap();

        writer.start_file("README.md", options).unwrap();
        writer.write_all(b"metadonnees Thunderstore").unwrap();

        writer
            .start_file("plugins/More_World_Locations_AIO.dll", options)
            .unwrap();
        writer.write_all(b"dll").unwrap();

        writer
            .start_file("plugins/Bundles/mwl_abandonedhouse1", options)
            .unwrap();
        writer.write_all(b"bundle").unwrap();

        let bytes = writer.finish().unwrap().into_inner();

        extract_mod_zip(&bytes, &profile, "mwl").unwrap();

        let mod_dir = valheim::bepinex_plugins_dir(&profile).join("mwl");
        assert!(mod_dir.join("More_World_Locations_AIO.dll").exists());
        assert!(mod_dir.join("Bundles/mwl_abandonedhouse1").exists());
        assert!(!mod_dir.join("plugins").exists());
        assert!(!mod_dir.join("manifest.json").exists());
        assert!(!mod_dir.join("README.md").exists());

        let _ = std::fs::remove_dir_all(&profile);
    }

    // Un mod sans convention `plugins/` (le cas le plus courant : dll directement à la
    // racine de l'archive) doit suivre le comportement par défaut — tout part dans
    // `BepInEx/plugins/<slug>/`, métadonnées Thunderstore filtrées.
    #[test]
    fn mod_extraction_falls_back_to_plugins_when_flat() {
        let profile = unique_dir("mod-flat-profile");

        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default();
        writer.start_file("manifest.json", options).unwrap();
        writer.write_all(b"{}").unwrap();
        writer.start_file("EpicLoot.dll", options).unwrap();
        writer.write_all(b"dll").unwrap();
        let bytes = writer.finish().unwrap().into_inner();

        extract_mod_zip(&bytes, &profile, "epicloot").unwrap();

        let mod_dir = valheim::bepinex_plugins_dir(&profile).join("epicloot");
        assert!(mod_dir.join("EpicLoot.dll").exists());
        assert!(!mod_dir.join("manifest.json").exists());

        let _ = std::fs::remove_dir_all(&profile);
    }

    // `config/` est partagé entre tous les mods, sans sous-dossier par slug —
    // contrairement à `plugins/`/`patchers/`/`monomod/`.
    #[test]
    fn mod_extraction_shares_config_folder_without_namespacing() {
        let profile = unique_dir("mod-config-profile");

        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default();
        writer.start_file("plugins/MyMod.dll", options).unwrap();
        writer.write_all(b"dll").unwrap();
        writer.start_file("config/MyMod.cfg", options).unwrap();
        writer.write_all(b"[General]").unwrap();
        let bytes = writer.finish().unwrap().into_inner();

        extract_mod_zip(&bytes, &profile, "mymod").unwrap();

        assert!(valheim::bepinex_config_dir(&profile)
            .join("MyMod.cfg")
            .exists());
        assert!(!valheim::bepinex_config_dir(&profile).join("mymod").exists());

        let _ = std::fs::remove_dir_all(&profile);
    }

    // `patchers/`/`monomod/` sont namespacés par slug comme `plugins/`, et un fichier
    // `*.mm.dll` est routé vers `monomod/<slug>/` même s'il n'est pas dans un dossier
    // `monomod/` dans l'archive.
    #[test]
    fn mod_extraction_namespaces_patchers_and_routes_mm_dll_by_extension() {
        let profile = unique_dir("mod-patchers-profile");

        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default();
        writer
            .start_file("patchers/MyPatcher.dll", options)
            .unwrap();
        writer.write_all(b"patcher").unwrap();
        writer.start_file("plugins/Extra.mm.dll", options).unwrap();
        writer.write_all(b"monomod-patch").unwrap();
        let bytes = writer.finish().unwrap().into_inner();

        extract_mod_zip(&bytes, &profile, "mymod").unwrap();

        assert!(valheim::bepinex_patchers_dir(&profile)
            .join("mymod/MyPatcher.dll")
            .exists());
        assert!(valheim::bepinex_monomod_dir(&profile)
            .join("mymod/Extra.mm.dll")
            .exists());
        assert!(!valheim::bepinex_plugins_dir(&profile)
            .join("mymod/Extra.mm.dll")
            .exists());

        let _ = std::fs::remove_dir_all(&profile);
    }

    #[test]
    fn read_manifest_info_finds_nested_manifest() {
        let bytes = build_thunderstore_style_zip();
        let info = read_manifest_info(&bytes);
        assert_eq!(info.version_number.as_deref(), Some("5.4.2333"));
        assert_eq!(info.name.as_deref(), Some("EpicLoot"));
        assert_eq!(info.description.as_deref(), Some("Adds legendary loot."));
        assert_eq!(
            info.dependencies,
            vec!["denikson-BepInExPack_Valheim-5.4.2333".to_string()]
        );
    }

    // Cas réel rencontré avec le manifest.json de shudnal/Seasons : généré via
    // PowerShell `ConvertTo-Json | Out-File`, qui écrit un BOM UTF-8 en tête de fichier
    // — invalide en JSON strict, faisait échouer le parsing en silence (retour aux
    // valeurs par défaut) avant que `read_manifest_info` ne le retire.
    #[test]
    fn read_manifest_info_strips_utf8_bom() {
        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default();
        writer.start_file("manifest.json", options).unwrap();
        let mut contents = vec![0xef, 0xbb, 0xbf];
        contents.extend_from_slice(
            br#"{"name": "Seasons", "version_number": "1.8.2", "description": "Four seasons.", "dependencies": []}"#,
        );
        writer.write_all(&contents).unwrap();
        let bytes = writer.finish().unwrap().into_inner();

        let info = read_manifest_info(&bytes);
        assert_eq!(info.name.as_deref(), Some("Seasons"));
        assert_eq!(info.version_number.as_deref(), Some("1.8.2"));
        assert_eq!(info.description.as_deref(), Some("Four seasons."));
    }

    #[test]
    fn read_manifest_info_defaults_without_manifest() {
        let mut writer = zip::ZipWriter::new(Cursor::new(Vec::new()));
        let options = zip::write::SimpleFileOptions::default();
        writer.start_file("MyMod.dll", options).unwrap();
        writer.write_all(b"dll").unwrap();
        let bytes = writer.finish().unwrap().into_inner();

        let info = read_manifest_info(&bytes);
        assert!(info.name.is_none());
        assert!(info.version_number.is_none());
        assert!(info.description.is_none());
        assert!(info.dependencies.is_empty());
    }

    #[test]
    fn slugify_produces_stable_folder_names() {
        assert_eq!(slugify("Epic Loot"), "epic-loot");
        assert_eq!(slugify("  Weird!!  Name__2  "), "weird-name-2");
    }

    fn manifest_with_mods(bepinex_sha: &str, mods: &[(&str, &str)]) -> Manifest {
        Manifest {
            slug: "default".to_string(),
            name: "Fedoheim".to_string(),
            version: "1.0.0".to_string(),
            bepinex: Some(BepinexEntry {
                download_url: "/uploads/bepinex.zip".to_string(),
                sha256: bepinex_sha.to_string(),
                version: "5.4.2333".to_string(),
            }),
            mods: mods
                .iter()
                .map(|(name, sha)| ModEntry {
                    name: name.to_string(),
                    version: "1.0.0".to_string(),
                    download_url: format!("/uploads/{name}.zip"),
                    sha256: sha.to_string(),
                })
                .collect(),
            config_files: Vec::new(),
        }
    }

    fn manifest_with_config_files(bepinex_sha: &str, files: &[(&str, &str)]) -> Manifest {
        let mut manifest = manifest_with_mods(bepinex_sha, &[]);
        manifest.config_files = files
            .iter()
            .map(|(filename, sha)| ConfigFileEntry {
                filename: filename.to_string(),
                download_url: format!("/uploads/{filename}"),
                sha256: sha.to_string(),
            })
            .collect();
        manifest
    }

    #[test]
    fn manifest_needs_update_false_when_config_files_identical_ignoring_order() {
        let local = manifest_with_config_files("a", &[("FastLink.cfg", "1"), ("Other.cfg", "2")]);
        let remote = manifest_with_config_files("a", &[("Other.cfg", "2"), ("FastLink.cfg", "1")]);
        assert!(!manifest_needs_update(&local, &remote));
    }

    #[test]
    fn manifest_needs_update_true_on_config_file_sha_change() {
        let local = manifest_with_config_files("a", &[("FastLink.cfg", "1")]);
        let remote = manifest_with_config_files("a", &[("FastLink.cfg", "2")]);
        assert!(manifest_needs_update(&local, &remote));
    }

    #[test]
    fn manifest_needs_update_true_on_config_file_added() {
        let local = manifest_with_config_files("a", &[]);
        let remote = manifest_with_config_files("a", &[("FastLink.cfg", "1")]);
        assert!(manifest_needs_update(&local, &remote));
    }

    #[test]
    fn manifest_needs_update_false_when_identical_ignoring_order() {
        let local = manifest_with_mods("a", &[("Epic Loot", "1"), ("Warfare", "2")]);
        let remote = manifest_with_mods("a", &[("Warfare", "2"), ("Epic Loot", "1")]);
        assert!(!manifest_needs_update(&local, &remote));
    }

    #[test]
    fn manifest_needs_update_true_on_mod_sha_change() {
        let local = manifest_with_mods("a", &[("Epic Loot", "1")]);
        let remote = manifest_with_mods("a", &[("Epic Loot", "2")]);
        assert!(manifest_needs_update(&local, &remote));
    }

    #[test]
    fn manifest_needs_update_true_on_bepinex_change() {
        let local = manifest_with_mods("a", &[("Epic Loot", "1")]);
        let remote = manifest_with_mods("b", &[("Epic Loot", "1")]);
        assert!(manifest_needs_update(&local, &remote));
    }
}
