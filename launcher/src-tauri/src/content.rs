use crate::config;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FaqEntry {
    pub question: String,
    pub answer: String,
}

pub async fn fetch_rules(http: &reqwest::Client) -> Result<Vec<String>, String> {
    let res = http
        .get(format!("{}/content/rules", config::api_base_url()))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!("Failed to fetch rules ({})", res.status()));
    }

    res.json().await.map_err(|e| e.to_string())
}

// Réservé aux admins : l'API revérifie le rôle Discord en direct (voir requireAdmin
// côté api/src/auth/plugin.ts) et renvoie 403 si l'appelant n'est pas admin.
pub async fn save_rules(
    http: &reqwest::Client,
    token: &str,
    rules: Vec<String>,
) -> Result<(), String> {
    let res = http
        .put(format!("{}/content/rules", config::api_base_url()))
        .bearer_auth(token)
        .json(&serde_json::json!({ "rules": rules }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to save rules ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}

pub async fn fetch_faq(http: &reqwest::Client) -> Result<Vec<FaqEntry>, String> {
    let res = http
        .get(format!("{}/content/faq", config::api_base_url()))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!("Failed to fetch FAQ ({})", res.status()));
    }

    res.json().await.map_err(|e| e.to_string())
}

// Réservé aux admins, comme save_rules ci-dessus.
pub async fn save_faq(
    http: &reqwest::Client,
    token: &str,
    faq: Vec<FaqEntry>,
) -> Result<(), String> {
    let res = http
        .put(format!("{}/content/faq", config::api_base_url()))
        .bearer_auth(token)
        .json(&serde_json::json!({ "faq": faq }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to save FAQ ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    Ok(())
}
