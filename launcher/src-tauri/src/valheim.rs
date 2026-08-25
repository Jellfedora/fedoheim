use std::path::{Path, PathBuf};
use tauri::AppHandle;
#[cfg(windows)]
use tauri::Manager;

pub const VALHEIM_APP_ID: &str = "892970";

// Détecte le dossier d'install Steam de Valheim. Windows en priorité (cible
// principale), macOS en support secondaire — voir /CLAUDE.md "Plateforme cible".
pub fn find_install_dir() -> Result<PathBuf, String> {
    #[cfg(windows)]
    {
        if let Some(dir) = find_install_dir_windows() {
            return Ok(dir);
        }
    }

    #[cfg(target_os = "macos")]
    {
        if let Some(dir) = find_install_dir_macos() {
            return Ok(dir);
        }
    }

    Err(
        "Impossible de trouver l'installation Valheim. Vérifie que le jeu est installé via Steam."
            .to_string(),
    )
}

#[cfg(windows)]
fn steam_path_windows() -> Option<PathBuf> {
    use winreg::enums::HKEY_CURRENT_USER;
    use winreg::RegKey;

    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let steam_key = hkcu.open_subkey("Software\\Valve\\Steam").ok()?;
    let steam_path: String = steam_key.get_value("SteamPath").ok()?;
    Some(PathBuf::from(steam_path))
}

#[cfg(windows)]
fn find_install_dir_windows() -> Option<PathBuf> {
    let candidate = steam_path_windows()?.join("steamapps/common/Valheim");
    if candidate.join("valheim.exe").exists() {
        Some(candidate)
    } else {
        None
    }
}

// L'exe Steam lui-même (pas le jeu) : c'est via lui qu'on lance Valheim, seul moyen de
// transmettre les arguments Doorstop au processus du jeu (voir `launch` ci-dessous).
#[cfg(windows)]
fn find_steam_exe_windows() -> Option<PathBuf> {
    let candidate = steam_path_windows()?.join("steam.exe");
    if candidate.exists() {
        Some(candidate)
    } else {
        None
    }
}

#[cfg(target_os = "macos")]
fn find_install_dir_macos() -> Option<PathBuf> {
    let home = dirs_home()?;
    let candidate = home.join("Library/Application Support/Steam/steamapps/common/Valheim");
    if candidate.exists() {
        Some(candidate)
    } else {
        None
    }
}

#[cfg(target_os = "macos")]
fn dirs_home() -> Option<PathBuf> {
    std::env::var_os("HOME").map(PathBuf::from)
}

// Dossier racine de BepInEx (core + tous les dossiers "override" ci-dessous) —
// supprimer ce seul dossier suffit à effacer BepInEx et tous les mods installés, voir
// `modpack::wipe_local_install`.
pub fn bepinex_dir(dir: &Path) -> PathBuf {
    dir.join("BepInEx")
}

pub fn bepinex_plugins_dir(dir: &Path) -> PathBuf {
    bepinex_dir(dir).join("plugins")
}

// Dossiers "override" reconnus par r2modman/Gale dans un package Thunderstore, en plus
// de `plugins` — voir `mod_override_dir`/`extract_mod_zip` dans modpack.rs pour leur
// usage.
pub fn bepinex_patchers_dir(dir: &Path) -> PathBuf {
    bepinex_dir(dir).join("patchers")
}

pub fn bepinex_monomod_dir(dir: &Path) -> PathBuf {
    bepinex_dir(dir).join("monomod")
}

// `config`, à la différence des autres, est partagé entre tous les mods (pas de
// sous-dossier par mod) — voir `extract_mod_zip`.
pub fn bepinex_config_dir(dir: &Path) -> PathBuf {
    bepinex_dir(dir).join("config")
}

// Lu au boot du jeu par la partie client de FedoServerTools (voir mods/FedoServerTools/
// SessionFile.cs) -- jamais synchronisé/zippé comme le contenu d'un mod (même logique
// que ServerToken) : sous `BepInEx/` mais hors de `plugins/<mod>/`, ignoré par
// `bootstrap_copy` (qui saute tout ce qui s'appelle "BepInEx") et par le nettoyage de
// `sync_mods` (qui n'agit que sur `BepInEx/plugins/*`). Format "clé=valeur" une ligne
// par champ, pas du JSON.
fn mod_session_path(profile_dir: &Path) -> PathBuf {
    bepinex_dir(profile_dir).join("fedoheim-session.txt")
}

