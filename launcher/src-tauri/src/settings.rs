use crate::config;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Settings {
    #[serde(rename = "buyMeACoffeeUrl")]
    pub buy_me_a_coffee_url: String,
    #[serde(rename = "heroEyebrow")]
    pub hero_eyebrow: String,
    #[serde(rename = "heroTagline")]
    pub hero_tagline: String,
}

// Lecture publique, pas d'auth requise — comme le règlement et la FAQ.
pub async fn fetch_settings(http: &reqwest::Client) -> Result<Settings, String> {
    let res = http
        .get(format!("{}/settings", config::api_base_url()))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!("Failed to fetch settings ({})", res.status()));
    }

    res.json().await.map_err(|e| e.to_string())
}

// Réservé aux admins : l'API revérifie le rôle Discord en direct et renvoie 403 sinon.
pub async fn save_settings(
    http: &reqwest::Client,
    token: &str,
    settings: &Settings,
) -> Result<Settings, String> {
    let res = http
        .put(format!("{}/settings", config::api_base_url()))
        .bearer_auth(token)
        .json(settings)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to save settings ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}
