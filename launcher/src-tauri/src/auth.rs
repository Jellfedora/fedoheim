use crate::config;
use crate::session::{Session, UserInfo};
use serde::Deserialize;
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tiny_http::{Response, Server};
use tokio::sync::oneshot;

const LOGIN_TIMEOUT: Duration = Duration::from_secs(180);

// Permet d'annuler une connexion en cours (voir cancel_login) : le thread d'écoute
// bloque sur incoming_requests() en attendant Discord, et `Server::unblock()` est le
// seul moyen propre de l'interrompre sans laisser le port loopback occupé.
pub type PendingLogin = Mutex<Option<Arc<Server>>>;

#[derive(Deserialize)]
struct TokenResponse {
    token: String,
    user: UserInfo,
}

// Démarre le serveur HTTP loopback local et son thread d'écoute (bloquant), qui capture
// le `code` renvoyé par Discord sur `/callback` puis se termine après une seule requête
// (ou plus tôt si `unblock()` est appelé sur le Server retourné).
fn start_loopback_server(
    expected_state: String,
    tx: oneshot::Sender<Result<String, String>>,
) -> Result<Arc<Server>, String> {
    let server = Server::http(format!("127.0.0.1:{}", config::LOOPBACK_PORT))
        .map_err(|e| format!("Could not start local callback server: {e}"))?;
    let server = Arc::new(server);
    let listener = server.clone();

    std::thread::spawn(move || {
        // On n'attend qu'une seule requête pertinente (Discord peut aussi faire une
        // requête favicon.ico qu'on ignore).
        for request in listener.incoming_requests() {
            let url = format!("http://127.0.0.1/{}", request.url().trim_start_matches('/'));
            let parsed = match url::Url::parse(&url) {
                Ok(u) => u,
                Err(_) => continue,
            };

            if parsed.path() != "/callback" {
                let _ = request.respond(Response::from_string("Not found").with_status_code(404));
                continue;
            }

            let params: std::collections::HashMap<_, _> = parsed.query_pairs().collect();
            let code = params.get("code").map(|c| c.to_string());
            let state = params.get("state").map(|s| s.to_string());

            let body = if code.is_some() && state.as_deref() == Some(expected_state.as_str()) {
                "Connexion réussie, tu peux fermer cet onglet."
            } else {
                "Connexion échouée, tu peux fermer cet onglet et réessayer depuis le launcher."
            };
            let _ = request.respond(Response::from_string(body));

            let result = match (code, state) {
                (Some(code), Some(state)) if state == expected_state => Ok(code),
                _ => Err("Invalid or missing OAuth state/code".to_string()),
            };
            let _ = tx.send(result);
            return;
        }
        // Sorti via unblock() (annulation ou timeout), sans requête reçue.
        let _ = tx.send(Err("Login cancelled".to_string()));
    });

    Ok(server)
}