// Écrit juste avant chaque lancement (voir `play`/`launch_only` dans lib.rs) --
// `character_name`/`auto_connect` peuvent tous les deux être absents (compte pas encore
// lié, ou profil sans cible configurée) : le mod ne fait alors rien (kill-switch, voir
// CLAUDE.md). `discord_username` sert uniquement à pré-remplir le nom à la création de
// perso (voir FejdStartupAutoNavigatePatch) -- jamais utilisé pour la liaison
// compte<->perso elle-même. Best-effort : une erreur d'écriture ne doit jamais empêcher
// le lancement.
pub fn write_mod_session(
    profile_dir: &Path,
    slug: &str,
    character_name: Option<&str>,
    discord_username: Option<&str>,
    auto_connect: Option<&crate::modpack::AutoConnectTarget>,
) {
    let mut lines = vec![
        format!("slug={slug}"),
        format!("character_name={}", character_name.unwrap_or("")),
        format!("discord_username={}", discord_username.unwrap_or("")),
    ];

    match auto_connect {
        Some(target) if target.kind == "world" => {
            lines.push("auto_connect_type=world".to_string());
            lines.push(format!(
                "auto_connect_world={}",
                target.world.as_deref().unwrap_or("")
            ));
        }
        Some(target) if target.kind == "server" => {
            lines.push("auto_connect_type=server".to_string());
            lines.push(format!(
                "auto_connect_host={}",
                target.host.as_deref().unwrap_or("")
            ));
            lines.push(format!(
                "auto_connect_port={}",
                target.port.map(|p| p.to_string()).unwrap_or_default()
            ));
            lines.push(format!(
                "auto_connect_password={}",
                target.password.as_deref().unwrap_or("")
            ));
        }
        _ => {}
    }

    let _ = std::fs::write(mod_session_path(profile_dir), lines.join("\n"));
}

// Dossier où vivent BepInEx et les mods. Sur Windows : dossier externe, hors de
// l'install Steam du joueur (façon r2modman/Gale) — même pattern que `session.rs` pour
// son fichier de session (app_data_dir, propre à cette app Tauri). Sur macOS :
// directement le dossier du jeu — c'est ce que fait macheim
// (github.com/lofcgi/macheim), seul mécanisme macOS dont on ait la preuve qu'il
// fonctionne réellement pour ce jeu ; pas de raison de réinventer un dossier externe
// non éprouvé sur cette plateforme (voir CLAUDE.md pour le détail de l'asymétrie).
pub fn profile_dir(app: &AppHandle) -> Result<PathBuf, String> {
    #[cfg(target_os = "macos")]
    {
        let _ = app;
        return find_install_dir();
    }

    #[cfg(windows)]
    {
        let dir = app
            .path()
            .app_data_dir()
            .map_err(|e| format!("Could not resolve app data dir: {e}"))?
            .join("gamedata");
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        return Ok(dir);
    }

    #[allow(unreachable_code)]
    Err("Unsupported platform".to_string())
}

// Recopie tout ce qui est à la racine du profil, SAUF le dossier `BepInEx` lui-même,
// dans le dossier d'install Steam : c'est le petit fichier proxy Doorstop
// (`winhttp.dll` pour BepInExPack_Valheim) et ses compagnons (`doorstop_config.ini`,
// `.doorstop_version`) qui doivent physiquement être à côté de `valheim.exe`
// (détournement de l'ordre de recherche de DLL Windows, aucune alternative). Tout le
// reste (BepInEx/core, plugins, config) reste dans le profil. Best-effort par entrée :
// ne bloque pas tout le lancement si un fichier ne peut pas être copié (ex. verrouillé).
#[cfg(windows)]
fn bootstrap_copy(profile_dir: &Path, install_dir: &Path) {
    let Ok(entries) = std::fs::read_dir(profile_dir) else {
        return;
    };
    for entry in entries.flatten() {
        if entry.file_name() == "BepInEx" {
            continue;
        }
        let dest = install_dir.join(entry.file_name());
        if entry.path().is_dir() {
            let _ = copy_dir_all(&entry.path(), &dest);
        } else {
            let _ = std::fs::copy(entry.path(), dest);
        }
    }
}

#[cfg(windows)]
fn copy_dir_all(src: &Path, dest: &Path) -> Result<(), String> {
    std::fs::create_dir_all(dest).map_err(|e| e.to_string())?;
    for entry in std::fs::read_dir(src).map_err(|e| e.to_string())? {
        let entry = entry.map_err(|e| e.to_string())?;
        let dest_path = dest.join(entry.file_name());
        if entry.path().is_dir() {
            copy_dir_all(&entry.path(), &dest_path)?;
        } else {
            std::fs::copy(entry.path(), dest_path).map_err(|e| e.to_string())?;
        }
    }
    Ok(())
}

