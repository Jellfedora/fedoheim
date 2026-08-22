use crate::config;
use serde::{Deserialize, Serialize};

// `biome` est déjà le texte final tel que configuré dans le .cfg du mod (voir
// fedo.servertools.cfg, section [Biomes]) -- affiché tel quel, pas de traduction ici.
// `None` si le joueur n'a pas de position exploitable pour ce rapport. `armor` est le
// total actuel (Humanoid.GetBodyArmor(), arrondi côté mod), `None` si le personnage
// n'a pas pu être retrouvé côté serveur.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OnlinePlayer {
    pub name: String,
    pub biome: Option<String>,
    pub armor: Option<i64>,
}

// Alimenté par le mod serveur FedoServerTools (voir /mods/FedoServerTools), qui poste
// la liste des joueurs connectés toutes les ~30s -- `online` reflète la fraîcheur de ce
// rapport côté API (voir onlinePlayers.ts), pas juste sa présence.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OnlinePlayers {
    pub online: bool,
    pub players: Vec<OnlinePlayer>,
    #[serde(rename = "updatedAt")]
    pub updated_at: Option<String>,
}

// Lecture publique, pas d'auth requise — comme le règlement, la FAQ et le statut
// BepInEx : un joueur doit pouvoir voir qui est en ligne sans être connecté.
pub async fn fetch_online_players(
    http: &reqwest::Client,
    slug: &str,
) -> Result<OnlinePlayers, String> {
    let res = http
        .get(format!(
            "{}/modpacks/{slug}/online-players",
            config::api_base_url()
        ))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!("Failed to fetch online players ({})", res.status()));
    }

    res.json().await.map_err(|e| e.to_string())
}
