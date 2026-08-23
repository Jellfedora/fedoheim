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
// la liste des joueurs connectés toutes les `SyncIntervalSeconds` -- `status` reflète
// le cycle de vie du serveur ("starting" pendant le chargement des mods/du monde,
// "online" une fois démarré, "stopping" pendant un arrêt, "offline" si l'API n'a plus
// reçu de rapport depuis 90s, ex: crash) ; `online` reste exposé en plus, dérivé côté
// API (`status === "online"`), pour un usage qui n'a besoin que du booléen.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct OnlinePlayers {
    pub status: String,
    pub online: bool,
    pub players: Vec<OnlinePlayer>,
    // Saison actuelle rapportée par le mod Seasons (shudnal/Seasons) via
    // FedoServerTools, déjà traduite côté mod -- `None` si ce mod tiers n'est pas
    // installé sur le serveur, ou si le dernier rapport est périmé.
    pub season: Option<String>,
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

// Dernier état connu d'un joueur pour ce profil (voir player_stats côté API) — reste
// affiché (biome/armure/dernière connexion) même une fois déconnecté, contrairement à
// OnlinePlayer ci-dessus qui ne représente qu'un joueur actuellement en ligne.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PlayerStat {
    pub name: String,
    pub biome: Option<String>,
    pub armor: Option<i64>,
    pub online: bool,
    #[serde(rename = "lastSeenAt")]
    pub last_seen_at: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PlayerStatsResponse {
    pub players: Vec<PlayerStat>,
}

// Public comme fetch_online_players ci-dessus — page "Joueurs" du launcher.
pub async fn fetch_player_stats(
    http: &reqwest::Client,
    slug: &str,
) -> Result<PlayerStatsResponse, String> {
    let res = http
        .get(format!(
            "{}/modpacks/{slug}/player-stats",
            config::api_base_url()
        ))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!("Failed to fetch player stats ({})", res.status()));
    }

    res.json().await.map_err(|e| e.to_string())
}