// Doorstop v3 par défaut si `.doorstop_version` (livré à la racine du package BepInEx)
// est absent ou ne commence pas par "4" — même défaut que Gale (fork maintenu de
// r2modman) en l'absence de marqueur.
#[cfg(windows)]
fn doorstop_args(profile_dir: &Path) -> Vec<String> {
    let version = std::fs::read_to_string(profile_dir.join(".doorstop_version"))
        .unwrap_or_else(|_| "3".to_string());
    let bepinex_core = profile_dir.join("BepInEx").join("core");

    if version.trim().starts_with('4') {
        vec![
            "--doorstop-enabled".to_string(),
            "true".to_string(),
            "--doorstop-target-assembly".to_string(),
            bepinex_core
                .join("BepInEx.Preloader.dll")
                .to_string_lossy()
                .into_owned(),
        ]
    } else {
        vec![
            "--doorstop-enable".to_string(),
            "true".to_string(),
            "--doorstop-target".to_string(),
            bepinex_core
                .join("BepInEx.dll")
                .to_string_lossy()
                .into_owned(),
        ]
    }
}

#[cfg(target_os = "macos")]
fn steam_is_running() -> bool {
    std::process::Command::new("pgrep")
        .args(["-x", "steam_osx"])
        .output()
        .map(|out| out.status.success())
        .unwrap_or(false)
}

// Contrairement à Windows (où `steam.exe -applaunch` démarre Steam lui-même si besoin,
// voir `launch` ci-dessous), le lancement direct macOS ne passe jamais par Steam —
// or Valheim (Steamworks) a besoin que le client Steam tourne déjà pour que
// SteamAPI_Init() réussisse, sinon le jeu démarre mais plante ses appels Steamworks
// ("Steamworks is not initialized"). Si Steam n'est pas déjà ouvert, on le lance puis
// on attend qu'il soit prêt (poll, quelques secondes) avant de lancer le jeu.
#[cfg(target_os = "macos")]
fn ensure_steam_running() -> Result<(), String> {
    if steam_is_running() {
        return Ok(());
    }

    std::process::Command::new("open")
        .args(["-a", "Steam"])
        .status()
        .map_err(|e| format!("Impossible de lancer Steam: {e}"))?;

    for _ in 0..30 {
        std::thread::sleep(std::time::Duration::from_secs(1));
        if steam_is_running() {
            // Laisse le temps au client de finir son initialisation interne (connexion,
            // login éventuel) avant de démarrer le jeu par-dessus.
            std::thread::sleep(std::time::Duration::from_secs(3));
            return Ok(());
        }
    }

    Err("Steam n'a pas démarré à temps — ouvre Steam manuellement puis relance.".to_string())
}

// Voir le commentaire dans `launch` : évite le ballet de relance via Steam que
// SteamAPI_Init() exige normalement quand le jeu n'est pas démarré par Steam lui-même.
// N'écrase jamais un fichier déjà présent (au cas où l'install Steam en fournirait déjà
// un légitime). Best-effort, jamais bloquant.
#[cfg(target_os = "macos")]
fn ensure_steam_appid_file(macos_dir: &Path) {
    let path = macos_dir.join("steam_appid.txt");
    if path.exists() {
        return;
    }
    let _ = std::fs::write(&path, VALHEIM_APP_ID);
}

#[cfg(target_os = "macos")]
fn find_doorstop_dylib(profile_dir: &Path) -> Option<PathBuf> {
    for dir in [profile_dir.to_path_buf(), profile_dir.join("doorstop_libs")] {
        for name in ["libdoorstop_x64.dylib", "libdoorstop.dylib"] {
            let candidate = dir.join(name);
            if candidate.exists() {
                return Some(candidate);
            }
        }
    }
    None
}

// Retire l'attribut de quarantine Gatekeeper (posé par macOS sur tout fichier
// téléchargé) du .dylib doorstop — sans ça `dyld` refuse de le charger via
// DYLD_INSERT_LIBRARIES. Best-effort, jamais bloquant.
#[cfg(target_os = "macos")]
fn remove_quarantine(path: &Path) {
    let _ = std::process::Command::new("xattr")
        .arg("-d")
        .arg("com.apple.quarantine")
        .arg(path)
        .status();
}

// Étapes propres à macOS après extraction de BepInEx dans le profil : lever la
// quarantine sur le .dylib doorstop, et corriger BepInEx.cfg ("Type = Application" ->
// "Type = GameObject", requis pour BepInEx sur macOS/Unity). Reproduit le comportement
// de macheim (github.com/lofcgi/macheim), qui fonctionne réellement en prod pour ce
// même jeu — pas de raison de réinventer ce mécanisme. Best-effort, jamais bloquant :
// une étape qui rate ne doit pas empêcher le lancement.
#[cfg(target_os = "macos")]
fn finish_bepinex_install_macos(profile_dir: &Path) {
    if let Some(dylib) = find_doorstop_dylib(profile_dir) {
        remove_quarantine(&dylib);
    }

    let config_path = profile_dir
        .join("BepInEx")
        .join("config")
        .join("BepInEx.cfg");
    if let Ok(content) = std::fs::read_to_string(&config_path) {
        let patched = content.replace("Type = Application", "Type = GameObject");
        if patched != content {
            let _ = std::fs::write(&config_path, patched);
        }
    }
}

