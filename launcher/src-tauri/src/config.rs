// Port fixe utilisé par le serveur loopback local pendant le login Discord.
// Doit correspondre exactement à une "Redirect URI" enregistrée sur l'app Discord :
// http://127.0.0.1:38217/callback (voir api/README.md).
pub const LOOPBACK_PORT: u16 = 38217;

// Client ID de l'app OAuth2 Discord (valeur publique, pas un secret).
pub const DISCORD_CLIENT_ID: &str = "1539934485460688926";

// `VALHEIM_API_URL` permet de surcharger l'URL dans les deux sens (ex: pointer un build
// de dev vers la prod pour tester, ou l'inverse) — sans elle, un build de dev
// (`tauri dev`) cible l'API locale, un build de release (celui distribué aux joueurs)
// cible directement l'API de prod.
pub fn api_base_url() -> String {
    std::env::var("VALHEIM_API_URL").unwrap_or_else(|_| {
        if cfg!(debug_assertions) {
            "http://127.0.0.1:3000".to_string()
        } else {
            "https://fedoheim.hopto.org".to_string()
        }
    })
}

pub fn redirect_uri() -> String {
    format!("http://127.0.0.1:{LOOPBACK_PORT}/callback")
}

// Message clair quand l'API n'est pas joignable (serveur éteint, pas de réseau...),
// utilisé partout où le launcher fait un appel HTTP, plutôt que laisser fuir l'erreur
// technique brute de reqwest ("error sending request for url (...)") jusqu'à l'UI.
pub fn describe_request_error(e: &reqwest::Error) -> String {
    if e.is_connect() || e.is_timeout() {
        format!(
            "Impossible de joindre l'API Fedoheim ({}). Vérifie ta connexion et réessaie.",
            api_base_url()
        )
    } else {
        format!("Erreur réseau : {e}")
    }
}
