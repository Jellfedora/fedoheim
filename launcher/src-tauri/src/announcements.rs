use crate::config;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Announcement {
    pub id: i64,
    pub author: String,
    pub title: Option<String>,
    pub message: String,
    pub images: Vec<String>,
    #[serde(rename = "createdAt")]
    pub created_at: String,
    #[serde(rename = "updatedAt")]
    pub updated_at: Option<String>,
    #[serde(rename = "postedToDiscord")]
    pub posted_to_discord: bool,
}

// Champs communs à la création et à l'édition d'une annonce.
#[derive(Debug, Clone, Serialize)]
pub struct AnnouncementDraft {
    pub title: Option<String>,
    pub message: String,
    pub images: Vec<String>,
}

// Lecture publique, pas d'auth requise — comme le règlement et la FAQ.
pub async fn fetch_announcements(http: &reqwest::Client) -> Result<Vec<Announcement>, String> {
    let res = http
        .get(format!("{}/announcements", config::api_base_url()))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!("Failed to fetch announcements ({})", res.status()));
    }

    res.json().await.map_err(|e| e.to_string())
}

// Réservé aux admins : l'auteur est dérivé du compte authentifié côté API, pas envoyé
// depuis le launcher.
pub async fn post_announcement(
    http: &reqwest::Client,
    token: &str,
    draft: &AnnouncementDraft,
) -> Result<Announcement, String> {
    let res = http
        .post(format!("{}/announcements", config::api_base_url()))
        .bearer_auth(token)
        .json(draft)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to post announcement ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

pub async fn update_announcement(
    http: &reqwest::Client,
    token: &str,
    id: i64,
    draft: &AnnouncementDraft,
) -> Result<Announcement, String> {
    let res = http
        .put(format!("{}/announcements/{id}", config::api_base_url()))
        .bearer_auth(token)
        .json(draft)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to update announcement ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

pub async fn delete_announcement(
    http: &reqwest::Client,
    token: &str,
    id: i64,
) -> Result<(), String> {
    let res = http
        .delete(format!("{}/announcements/{id}", config::api_base_url()))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to delete announcement ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

#[derive(Debug, Deserialize)]
struct UploadResponse {
    url: String,
}

// Upload une image locale (chemin choisi via le sélecteur de fichier natif côté
// frontend) et renvoie son URL relative servie par l'API (ex: "/uploads/xxx.png").
pub async fn upload_image(
    http: &reqwest::Client,
    token: &str,
    file_path: &str,
) -> Result<String, String> {
    let bytes = tokio::fs::read(file_path)
        .await
        .map_err(|e| format!("Could not read file: {e}"))?;

    let filename = std::path::Path::new(file_path)
        .file_name()
        .map(|n| n.to_string_lossy().to_string())
        .unwrap_or_else(|| "image".to_string());

    let mime = mime_guess_from_extension(file_path);
    let part = reqwest::multipart::Part::bytes(bytes)
        .file_name(filename)
        .mime_str(mime)
        .map_err(|e| e.to_string())?;
    let form = reqwest::multipart::Form::new().part("file", part);

    let res = http
        .post(format!("{}/announcements/images", config::api_base_url()))
        .bearer_auth(token)
        .multipart(form)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to upload image ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    let payload: UploadResponse = res.json().await.map_err(|e| e.to_string())?;
    Ok(payload.url)
}

// Doit rester cohérent avec EXTENSION_BY_MIME côté api/src/announcements/images.ts,
// qui revalide de toute façon le mimetype réel du fichier reçu.
fn mime_guess_from_extension(file_path: &str) -> &'static str {
    match std::path::Path::new(file_path)
        .extension()
        .and_then(|e| e.to_str())
        .map(|e| e.to_ascii_lowercase())
        .as_deref()
    {
        Some("png") => "image/png",
        Some("jpg") | Some("jpeg") => "image/jpeg",
        Some("webp") => "image/webp",
        Some("gif") => "image/gif",
        _ => "application/octet-stream",
    }
}
