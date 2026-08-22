use crate::config;
use serde::{Deserialize, Serialize};

// Nom brut de l'enum `Heightmap.Biome` côté jeu (ex: "Meadows", "BlackForest"), pas
// traduit ici — voir HomePage.tsx pour l'affichage en français. `None` si le joueur a
// désactivé le partage de sa position (`ZNet.PlayerInfo.m_publicPosition` côté mod).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OnlinePlayer {
    pub name: String,
    pub biome: Option<String>,
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
