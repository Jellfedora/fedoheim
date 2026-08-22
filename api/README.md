# valheim-api

API centrale : auth Discord, manifests de modpacks, distribution des mods.
Voir `/CLAUDE.md` (racine du repo) pour l'architecture générale et le flow d'auth complet.

## Setup

1. `nvm use` (Node 22 requis, voir `.nvmrc`)
2. `npm install`
3. `cp .env.example .env` puis remplir :
   - **App OAuth2 Discord** (Discord Developer Portal → New Application → OAuth2) :
     `DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET`. Ajouter
     `http://127.0.0.1:38217/callback` dans les Redirect URIs.
   - **Bot Discord** (même app ou une dédiée → Bot → activer l'intent
     `Server Members Intent`, inviter le bot sur le serveur) : `DISCORD_BOT_TOKEN`.
   - `DISCORD_GUILD_ID` : ID du serveur Discord de la communauté.
   - `DISCORD_REQUIRED_ROLE_ID` : ID du rôle Discord requis pour utiliser le launcher.
   - `DISCORD_ADMIN_ROLE_ID` : ID du rôle Discord donnant les droits admin.
   - `JWT_SECRET` : chaîne aléatoire longue (ex: `openssl rand -hex 32`).
   - *(Optionnel)* `DISCORD_ANNOUNCEMENT_CHANNEL_ID` + `PUBLIC_API_URL` : si remplis,
     chaque annonce créée/éditée/supprimée dans le launcher est aussi postée/éditée/
     supprimée dans ce salon Discord (verrouillé en écriture pour les joueurs, réactions
     autorisées — le bot doit y avoir la permission "Envoyer des messages"). Sans eux,
     les annonces restent uniquement dans le launcher.
   - *(Optionnel)* `DISCORD_RULES_CHANNEL_ID` : si rempli, le règlement est affiché
     (et édité en place, pas reposté) dans ce salon à chaque modification depuis le
     launcher.
4. `npm run db:generate && npm run db:migrate` pour créer `data.sqlite`.
5. `npm run dev`

## Endpoints

- `GET /health` — ping.
- `POST /auth/discord/token` — `{ code, redirectUri }` → échange le code OAuth2 obtenu
  par le launcher, vérifie le rôle Discord requis, renvoie `{ token, user }`.
- `GET /auth/me` — infos de l'utilisateur courant (Bearer JWT requis).
- `GET /modpacks/:slug/manifest?mode=player|admin` (défaut `player`) — manifest d'un
  modpack (mods + config BepInEx, Bearer JWT requis). Un mod est une archive zip
  (`downloadUrl`/`sha256`), extraite telle quelle par le launcher. `mode=admin` inclut
  aussi les mods `adminOnly` et revérifie le rôle admin en direct sur Discord — voir
  `/CLAUDE.md` section Modpacks.
- `GET /modpacks/:slug/mods` (public) / `GET /modpacks/:slug/mods/full` (admin) /
  `PUT /modpacks/:slug/mods` (admin, remplacement complet) — CRUD de la liste de mods.
  Un mod avec `enabled: false` (case "Activé" décochée dans l'éditeur) est absent de la
  liste publique et du manifest (pour tout le monde, y compris `mode=admin`), mais reste
  dans `mods/full` pour pouvoir être réactivé sans perdre sa fiche.
- `GET /modpacks/:slug/bepinex` / `PUT /modpacks/:slug/bepinex` (admin) — config du
  package BepInEx du modpack (`{ url, sha256 }`), distincte des mods.
- `POST /modpacks/files` (admin, multipart, 200 Mo max) — upload générique d'une
  archive zip (mod ou package BepInEx), sha256 calculé côté serveur, renvoie
  `{ url, sha256 }`. Utilisé par le bouton "Choisir le dossier..." du launcher, qui
  zippe le dossier choisi avant l'upload.
- `GET /content/rules` — règlement (public). `PUT /content/rules` — remplace le
  règlement (`{ rules: string[] }`), réservé aux admins : Bearer JWT + rôle Discord
  `DISCORD_ADMIN_ROLE_ID` revérifié en direct à chaque appel. Répercute aussi le
  règlement sur Discord si `DISCORD_RULES_CHANNEL_ID` est configuré.
- `GET /content/faq` — FAQ (public).
- `GET /announcements` — annonces (public). `POST` / `PUT /announcements/:id` /
  `DELETE /announcements/:id` — réservés aux admins ; `POST`/`PUT` répercutent aussi
  l'annonce sur Discord si `DISCORD_ANNOUNCEMENT_CHANNEL_ID` est configuré.
- `POST /announcements/images` — upload d'une image (multipart, admin), servie ensuite
  via `/uploads/<fichier>`.
- `GET /modpacks/:slug/report-token` / `POST /modpacks/:slug/report-token/regenerate`
  (admin) — jeton partagé avec le mod serveur FedoServerTools, à recopier dans son
  `.cfg` (header `x-server-token`). `GET /modpacks` n'expose que `hasReportToken`
  (booléen), jamais la valeur.
- `POST /modpacks/online-players` — posté par FedoServerTools toutes les ~30s
  (`x-server-token` requis, pas de `:slug` : le jeton identifie déjà le profil de façon
  unique) : `{ players: { name: string, biome: string | null }[], online: boolean }` —
  `biome` est le nom brut de l'enum `Heightmap.Biome` côté jeu (ex: `"Meadows"`), `null`
  si le joueur a désactivé le partage de sa position. `GET
  /modpacks/:slug/online-players` (public) renvoie `{ online, players, updatedAt }` —
  état gardé en mémoire (pas en base), `online` repasse à `false` tout seul si aucun
  rapport n'arrive plus depuis 90s (serveur crashé) ou immédiatement si le dernier
  rapport reçu avait `online: false` (arrêt propre).
