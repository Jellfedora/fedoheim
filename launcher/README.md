# launcher

App desktop (Tauri + React) : login Discord, mise à jour du modpack, lancement du jeu.
Voir `/CLAUDE.md` (racine du repo) pour l'architecture générale et le flow d'auth complet.

## Installation (joueurs)

Builds disponibles sur la page [Releases](https://github.com/Jellfedora/fedoheim/releases)
du repo. Les builds ne sont pas signés (pas de compte Apple Developer / certificat
Windows) — premier lancement bloqué par défaut sur les deux OS :

- **macOS** : `.dmg` téléchargé → glisser l'app dans `Applications` (pas la lancer
  directement depuis le volume monté, en lecture seule) → macOS affiche "l'app est
  endommagée" (Gatekeeper, pas un vrai problème). Dans un terminal :
  ```bash
  xattr -cr "/Applications/Fedoheim Launcher.app"
  ```
  puis relancer l'app normalement.
- **Windows** : SmartScreen affiche "Windows a protégé votre ordinateur" au premier
  lancement de l'installeur → "Informations complémentaires" → "Exécuter quand même".

### Mise à jour automatique

Le launcher vérifie une nouvelle version disponible au démarrage (`tauri-plugin-updater`)
contre le `latest.json` de la dernière release GitHub **publiée** (pas les drafts) —
bandeau "Mettre à jour" en haut de l'écran si une version plus récente existe, sans
attendre ni bloquer le reste de l'app en cas d'échec (offline, pas encore de release
publiée...). Signé avec une clé Ed25519 propre à Tauri (gratuite, sans rapport avec un
certificat Apple/Windows) — la clé privée et son mot de passe vivent uniquement dans les
secrets GitHub Actions du repo (`TAURI_SIGNING_PRIVATE_KEY`/`_PASSWORD`), jamais commités.
**Si ces secrets sont perdus**, il faut régénérer une paire de clés
(`npx tauri signer generate`) et mettre à jour `pubkey` dans `tauri.conf.json` — les
installs existantes ne pourront plus vérifier les futures mises à jour tant qu'elles
n'auront pas réinstallé manuellement une version portant la nouvelle clé.

Ne fonctionne que pour les installs déjà buildées **avec** le plugin (0.0.2 et
suivantes) — les joueurs encore sur 0.0.1 doivent réinstaller une fois manuellement.

## Setup

1. `nvm use 22` (le launcher lui-même n'a pas besoin de Node en prod, mais le tooling
   Tauri/Vite si).
2. Rust stable installé via [rustup](https://rustup.rs).
3. `npm install`
4. Renseigner `src-tauri/src/config.rs` :
   - `DISCORD_CLIENT_ID` : même client_id que côté API (`api/.env` → `DISCORD_CLIENT_ID`).
   - Le port de callback `LOOPBACK_PORT` (38217 par défaut) doit être enregistré comme
     Redirect URI Discord : `http://127.0.0.1:38217/callback`.
5. `npm run tauri dev` — l'API doit tourner en parallèle (`cd ../api && npm run dev`),
   par défaut le launcher l'appelle sur `http://127.0.0.1:3000` (surchargeable via la
   variable d'env `VALHEIM_API_URL`).

## UI (`src/`)

Identité visuelle : gris Discord (`styles/tokens.css`) + accent cyan `#25d3e4`, wordmark en
Cinzel (typo à l'esthétique runique), corps de texte en Inter, chiffres/versions en
JetBrains Mono. Signature : feux follents ambiants (`components/ParticleField.tsx`) +
un wisp qui suit le curseur (`components/CursorWisp.tsx`), en écho aux wisps de Valheim.

Trois pages (Accueil / Mods / Règlement) via un state simple dans `App.tsx`, pas de
router — l'appli est trop petite pour le justifier. Une barre d'action persistante en
bas (statut modpack + bouton Jouer) reste visible sur toutes les pages.

**Données factices en attendant les vrais endpoints** (`src/data/mock.ts`) : joueurs en
ligne, dernière annonce, liste de mods, règlement, URL Buy Me a Coffee (placeholder à
remplacer par la vraie page). Le login/logout/sync/lancement, eux, sont déjà branchés
sur les vraies commandes Rust.

## Structure (`src-tauri/src`)

- `config.rs` — constantes (client id Discord, port loopback, URL de l'API).
- `auth.rs` — flow OAuth2 Discord : serveur loopback local + échange du code via l'API.
- `session.rs` — persistance du JWT de session sur disque (dossier de données de l'app).
- `valheim.rs` — détection du dossier d'install Valheim (Windows/macOS), dossier profil
  externe (BepInEx + mods, hors de l'install Steam), lancement via Steam avec arguments
  Doorstop (Windows).
- `modpack.rs` — packaging admin (zip d'un dossier + upload), synchronisation BepInEx et
  mods dans le profil (téléchargement/vérification/extraction), jeton de rapport
  FedoServerTools par profil (`fetch_report_token`/`regenerate_report_token`).
- `online.rs` — lecture publique de qui est en ligne (`fetch_online_players`), alimentée
  par le mod serveur FedoServerTools (voir `/mods/FedoServerTools`).
- `lib.rs` — commandes Tauri exposées au frontend React (`invoke(...)`).