pub async fn login(http: &reqwest::Client, pending: &PendingLogin) -> Result<Session, String> {
    let state = uuid::Uuid::new_v4().to_string();
    let (tx, rx) = oneshot::channel();
    let server = start_loopback_server(state.clone(), tx)?;
    *pending.lock().unwrap() = Some(server.clone());

    let redirect_uri = config::redirect_uri();
    let authorize_url = url::Url::parse_with_params(
        "https://discord.com/api/oauth2/authorize",
        &[
            ("client_id", config::DISCORD_CLIENT_ID),
            ("redirect_uri", redirect_uri.as_str()),
            ("response_type", "code"),
            ("scope", "identify"),
            ("state", state.as_str()),
        ],
    )
    .map_err(|e| e.to_string())?;

    if let Err(e) = open::that(authorize_url.as_str()) {
        *pending.lock().unwrap() = None;
        return Err(format!("Could not open browser: {e}"));
    }

    let outcome = tokio::time::timeout(LOGIN_TIMEOUT, rx).await;
    *pending.lock().unwrap() = None;

    let code = match outcome {
        Ok(Ok(result)) => result?,
        Ok(Err(_)) => return Err("Login cancelled".to_string()),
        Err(_) => {
            // Le thread d'écoute attend toujours : on le débloque pour libérer le port
            // avant de remonter l'erreur, sinon la prochaine tentative échouerait.
            server.unblock();
            return Err("Login timed out".to_string());
        }
    };

    let res = http
        .post(format!("{}/auth/discord/token", config::api_base_url()))
        .json(&serde_json::json!({ "code": code, "redirectUri": redirect_uri }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        let body = res.text().await.unwrap_or_default();
        let error_code = serde_json::from_str::<serde_json::Value>(&body)
            .ok()
            .and_then(|v| v.get("error").and_then(|e| e.as_str()).map(str::to_string));

        return Err(match error_code.as_deref() {
            // Messages clairs pour les deux refus attendus de POST /auth/discord/token
            // (voir api/src/auth/routes.ts) — un joueur ne doit jamais voir le JSON brut
            // de l'API pour un cas aussi courant que "pas le bon rôle Discord". Préfixe
            // "AUTH_WARNING:" détecté côté App.tsx pour un ton neutre (pas rouge/alarmant
            // — ce n'est pas un bug, juste une autorisation manquante) — retiré avant
            // affichage.
            Some("Missing required Discord role") => {
                "AUTH_WARNING:Tu n'as pas encore l'autorisation nécessaire pour rejoindre le \
                 serveur. Demande à un admin du Discord Fedoheim de te donner le rôle qui y \
                 donne accès."
                    .to_string()
            }
            Some("Banned") => "AUTH_WARNING:Ton compte a été banni du serveur. Contacte un \
                 administrateur sur le Discord Fedoheim si tu penses qu'il s'agit d'une erreur."
                .to_string(),
            _ => format!("Connexion refusée par l'API : {body}"),
        });
    }

    let payload: TokenResponse = res.json().await.map_err(|e| e.to_string())?;
    Ok(Session {
        token: payload.token,
        user: payload.user,
    })
}

// Débloque le thread d'écoute loopback d'une connexion en cours, si il y en a une.
// Sans effet (pas d'erreur) si aucune connexion n'est en cours.
pub fn cancel_login(pending: &PendingLogin) {
    if let Some(server) = pending.lock().unwrap().as_ref() {
        server.unblock();
    }
}

pub enum RefreshError {
    // Token invalide ou rôle Discord requis perdu : il faut déconnecter le joueur.
    Unauthorized(String),
    // Erreur transitoire (réseau, Discord indisponible...) : on garde la session actuelle.
    Other(String),
}

// Revalidation périodique en tâche de fond : relit le rôle Discord du joueur côté API
// sans jamais rouvrir de navigateur ni redemander de login.
pub async fn refresh(http: &reqwest::Client, token: &str) -> Result<UserInfo, RefreshError> {
    let res = http
        .get(format!("{}/auth/me", config::api_base_url()))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| RefreshError::Other(config::describe_request_error(&e)))?;

    let status = res.status();
    if status == reqwest::StatusCode::UNAUTHORIZED || status == reqwest::StatusCode::FORBIDDEN {
        return Err(RefreshError::Unauthorized(
            res.text().await.unwrap_or_default(),
        ));
    }
    if !status.is_success() {
        return Err(RefreshError::Other(format!(
            "Refresh failed ({status}): {}",
            res.text().await.unwrap_or_default()
        )));
    }

    res.json()
        .await
        .map_err(|e| RefreshError::Other(e.to_string()))
}

pub async fn accept_rules(http: &reqwest::Client, token: &str) -> Result<UserInfo, String> {
    let res = http
        .post(format!("{}/auth/accept-rules", config::api_base_url()))
        .bearer_auth(token)
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to accept rules ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}

pub async fn set_steam_id(
    http: &reqwest::Client,
    token: &str,
    steam_id: &str,
) -> Result<UserInfo, String> {
    let res = http
        .post(format!("{}/auth/steam-id", config::api_base_url()))
        .bearer_auth(token)
        .json(&serde_json::json!({ "steamId": steam_id }))
        .send()
        .await
        .map_err(|e| config::describe_request_error(&e))?;

    if !res.status().is_success() {
        return Err(format!(
            "Failed to save Steam ID ({}): {}",
            res.status(),
            res.text().await.unwrap_or_default()
        ));
    }

    res.json().await.map_err(|e| e.to_string())
}