pub fn launch(install_dir: &Path, profile_dir: &Path) -> Result<(), String> {
    #[cfg(windows)]
    {
        bootstrap_copy(profile_dir, install_dir);
        let steam_exe = find_steam_exe_windows().ok_or_else(|| {
            "Impossible de trouver Steam. Vérifie qu'il est installé.".to_string()
        })?;

        // `-applaunch <appid> <args>` démarre Steam lui-même s'il n'est pas déjà lancé,
        // puis enchaîne sur le jeu avec `<args>` transmis au process du jeu — donc pas
        // besoin de vérifier si Steam tourne avant. Contrairement au protocole
        // `steam://run/…`, qui ignore les arguments additionnels.
        std::process::Command::new(steam_exe)
            .arg("-applaunch")
            .arg(VALHEIM_APP_ID)
            .args(doorstop_args(profile_dir))
            .spawn()
            .map_err(|e| format!("Could not launch Valheim: {e}"))?;
        return Ok(());
    }

    #[cfg(target_os = "macos")]
    {
        // `profile_dir` == `install_dir` sur macOS (voir `profile_dir` ci-dessus) : pas
        // de fichier à recopier comme sur Windows, juste les finitions BepInEx puis le
        // lancement direct (pas via Steam ici, contrairement à Windows) — mais Steam
        // doit quand même déjà tourner en arrière-plan, voir `ensure_steam_running`.
        ensure_steam_running()?;
        finish_bepinex_install_macos(profile_dir);

        let preloader = profile_dir
            .join("BepInEx")
            .join("core")
            .join("BepInEx.Preloader.dll");
        let doorstop = find_doorstop_dylib(profile_dir).ok_or_else(|| {
            "BepInEx introuvable (doorstop manquant) — relance une synchronisation.".to_string()
        })?;
        let macos_dir = install_dir.join("valheim.app").join("Contents/MacOS");
        let executable = macos_dir.join("valheim");

        // Sans passer par `steam.exe -applaunch` (impossible ici, voir plus haut),
        // SteamAPI_Init() exige normalement que le jeu ait été relancé par Steam lui-même
        // pour vérifier l'AppID — un `steam_appid.txt` contenant l'AppID, présent dans le
        // dossier de travail au démarrage, supprime cette exigence (mécanisme standard du
        // SDK Steamworks). Écrit une seule fois, jamais écrasé ensuite (best-effort).
        ensure_steam_appid_file(&macos_dir);

        // Script généré puis ouvert dans Terminal.app (processus indépendant de l'app
        // Tauri) : reproduit exactement l'invocation de macheim
        // (github.com/lofcgi/macheim), la seule approche macOS dont on ait la preuve
        // qu'elle fonctionne pour ce jeu. `arch -x86_64` force Rosetta même sur Apple
        // Silicon — macheim le fait inconditionnellement (pas de build arm64 native du
        // doorstop/BepInEx pour ce jeu). Le `cd` initial aligne le dossier de travail sur
        // celui du `steam_appid.txt` ci-dessus.
        let script = format!(
            "#!/bin/sh\ncd '{}' &&\narch -x86_64 env \\\n  DOORSTOP_ENABLED=1 \\\n  DOORSTOP_TARGET_ASSEMBLY='{}' \\\n  DYLD_LIBRARY_PATH='{}/' \\\n  DYLD_INSERT_LIBRARIES='{}' \\\n  '{}' -console\n",
            macos_dir.display(),
            preloader.display(),
            install_dir.display(),
            doorstop.display(),
            executable.display(),
        );

        let script_path = install_dir.join(".fedoheim-launch.sh");
        std::fs::write(&script_path, script).map_err(|e| e.to_string())?;

        use std::os::unix::fs::PermissionsExt;
        let mut perms = std::fs::metadata(&script_path)
            .map_err(|e| e.to_string())?
            .permissions();
        perms.set_mode(0o755);
        std::fs::set_permissions(&script_path, perms).map_err(|e| e.to_string())?;

        std::process::Command::new("open")
            .arg("-a")
            .arg("Terminal")
            .arg(&script_path)
            .spawn()
            .map_err(|e| format!("Could not launch Valheim: {e}"))?;
        return Ok(());
    }

    #[allow(unreachable_code)]
    Err("Unsupported platform".to_string())
}
