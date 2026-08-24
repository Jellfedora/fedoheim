# Fedoheim — Valheim Server Ecosystem

Écosystème complet pour "Fedoheim", un serveur Valheim communautaire : site web, launcher
desktop, API centrale, et mods maison. Développé pour la nouvelle saison.

Convention de nommage : "Fedoheim" est le nom de la communauté/marque (package API
`fedoheim-api`, identifiant Tauri `com.fedoheim.launcher`, `productName` "Fedoheim
Launcher"). "Valheim" reste utilisé uniquement pour désigner le jeu lui-même (curseur
`Valheim_Sword_Cursor.cur`, `valheim.exe`, commentaires sur l'ambiance du jeu) — ne pas
renommer ces occurrences-là, elles sont correctes telles quelles.

## Vue d'ensemble

- **`website/`** — Site web pour consulter les infos du serveur (statut, joueurs en ligne,
  changelog des mods, stats...). Pas encore démarré. **Quand il sera construit, il devra
  très probablement réutiliser le même `api/`** : mods, règlement, FAQ, annonces et
  settings sont déjà exposés en lecture publique (voir section Contenu ci-dessous), pas
  besoin de dupliquer cette logique côté site — surtout ne pas recréer une seconde source
  de vérité.
- **`launcher/`** — App desktop que les joueurs installent : connexion Discord, onboarding
  (règlement + SteamID), téléchargement/mise à jour automatique du modpack, lancement du
  jeu. Largement fonctionnel — voir `launcher/README.md` et la section Auth/Onboarding
  ci-dessous pour le flow complet.
- **`api/`** — Backend central (auth Discord, onboarding, manifests de modpacks, contenu
  admin-géré, repost Discord) qui sert de source de vérité pour le website + le launcher.
  Fonctionnel — voir `api/README.md`.
- **Mods maison** (`mods/`) — mods Valheim (BepInEx) développés en interne, distribués via
  l'API et installés automatiquement par le launcher. Six mods écrits et buildés
  (`HelloFedo`, `FedoDeath`, `FedoGoldRabbit`, `FedoGuardian`, `FedoDeathGif`,
  `FedoServerTools` — voir `mods/CLAUDE.md`; `FedoServerTools` a depuis peu absorbé
  deux fonctionnalités écrites à part puis fusionnées dedans (et supprimées en tant que
  mods séparés) plutôt que de multiplier les petits mods maison : le logging Discord de
  l'ancien mod `FedoDiscordLogs`, et la connexion automatique développée un temps sous
  le nom `FedoAutoJoin` (jamais commité en tant que mod séparé), packagés en zips dans
  `mods/dist/` prêts à être uploadés via l'éditeur du launcher ; aucun n'est encore
  effectivement configuré dans un profil de modpack en
  base.

## Stack technique (décisions actées)

| Composant | Choix | Pourquoi |
|---|---|---|
| API | Node + Fastify + TypeScript | Proche des habitudes Express du dev, mais typé et plus performant. |
| DB API | SQLite + Drizzle ORM | Léger, pas de serveur DB à admin pour un projet de cette taille, typesafe. |
| Auth | Discord OAuth2 (scope `identify`) + bot Discord | Voir section Auth ci-dessous. |
| Launcher (core) | Rust | Binaire léger, rapide, pas de runtime lourd à distribuer aux joueurs. |
| Launcher (UI) | Tauri + React + TypeScript | Webview système (pas de Chromium embarqué) + React pour l'UI. |
| Mods | BepInEx (mod loader standard Valheim), C# | Écosystème de mod Valheim standard. |

## Auth : Discord → rôle serveur

Le launcher n'a pas son propre système de comptes. Le flow est :

1. Le launcher démarre un petit serveur HTTP loopback local sur un port libre
   (ex: `http://127.0.0.1:<port>/callback`) — pas de deep link, plus fiable cross-OS.
2. Le joueur clique "Se connecter" → ouverture du navigateur système sur l'URL
   d'autorisation Discord OAuth2 (scope `identify` uniquement), avec ce loopback comme
   `redirect_uri` et un `state` anti-CSRF.
3. Discord redirige le navigateur vers le loopback local avec un `code`. Le launcher
   récupère ce `code`, ferme le loopback, et affiche une page "tu peux fermer cet onglet".
4. Le launcher envoie ce `code` (+ le `redirect_uri` exact utilisé) à l'API via
   `POST /auth/discord/token`. **Le `client_secret` Discord ne vit jamais dans le
   launcher distribué** — seule l'API le connaît et fait l'échange serveur à serveur.
5. L'API échange le `code` contre l'ID Discord de l'utilisateur, puis (via son **bot
   Discord**, token séparé de l'app OAuth2) interroge
   `GET /guilds/{guild.id}/members/{user.id}` pour lire les rôles du joueur sur le
   serveur Discord de la communauté.
6. Si le joueur possède le rôle autorisé (`DISCORD_REQUIRED_ROLE_ID`) **ou** le rôle
   admin (`DISCORD_ADMIN_ROLE_ID` — `hasRequiredRole` accepte l'un ou l'autre, voir
   `api/src/auth/discord.ts`) et n'est pas banni (`users.isBanned`, géré manuellement
   via `PATCH /admin/users/:discordId/ban` — pas encore d'UI dans le launcher pour ça),
   l'API émet un JWT de session (30 jours) renvoyé au launcher, qui le stocke et
   l'utilise pour tous les appels suivants. Un admin doit toujours pouvoir se connecter
   même s'il n'a pas cumulé le rôle de base sur Discord.
7. Sans le rôle requis ou si banni → l'API répond 403, pas de token. Le launcher
   traduit ça en un message clair invitant à demander le rôle à un admin Discord (pas
   le JSON brut de l'API) — `auth.rs` préfixe ce message avec `AUTH_WARNING:` (retiré
   avant affichage) pour que `App.tsx` l'affiche en ton neutre/orange plutôt qu'en
   rouge alarmant, même principe que le bandeau API injoignable ci-dessous : ce n'est
   pas un bug, juste une autorisation à obtenir.
8. Le rôle **admin** (`DISCORD_ADMIN_ROLE_ID`) est recalculé à chaque login/refresh et
   **revérifié en direct sur Discord à chaque action d'écriture admin** (`requireAdmin`
   dans `api/src/auth/plugin.ts`) — jamais fait confiance au JWT/DB seuls pour une action
   sensible, pour qu'une perte de rôle prenne effet immédiatement.
9. Le launcher revalide la session en tâche de fond (`GET /auth/me`, toutes les 5 min +
   immédiatement à l'ouverture) : rôle perdu, ban, ou règlement modifié depuis → déconnexion
   ou redemande de signature automatique, sans que le joueur ait à rien faire de son côté.
10. Indépendamment de la session, le launcher ping `GET /health` (public) toutes les 15s
    (`check_api_reachable`, avec un compte à rebours affiché) pour savoir si l'API est
    joignable du tout. Si elle ne l'est pas : bandeau persistant en haut de l'écran
    (ton volontairement léger, pas un rouge alarmant), et la navigation de la sidebar
    est verrouillée sur "Accueil" (seule page qui dégrade déjà proprement sans réseau —
    voir `HomePage.tsx`).
11. **`play` peut fonctionner hors ligne** si une installation existe déjà : à chaque
    synchronisation réussie, une copie du manifest est écrite dans le profil
    (`modpack::save_local_manifest`/`load_local_manifest`, pas juste "un dossier existe
    quelque part" — une vraie trace de ce qui a été installé). Si l'API est injoignable
    au moment de `play`, ce manifest local sert de repli : lancement direct avec
    l'installation existante (aucune sync/vérification de mise à jour dans ce cas),
    sinon l'erreur d'origine est remontée. Le bouton du bandeau du bas reflète cet état
    (`has_local_manifest` côté frontend) : "Télécharger" tant que rien n'a jamais été
    installé, "Jouer" ensuite ; désactivé uniquement si l'API est injoignable **et**
    qu'aucune installation locale n'existe (rien à télécharger, rien à lancer).

### Onboarding après le login (avant de pouvoir jouer)

Une fois connecté, le joueur doit, dans l'ordre : **1)** valider le règlement (`POST
/auth/accept-rules`) — **2)** renseigner son SteamID64 (`POST /auth/steam-id`, validation
de format uniquement, pas d'appel à l'API Steam). C'est seulement à ce moment que le
manifest de modpack devient accessible (`requireOnboarded` dans `auth/plugin.ts` protège
`GET /modpacks/:slug/manifest` — la vraie barrière de sécurité est là, le launcher ne fait
qu'afficher les écrans dans le bon ordre). Le bouton "Jouer" lui-même ne peut être bloqué
que côté UI (il lance `valheim.exe` en local, sans appel réseau).

Si un admin modifie le règlement, les joueurs qui l'avaient déjà signé doivent le
re-signer : `rules_meta.updatedAt` (une ligne singleton) est comparé à
`users.rulesAcceptedAt` pour calculer `hasAcceptedRules` dynamiquement — rien n'est
jamais réinitialisé en base, juste recalculé à la volée.

## Modpacks

- L'API expose un manifest de modpack (mods + config BepInEx, protégé par
  `requireOnboarded`) et une liste publique pour affichage (sans les URLs de
  téléchargement, `GET /modpacks/:slug/mods`).
- **Un mod = une archive zip**, pas un fichier unique : un mod réel a souvent plusieurs
  fichiers (dll + cfg + dépendances...). `downloadUrl`/`sha256` sur une ligne `mods`
  désignent ce zip, extrait tel quel par le launcher.
- **"+ Ajouter un mod" = un seul bouton** : il ouvre directement le sélecteur `.zip`
  (pas d'étape "carte vide" à remplir avant), uploade l'archive telle quelle à
  `POST /modpacks/files` (même mécanisme que `pick_and_upload_image` pour les
  annonces ; l'API calcule le sha256 côté serveur, jamais celui d'un client), et crée la
  fiche du mod déjà préremplie — nom/version/description quand l'archive a un
  manifest.json Thunderstore (voir point suivant), sinon laissés vides à compléter à la
  main. `version` est toujours celle de l'archive tout juste choisie ; `name`/
  `description` ne sont préremplis que si le champ est encore vide, pour ne jamais
  écraser une fiche déjà personnalisée (ex: description traduite en français) quand on
  reclique "Choisir le zip du mod" pour juste mettre à jour les fichiers d'un mod
  existant. L'admin pointe directement vers le zip téléchargé (Thunderstore ou autre),
  pas besoin de le dézipper/rezipper à la main.
- **Enveloppe Thunderstore gérée automatiquement** : un zip Thunderstore (mods comme
  BepInExPack_Valheim) contient souvent un sous-dossier d'enveloppe à côté de métadonnées
  (`manifest.json`, `README.md`, `icon.png`...) qui ne doivent pas atterrir dans l'install
  du jeu. Pour BepInEx, l'extraction se "re-root" automatiquement sur le dossier qui
  contient `BepInEx/` (voir `modpack.rs::find_zip_root`) et ignore tout le reste — pas
  besoin que l'admin sache où se trouve le vrai contenu dans l'archive.
- **Version BepInEx affichée automatiquement** : détectée depuis le `manifest.json`
  Thunderstore de l'archive au moment de l'upload (`version_number`), stockée en DB
  (`modpacks.bepinexVersion`) — juste pour l'affichage admin, jamais utilisée pour
  l'installation elle-même.
- **Avertissement de dépendance manquante** : `manifest.json` liste aussi `dependencies`
  (ex: `"denikson-BepInExPack_Valheim-5.4.2333"`), stocké tel quel
  (`mods.dependencies`, JSON). Dans l'éditeur, chaque dépendance d'un mod est comparée
  (nom de package normalisé, en ignorant auteur/version) aux mods déjà configurés dans
  le modpack, et à la config BepInEx si la dépendance en est une — si elle est absente,
  la carte du mod passe en bordure orange avec le nom de la dépendance manquante.
  **Bloque l'enregistrement** (même mécanique que les noms de mods dupliqués,
  `missingDepsByMod` dans `ModsPage.tsx`) tant que la dépendance n'a pas été ajoutée au
  modpack ou que le mod n'a pas été supprimé du brouillon.
- **Icône de mod détectée automatiquement** : `icon.png`, quasi systématique dans un
  package Thunderstore, extrait du zip et uploadé séparément à `POST /modpacks/icons`
  (même mécanique que `POST /announcements/images`, réservé aux admins) — best-effort,
  n'échoue jamais l'upload du mod si l'icône échoue. Affichée en miniature dans la liste
  publique des mods et dans l'éditeur. Piège vécu : un champ Rust `snake_case` sans
  `#[serde(rename = "camelCase")]` part tel quel en JSON — `icon_url` au lieu
  d'`iconUrl` attendu côté TS, silencieusement `undefined`, jamais d'erreur qui
  l'aurait signalé. Vérifier ce mapping à chaque nouveau champ ajouté à une struct
  échangée avec le frontend.
- **Noms de mods uniques, imposé** : deux mods avec le même nom casseraient le slug
  d'extraction côté launcher (même dossier `BepInEx/plugins/<slug>/` pour les deux) et
  la clé de liste React côté affichage public. Détecté dans l'éditeur (bordure orange,
  même code que les dépendances manquantes) et bloqué à l'enregistrement, pas juste un
  avertissement visuel.
- **Mod désactivable sans le supprimer** (`mods.enabled`, case à cocher "Activé" dans
  l'éditeur, `ModsPage.tsx`) : décoché, le mod disparaît du manifest (les deux modes,
  voir `GET /modpacks/:slug/manifest`) et de la liste publique — donc désinstallé chez
  tout le monde au prochain sync, `sync_mods` nettoyant déjà tout dossier absent du
  manifest reçu, aucun code dédié côté launcher — mais reste visible dans l'éditeur
  admin (bordure en pointillés) et dans la vue liste d'un admin (badge "Désactivé",
  assourdi) pour pouvoir le réactiver sans reperdre sa fiche (description, catégorie,
  dépendances, zip déjà importé). Distinct d'`adminOnly` : `enabled` filtre pour tout le
  monde y compris un admin, `adminOnly` ne filtre que pour un joueur normal.
- **Fichiers uploadés jamais utilisés, nettoyables** : un zip/une icône est uploadé à
  l'API dès la sélection (`pick_zip_and_upload`), *avant* que la fiche du mod soit
  enregistrée — si l'admin clique "Annuler", ces fichiers restent orphelins côté
  serveur sauf confirmation explicite de suppression (`DELETE /modpacks/files`, best
  effort, réservé aux admins). Changer de page ou fermer le launcher pendant une
  édition non enregistrée demande aussi confirmation (`ModsPage.onDirtyChange` remonté
  à `App.tsx`), mais ne déclenche pas ce nettoyage — seul le bouton "Annuler" le fait.
- **Catégorie = champ texte avec suggestions** (`<datalist>`), pas un `<select>` fermé :
  les catégories déjà utilisées dans le modpack sont proposées à la sélection, mais
  taper une nouvelle valeur reste possible et fait apparaître un nouvel onglet còté
  joueur — cohérent avec le principe "pas de liste figée" déjà en place.
- **`createdAt`/`updatedAt` par mod, admin seulement** (jamais dans `GET /mods` public,
  seulement dans `GET /mods/full`) : `createdAt` est préservé d'une sauvegarde à l'autre
  (la liste est remplacée en bloc à chaque `PUT`, donc matché par `name`, en pratique
  unique) ; `updatedAt` ne bouge que si `downloadUrl`/`sha256` changent réellement —
  "date du dernier zip importé", pas de la dernière modif de description/catégorie.
  Toujours calculés côté API, jamais acceptés depuis le body client.
- **BepInEx lui-même** est configuré une fois par modpack (pas par mod) via la même
  mécanique d'upload (`modpacks.bepinexUrl`/`bepinexSha256`/`bepinexVersion`) — l'admin
  pointe vers le zip du package officiel BepInExPack_Valheim tel que téléchargé depuis
  Thunderstore. Le bouton "Jouer" refuse de lancer le jeu tant que ce n'est pas
  configuré (bordure orange, `.mods-list__item--warning`/`.mods-editor__card--warning`,
  pour signaler l'état manquant). Affiché **au même niveau que les mods** — première
  carte de la liste/de l'éditeur (`ModsPage.tsx`), pas dans une section à part — cohérent
  avec "BepInEx est un mod comme un autre du point de vue du joueur" : visible seulement
  sur l'onglet "Tous" (pas de vraie catégorie), compté dans le total de mods affiché en
  haut de page.
- BepInEx et les mods vivent dans un dossier "profil" (`valheim::profile_dir`), dont
  l'emplacement et le mécanisme d'injection **diffèrent par plateforme** — asymétrie
  assumée, pas un manque :
  - **Windows** (cible principale) : profil externe (`app_data_dir()/gamedata/`), hors
    de l'install Steam du joueur (façon r2modman/Gale). Un mod s'extrait dans
    `<profil>/BepInEx/plugins/<slug-du-mod>/`. Possible parce que Doorstop (le
    mécanisme d'injection de BepInEx 5.x sur Mono) accepte de pointer vers un dossier
    BepInEx externe via des arguments de lancement
    (`--doorstop-target-assembly`/`--doorstop-target` selon `.doorstop_version`) — seul
    un petit fichier proxy (`winhttp.dll` pour BepInExPack_Valheim, à la racine du
    package) doit physiquement être à côté de `valheim.exe`, recopié automatiquement
    juste avant chaque lancement (détournement de l'ordre de recherche de DLL Windows,
    aucune alternative). Le jeu est lancé via `steam.exe -applaunch 892970 <args
    doorstop>`, pas en spawnant `valheim.exe` directement — `-applaunch` démarre Steam
    lui-même si besoin.
  - **macOS** (support secondaire) : le profil, c'est directement le dossier du jeu —
    pas de dossier externe ici, contrairement à Windows. Mécanisme repris de
    [macheim](https://github.com/lofcgi/macheim) (launcher Tauri équivalent, dont c'est
    l'approche réellement utilisée en prod pour ce jeu) : injection via
    `DYLD_INSERT_LIBRARIES`/`DYLD_LIBRARY_PATH` + `DOORSTOP_ENABLED`/
    `DOORSTOP_TARGET_ASSEMBLY`, `arch -x86_64` forcé (Rosetta, même sur Apple Silicon —
    pas de build arm64 native du doorstop pour ce jeu). Deux finitions post-extraction
    obligatoires : lever la quarantine Gatekeeper sur le `.dylib` doorstop téléchargé
    (sinon `dyld` refuse de le charger), et patcher `BepInEx.cfg` (`Type = Application`
    → `Type = GameObject`, requis pour BepInEx sur macOS/Unity). Le jeu est lancé via un
    script généré ouvert dans `Terminal.app` (`open -a Terminal ...`), pas via Steam —
    processus indépendant de l'app Tauri.
- Tous les fichiers d'une archive (dll comme `.cfg`) sont **resynchronisés à l'identique
  du serveur** dès que le sha256 change — pas de préservation d'une config locale
  modifiée par un joueur, la config livrée par l'admin fait autorité. Les dossiers de
  mods retirés du modpack sont nettoyés automatiquement au sync suivant.
- **Une seule action "Jouer"** (commande Rust `play`) enchaîne : sync BepInEx → sync
  mods (progression émise via l'event `sync-progress`, granularité par mod/étape) →
  lancement — **tant qu'aucune mise à jour n'est détectée**. Si le manifest servi par
  l'API diffère de la dernière install locale réussie (comparaison par sha256 de
  BepInEx + chaque mod — nom+sha256 de chaque entrée, donc un mod ajouté ou retiré change
  la longueur de la liste et compte comme une mise à jour au même titre qu'un sha256
  différent, pas seulement un fichier modifié — voir `check_update_available`/
  `modpack::manifest_needs_update`, revérifié à chaque fois que l'API redevient joignable
  **et** en continu toutes les `SESSION_REFRESH_INTERVAL_MS` (5 min, même cadence que la
  revalidation de session) tant que le launcher reste ouvert — sinon un ajout/retrait de
  mod pendant que l'API reste joignable en permanence ne serait jamais détecté), le
  bouton unique se scinde en
  deux : "Jouer" (`launch_only`, lance directement avec l'installation existante, sans
  resynchroniser) et "Mettre à jour" (`sync_modpack`, synchronise sans lancer) — le
  joueur choisit d'attendre la mise à jour ou de continuer avec ce qu'il a déjà, plutôt
  que de resynchroniser en silence à chaque clic sur "Jouer" une fois tout à jour.
- Les mods ont une catégorie libre (texte), affichée en onglets filtrables dans le
  launcher ("Tous" + une catégorie par valeur distincte trouvée) — un admin qui tape une
  nouvelle catégorie sur un mod fait apparaître un nouvel onglet, pas de liste figée.
- **Deux modpacks logiques, un seul modpack en base** : un mod peut être coché
  `adminOnly` dans l'éditeur (`mods.adminOnly`, case à cocher dans `ModsPage.tsx`) pour
  n'apparaître que dans le modpack "Admin" (mods communs + mods admin), jamais dans le
  modpack "Joueur" (mods communs seuls) ni dans la liste publique (`GET
  /modpacks/:slug/mods`, toujours filtrée — même pour un admin qui consulte hors
  édition, ces mods ne sont visibles que via "Éditer"). Le manifest
  (`GET /modpacks/:slug/manifest?mode=player|admin`, défaut `player`) applique le même
  filtre ; `mode=admin` revérifie le rôle admin **en direct sur Discord** (même
  mécanisme que `requireAdmin`, appelé à la main dans le handler puisque le préHandler
  est fixe) avant de servir la liste complète — un joueur qui changerait juste le
  paramètre d'URL ne récupère rien de plus sans le rôle.
- Quand un admin clique sur "Jouer"/"Télécharger"/"Mettre à jour"/"Réparer" (les seules
  actions qui resynchronisent, donc qui ont besoin d'un mode — pas "Jouer" en variante
  `launch_only`, qui lance l'installation existante telle quelle), une popup demande
  Joueur ou Admin **à chaque clic** (pas de mode mémorisé — voir
  `requestPrimaryAction` dans `App.tsx`) ; un joueur normal ne voit jamais cette popup,
  toujours en mode Joueur. Basculer de mode ne demande aucune logique de sync
  supplémentaire côté Rust : le nettoyage déjà en place dans `sync_mods` (qui retire
  tout dossier de mod absent du manifest reçu) ajoute/retire les mods admin comme un
  changement de modpack normal — seul le paramètre `mode` transmis à
  `fetch_manifest`/`play`/`sync_modpack`/`repair_modpack`/`check_update_available`
  change.
  - **Popup sautée si le profil actif n'a aucun mod `adminOnly` activé** (`hasAdminOnlyMods`
    dans `App.tsx`, rechargé via `fetch_mods_full` à chaque changement de profil actif,
    indépendamment de `ModsPage` qui peut ne pas être montée) : dans ce cas les modpacks
    "Joueur" et "Admin" sont strictement identiques (voir `resolveAutoConnect`/filtrage
    `adminOnly`+`enabled` ci-dessus), donc proposer un choix n'aurait aucun effet
    observable — `requestPrimaryAction` part directement en mode `"player"`, même pour un
    admin. Un mod `adminOnly` mais désactivé (`enabled: false`) ne compte pas non plus :
    déjà filtré du manifest des deux modpacks, donc sans différence lui non plus.

### Profils de modpack (test vs production)

Un admin peut créer plusieurs **profils de modpack** (table `modpacks`, une ligne par
profil) pour tester un modpack sur un serveur Valheim séparé avant de le répliquer sur
le vrai serveur — sans jamais risquer l'expérience des joueurs normaux :

- **Un seul profil est marqué `isDefault`** (le profil "Production", slug `default`,
  fixé côté launcher via `PRODUCTION_MODPACK_SLUG`) — c'est le seul que reçoit un joueur
  normal, jamais paramétrable pour lui. Les autres profils sont des profils de test créés
  librement par un admin (`POST /modpacks`), renommables (`PATCH /modpacks/:slug`, le nom
  seulement — le `slug` ne change jamais après création, il est référencé tel quel par les
  installs locales) et supprimables (`DELETE /modpacks/:slug`, avec leurs mods, en
  transaction) — sauf le profil `isDefault`, dont la suppression est refusée côté API
  (protection serveur, pas seulement une précaution côté launcher).
- Page "Profils" du launcher (`ProfilesPage.tsx`, item de sidebar admin-only) : liste des
  profils avec badge "Production"/"Profil actif", création, renommage, suppression, et
  sélection du **profil actif** pour la session en cours (état React dans `App.tsx`,
  jamais persisté sur disque — un launcher redémarré repart toujours sur "Production" par
  sécurité, pour qu'un admin ne se retrouve jamais bloqué sur un profil de test sans s'en
  rendre compte).
- **Aucune installation locale parallèle par profil** : Windows n'a qu'un seul dossier
  `gamedata` externe, et macOS n'a même pas de notion de profil externe (le "profil" y est
  le dossier du jeu lui-même, voir plus haut) — impossible d'avoir deux installs
  simultanées de façon fiable cross-platform. Changer de profil actif ne fait donc que
  changer quel `slug` est visé par `play`/`sync_modpack`/`repair_modpack`/`check_
  update_available` et par l'éditeur de mods (`ModsPage`) ; le mécanisme de mise à jour
  déjà en place (comparaison par sha256, nettoyage des mods retirés) prend en charge la
  resynchronisation complète vers le nouveau profil sans code dédié — au prix d'une
  resynchronisation à chaque bascule test ↔ production, accepté comme compromis.
- Un joueur normal ne voit jamais la page "Profils" ni aucune notion de choix de profil —
  `effectiveModpackSlug` (`App.tsx`) retombe toujours sur `PRODUCTION_MODPACK_SLUG` dès
  que `user.isAdmin` est faux, y compris si le rôle admin est perdu en cours de session.
- **Couleur par profil** (`modpacks.color`, hex `#rrggbb` nullable) : un admin choisit une
  couleur par `<input type="color">` dans la page "Profils", jamais pour le profil
  production (le sélecteur n'y est simplement pas affiché). `PATCH /modpacks/:slug`
  accepte `name`/`color` indépendamment (`null` explicite pour réinitialiser, distinct
  d'absent qui laisse le champ inchangé). Purement cosmétique, aucune influence sur la
  sync/l'install.
  - **Reteinte tout le launcher, pas juste un badge** : tant qu'un profil de test coloré
    est actif, `App.tsx` surcharge `--accent`/`--accent-soft`/`--accent-strong` (voir
    `tokens.css`) via un `style` inline sur le conteneur racine `.shell` — tout ce qui
    utilise déjà ces variables (boutons accent, onglet actif de la sidebar, badges Admin/
    Mode admin/Profil, focus, sélection de texte, particules de fond) suit automatiquement
    par héritage CSS, sans style manuel par élément. Retombe sur l'accent Fedoheim par
    défaut dès qu'on revient sur "Production" ou que le profil de test n'a pas de couleur
    choisie (`activeProfileColor` à `null` dans ce cas). But explicite : qu'un admin ne
    puisse jamais confondre visuellement un profil de test avec la production.
  - La page "Profils" (qui liste tous les profils à la fois, pas seulement l'actif) garde
    en plus une teinte par carte indépendante de ce thème global, pour distinguer
    plusieurs profils de test de couleurs différentes affichés côte à côte.
- **Copie d'un modpack d'un profil à l'autre** (page "Profils", section dédiée) : source
  et destination choisies librement (les deux sens sont possibles, ex: profil de test →
  production ou l'inverse), avec un récapitulatif (mods ajoutés/supprimés/mis à jour +
  BepInEx) avant toute écriture. Remplace en bloc mods + BepInEx de la destination —
  aucune route API dédiée, juste `fetch_mods_full`/`fetch_bepinex`/`save_mods`/
  `save_bepinex` déjà existants appelés sur les deux slugs.

## Joueurs en ligne (FedoServerTools)

Le mod maison `mods/FedoServerTools` (server-side, pas de mécanique de jeu) tourne sur le
serveur Valheim lui-même (dédié, ou l'hôte d'une partie solo/co-op) et parle à l'API
toutes les `SyncIntervalSeconds` (30s par défaut, renommé depuis `ReportIntervalSeconds`
— ce mod est prévu pour devenir le canal général avec l'API, pas juste un rapporteur de
joueurs : à terme l'API pourra aussi demander des choses au jeu, ex. déclencher un
événement — toujours en sondage depuis le mod, jamais l'inverse, le jeu n'exposant
aucun serveur qu'on pourrait interroger de l'extérieur). Aujourd'hui ça se limite à
poster la liste des joueurs connectés à `POST /modpacks/online-players`, authentifié
par un jeton
partagé (`modpacks.reportToken`, header `x-server-token`) plutôt qu'une session Discord
— ce mod n'a pas d'identité joueur. La liste elle-même vient de `ZNet.GetPlayerList()`
(l'API du jeu utilisée par son propre panneau "joueurs"), pas d'un suivi maison des
connexions/déconnexions — elle inclut donc l'hôte en partie solo, pas seulement les
pairs distants. Chaque entrée est `{ name, biome, armor }` : `armor` vient de
`Humanoid.GetBodyArmor()` (arrondi), lu sur l'instance `Player` correspondante
(retrouvée via `Player.GetAllPlayers()`, indexé par nom — `null` si introuvable côté
serveur au moment du rapport).

- **Jeton par profil, pas global, et pas de slug à configurer côté mod** : chaque profil
  de modpack (voir section Modpacks) a son propre `reportToken`, généré/régénéré par un
  admin depuis la page "Profils" (`GET`/`POST /modpacks/:slug/report-token[/regenerate]`,
  admin uniquement). Le jeton identifiant déjà le profil de façon unique, `POST
  /modpacks/online-players` ne prend pas de `:slug` — l'API retrouve le profil en
  comparant le jeton reçu à chaque `reportToken` en base (`findModpackByToken`, en temps
  constant par ligne), pas en le demandant en plus au `.cfg` du mod. Seul `GET
  /modpacks/:slug/online-players` (lu par le launcher, sans jeton) reste scopé par slug.
  `GET /modpacks` n'expose jamais la valeur du jeton, seulement `hasReportToken` — la
  valeur en clair n'est révélée qu'à la demande explicite d'un admin, pour la recopier
  dans le `.cfg` de FedoServerTools sur le serveur Valheim concerné. Régénérer invalide
  immédiatement l'ancien jeton (le mod continue de reporter avec jusqu'à mise à jour de
  son `.cfg`, ses rapports étant alors rejetés entre-temps).
- **`ServerToken` ne doit jamais partir rempli dans le modpack des joueurs** — voir
  README de FedoServerTools. Le `.cfg` (donc le jeton) est resynchronisé à l'identique
  chez tout le monde dès qu'un mod fait partie du modpack ; un joueur hébergeant sa
  propre partie solo/coop devient un serveur lui aussi (`ZNet.IsServer()` vrai chez lui),
  et posterait alors sous le jeton de la vraie communauté, polluant "qui est en ligne"
  avec sa session privée. `ServerToken` vide (le défaut) rend le rapport inoffensif
  (juste sauté, avec un avertissement en log local) — seule la copie installée à la main
  sur le vrai serveur dédié doit avoir la valeur réelle. Reste vrai même si ce mod
  finissait un jour dans le modpack joueur pour une fonctionnalité côté client qui n'a,
  elle, besoin d'aucun secret (ex: forcer sa propre "Position publique" depuis le client
  lui-même, si le forçage serveur seul ne suffit pas — pas encore vérifié).
- **État en mémoire côté API, pas en base** (`modpacks/onlinePlayers.ts`) : "qui est en
  ligne maintenant" n'a pas besoin de survivre à un redémarrage de l'API, le prochain
  rapport du mod (au plus ~30s après) reconstruit l'état tout seul — pas de migration
  dédiée pour ça.
- **Biome de chaque joueur, résolution à deux niveaux, nom configurable dans le `.cfg`
  du mod** : `Heightmap.FindBiome(player.m_position)` en priorité — le biome réellement
  affiché au joueur (post-lissage des bordures de la zone déjà chargée) — mais elle
  renvoie silencieusement `None` si cette zone n'est pas chargée en mémoire à cet
  instant (observé en pratique même avec une position publique et valide). Secours sur
  `WorldGenerator.instance.GetBiome(player.m_position)` (calcul procédural brut, celui
  qui a servi à générer le terrain) dans ce cas — toujours disponible, mais peut se
  tromper près d'une côte (observé : une bordure de plage en Forêt Noire classée Océan
  avant lissage). `None` est filtré côté mod dans tous les cas (jamais envoyé comme
  texte). Le résultat est ensuite traduit en texte final par le mod lui-même, via un
  `ConfigEntry<string>` par biome (`fedo.servertools.cfg`, section `[Biomes]`, ex:
  `MeadowsName`), par défaut en anglais (`Meadows`, `Black Forest`...) — même convention
  que les autres textes affichés au joueur dans ce repo (voir `mods/CLAUDE.md`) : éditer
  le `.cfg` généré pour y mettre sa propre traduction (ex: `MeadowsName = Prairies`).
  L'API et le launcher affichent cette valeur telle quelle, sans mapping ni connaissance
  des biomes du jeu.
- **`ForcePublicPosition` (`.cfg`, section `[Players]`, activé par défaut) force le vrai
  réglage, des deux côtés à la fois** : la position d'un joueur n'est normalement
  exploitable que s'il a lui-même activé "Position publique" (Options > Jeu, décoché par
  défaut chez à peu près tout le monde). Ce mod force ce réglage pour de vrai (le joueur
  apparaît sur la carte des autres, pas seulement un effet interne à ce mod), en
  cumulant deux mécanismes complémentaires :
  - **Côté serveur** : écrit directement `ZNetPeer.m_publicRefPos` (champ public,
    canonique) à `true` pour chaque pair connecté, à chaque cycle (`GetConnectedPlayers`)
    — jamais en réécrivant le retour d'une méthode partagée. Une première version
    patchait `ZNet.GetPlayerList()` en Harmony pour forcer `PlayerInfo.m_publicPosition`
    dans sa liste de retour — risque réel de corrompre une liste utilisée par d'autres
    systèmes du jeu (une boucle de `NullReferenceException` dans `ZNetScene.
    CreateDestroyObjects`/`RemoveObjects` a été observée avec cette approche) ; retirée.
  - **Côté client** (`ForceOwnPublicPosition`, vérifié à chaque frame depuis `Update()`
    — pas de check `IsServer()`, nécessaire aussi pour l'hôte d'une partie
    solo/hébergée) : simule un vrai clic sur la case en jeu (`Minimap.instance.
    OnTogglePublicPosition()`, méthode publique) pour passer par le chemin normal du jeu
    plutôt que d'espérer que l'écriture côté serveur seule suffise à déclencher la
    diffusion aux autres clients — sans certitude à ce sujet faute de pouvoir décompiler.
    Une première version patchait `Game.Start()` en Harmony (une seule fois) au lieu de
    vérifier à chaque frame — observé en pratique : la case restait jamais forcée, rien
    ne garantissant que `Minimap.instance` existe déjà au moment précis où un objet Unity
    donné exécute son `Start()`/`Update()` par rapport aux autres dans la même frame ;
    corrigé. C'est le seul morceau de ce mod qui a un effet côté client et qui ne
    nécessite aucun jeton — voir plus haut, sans danger à distribuer dans le modpack
    joueur.
  **Synchronisé et verrouillé via ServerSync** (`mods/_shared/ConfigSync.cs`,
  `ConfigSync.IsLocked = true`) : seul le `.cfg` du serveur (source de vérité, `IsAdmin`
  toujours vrai côté serveur) contrôle ce réglage — un joueur ne peut plus le désactiver
  en éditant son propre `.cfg` local, sa valeur y est écrasée dès la connexion. Voir
  section "ServerSync" plus bas.
- **`status` (`starting`/`online`/`stopping`/`offline`) reflète le cycle de vie complet
  du serveur**, pas juste en-ligne/hors-ligne :
  - `starting` est envoyé dès le chargement du plugin (`Awake`), **avant même de savoir**
    si cette instance sera serveur ou client — inoffensif sur un client normal
    (`ServerToken` y est vide par convention, le rapport est juste sauté). C'est la seule
    fenêtre qui couvre le temps de chargement de BepInEx/des mods, potentiellement long
    sur un serveur qui en a beaucoup — sans ce rapport précoce, le launcher n'aurait
    aucune donnée du tout pendant tout ce temps (donc "hors ligne", trompeur). Les
    rapports périodiques normaux continuent de dire `starting` tant que
    `StartingGracePeriodSeconds` (60s par défaut, configurable) ne s'est pas écoulé
    depuis le chargement du plugin, puis basculent sur `online`.
  - `stopping` est envoyé une première fois dès `OnApplicationQuit` (best-effort,
    fire-and-forget — le process a encore un peu de temps à ce moment), puis une
    dernière fois juste avant la destruction de `ZNet` (`ZNet.OnDestroy`) —
    **celui-ci attendu de façon bloquante** (borné par un timeout HTTP court côté mod),
    pas fire-and-forget : une version fire-and-forget se faisait tuer par la fin du
    process juste après `OnDestroy`, y compris sur un arrêt propre du jeu (pas
    seulement un crash), laissant le launcher afficher "en ligne" jusqu'à péremption
    (~90s plus tard) au lieu d'un passage immédiat à un statut de fermeture.
  - `offline` n'est **jamais envoyé par le mod** — c'est ce que l'API renvoie elle-même
    (`onlinePlayers.ts`) dès que plus aucun rapport frais n'est disponible (péremption à
    90s, 3x l'intervalle de synchronisation) : le filet de sécurité pour un vrai crash,
    où même `OnApplicationQuit` n'a pas eu l'occasion de se déclencher.
  `online` (booléen, `status === "online"`) reste exposé en plus par
  `GET /modpacks/:slug/online-players` (public, comme `/health`) pour un usage qui n'a
  besoin que de ça.
- **`GET /modpacks/:slug/online-players` public, pas de session requise** : cohérent avec
  le reste du contenu en lecture publique (règlement/FAQ/annonces/statut BepInEx), et
  nécessaire puisque ce sera à terme un mod *serveur* qui alimente cette donnée, pas un
  joueur connecté. `HomePage.tsx` l'interroge au montage puis toutes les 10s (le
  minimum autorisé pour `SyncIntervalSeconds` côté mod — pas la peine d'ajouter un
  délai d'affichage au-dessus du rythme le plus rapide possible côté rapport), scopé
  sur `effectiveModpackSlug` (donc la production pour un joueur normal, le profil actif
  pour un admin en train d'en tester un autre) — voir App.tsx.
- **Saison en cours (mod tiers Seasons), dépendance douce détectée à l'exécution** :
  chaque rapport inclut aussi la saison actuelle (`Spring`/`Summer`/`Fall`/`Winter`,
  traduite dans `fedo.servertools.cfg` section `[Seasons]`, même principe que les noms
  de biome) si le mod [shudnal/Seasons](https://thunderstore.io/c/valheim/p/shudnal/Seasons/)
  fait aussi partie du modpack — `null`/absent sinon, sans erreur.
  `SeasonReporting.cs` isole tout usage de l'API de Seasons dans un fichier dédié : le
  garde `IsLoaded` (vérifie juste la présence du plugin dans
  `BepInEx.Bootstrap.Chainloader.PluginInfos`, jamais un type `Seasons.*`) doit toujours
  être vérifié avant tout appel touchant réellement son API — le CLR ne résout le corps
  d'une méthode (donc les types qu'il référence) qu'à sa première exécution, jamais à la
  simple présence de sa signature dans l'assembly, donc ce découpage suffit à ce que
  FedoServerTools reste chargeable même si Seasons.dll est absent du serveur. Contrairement
  à biome/armor, une seule valeur par rapport (pas par joueur) : la saison est un état du
  monde, pas du joueur. `manifest.json` liste quand même `shudnal-Seasons-1.8.2` dans ses
  `dependencies` — pas pour en faire une dépendance dure au sens BepInEx (le garde
  `IsLoaded` ci-dessus reste nécessaire, le mod continue de charger sans elle), mais pour
  que l'éditeur de modpack du launcher avertisse (bordure orange, même mécanique que les
  dépendances manquantes classiques) si un admin configure FedoServerTools sans avoir
  aussi ajouté Seasons — sinon la saison resterait silencieusement absente des rapports
  sans qu'on comprenne pourquoi. **Exposée par `GET /modpacks/:slug/online-players` seulement si
  `status === "online"`**, comme `players` — sinon un serveur en cours d'arrêt (ou
  fermé depuis peu, tant que le dernier rapport n'est pas encore périmé) afficherait
  encore une saison alors que le jeu n'est plus joignable.

## Connexion automatique (FedoServerTools)

Partie client de `FedoServerTools` (`AutoConnect.cs`/`FejdStartupPatches.cs`/
`SessionFile.cs` — le seul patch du mod qui touche l'écran-titre `FejdStartup`, pas le
gameplay ; développée un temps comme un mod séparé `FedoAutoJoin`, jamais commis en
tant que tel, fusionnée dedans avant le premier commit pour ne pas multiplier les
petits mods maison — même raisonnement que l'absorption de `FedoDiscordLogs`, voir
Vue d'ensemble) qui saute le menu principal de Valheim quand un profil de modpack a une
**cible d'auto-connexion** configurée (page "Profils" du launcher, admin seulement,
section "Connexion auto") : soit un monde local à héberger (`autoConnectType: "world"`,
champ texte libre comparé **insensible à la casse** aux mondes locaux, `AutoConnect.
ConnectToWorld` — pas un sélecteur sur la vraie liste, une casse légèrement différente du
nom réel a été observée en conditions réelles), soit un serveur dédié à rejoindre
(`"server"`, host/port/mot de passe optionnel). `null`/absent = kill-switch,
comportement 100% vanilla — c'est ce qui permet de n'activer la fonctionnalité que sur
un profil de test (ex. `fedodev3`) sans toucher à la production tant qu'elle n'est pas
validée en conditions réelles.

- **Le nom de personnage est celui imposé (pseudo Discord), pas un choix libre du
  joueur** — voir ci-dessous ; ce choix **devient définitif pour ce compte** :
  `users.characterName` (API) est posé une seule fois, "premier arrivé, premier servi",
  dès que `FedoServerTools` rapporte un `steamId` connecté correspondant à un compte dont
  `characterName` est encore `null` (voir `onlinePlayers.ts::linkCharacterName` et
  `mods/FedoServerTools/PeerSteamId.cs`). Pas de contrainte UNIQUE en base ; l'unicité
  est vérifiée en code avant d'assigner.
  - **`PeerSteamId.Resolve` distingue deux cas** : un pair distant (`ZNet.
    GetPeerByPlayerName(name).m_socket.GetHostName()` — méthode/champs publics, vérifiés
    par reflection dump contre le vrai `assembly_valheim.dll`) ; ou l'hôte lui-même (nom
    comparé à `Game.instance.GetPlayerProfile().GetName()`), qui n'a **aucun `ZNetPeer`
    le représentant** — confirmé par désassemblage IL de `ZNet.UpdatePlayerList()`, qui
    ajoute l'entrée de l'hôte à `m_players` directement depuis son propre profil, jamais
    via `m_peers` (rempli uniquement par `OnNewConnection`, donc seulement pour de
    vraies connexions entrantes). Sans ce second cas, un admin qui héberge et joue
    lui-même (le scénario de test le plus courant, ex. `fedodev3`) ne voyait **jamais**
    son propre perso lié à son compte, peu importe combien de fois il se reconnectait —
    trouvé et corrigé après un vrai test en jeu. Le SteamID64 de l'hôte est alors lu
    directement via la couche plateforme (`Splatform.PlatformManager` — singleton
    accessible seulement par un champ statique privé, `s_distributionPlatform`, seul
    point de reflection nécessaire ici — puis
    `IDistributionPlatform.LocalUser.PlatformUserID.TryParseAsUInt64`, tous publics,
    référencés directement dans `FedoServerTools.csproj`).
  - **Protection contre l'usurpation d'un nom déjà lié** (`CharacterOwnershipPatch.cs`,
    `POST /modpacks/character-check`) : à la connexion d'un pair distant (jamais l'hôte
    lui-même, même distinction que ci-dessus), le mod demande à l'API si ce nom de
    perso appartient déjà à un AUTRE compte que le SteamID qui se connecte — si oui,
    `ZNet.Kick(peer.m_playerName)` (méthode publique, même mécanisme que `/kick` en
    console admin) l'éjecte immédiatement. Un nom pas encore lié à personne reste
    toujours autorisé (cette route ne fait que bloquer un vol d'identité, jamais la
    première liaison elle-même). Volontairement bloquant (comme `ReportBlocking`, voir
    plus haut) : un Postfix Harmony ne peut pas être async, et la décision doit être
    connue avant de laisser la connexion continuer — timeout court (3s) car ça bloque
    le thread principal du serveur (donc tout le monde), pas seulement le joueur qui se
    connecte ; échoue toujours "ouvert" (autorisé) sur erreur/timeout API, jamais
    "fermé", pour qu'un souci réseau ne verrouille jamais un joueur légitime dehors.
    `ServerToken` vide désactive ce contrôle, même logique que le reporting.
- Sans `characterName` lié → `FejdStartup` patché en Postfix de `Start()` saute
  direct à l'écran de création, nom **imposé au pseudo Discord du joueur** (pas de choix
  libre du tout — champ pré-rempli puis verrouillé en lecture seule, `readOnly` sur le
  `TMP_InputField`), `PlayerProfile.HaveProfile` suffixé d'un nombre croissant —
  `Nom2`, `Nom3`... — si déjà pris localement (voir `ResolvePrefillName`/
  `PrefillCharacterName` dans `FejdStartupPatches.cs`) ; une fois
  "Terminé" cliqué (`OnNewCharacterDone`, patché en Postfix), connexion automatique à la
  cible configurée. Avec un `characterName` déjà lié et un `.fch` local correspondant →
  aucun menu du tout, le perso est sélectionné et la connexion se lance immédiatement.
- Le launcher écrit, juste avant chaque lancement (`play`/`launch_only` dans `lib.rs`),
  un fichier plat `<profil>/BepInEx/fedoheim-session.txt` (format `clé=valeur`, pas du
  JSON — aucun mod de ce repo n'a de dépendance de parsing JSON) contenant
  `character_name` + `discord_username` (pré-remplissage seulement, voir ci-dessus) + la
  cible résolue — jamais synchronisé/zippé comme le contenu d'un mod, même logique que
  `ServerToken` de `FedoServerTools`. `GET /modpacks/:slug/manifest` expose la cible
  résolue (`autoConnect`) à **tout** joueur onboardé, pas seulement un admin : chacun
  doit pouvoir lire la cible de son propre profil actif pour que l'auto-connexion
  fonctionne chez lui.
  - **`state.session` rafraîchi juste avant l'écriture de ce fichier** (`play`/
    `launch_only` appellent maintenant `refresh_session_inner` avant `write_mod_session`,
    voir `lib.rs`) — trouvé et corrigé après un vrai test en jeu : le process Valheim est
    lancé en fire-and-forget (`valheim::launch`, jamais attendu), donc rien ne
    rafraîchissait `characterName` entre deux lancements rapprochés autrement que le
    timer de 5 min côté frontend (`SESSION_REFRESH_INTERVAL_MS`). Sans ce
    rafraîchissement, un `characterName` tout juste lié côté API (quelques secondes plus
    tôt) restait invisible du launcher, qui écrivait alors un `character_name` vide et
    faisait recréer un nouveau personnage à chaque reconnexion rapide au lieu de
    retrouver celui déjà lié. Best-effort : une erreur ne bloque jamais le lancement,
    juste un retour au cache existant.
- **Écran de chargement Fedoheim** (`LoadingOverlay.cs`) : comme aucun panneau de menu
  n'est affiché une fois l'auto-connexion enclenchée, l'écran de chargement vanilla
  (qui se déclenche via ces mêmes panneaux) ne l'est pas non plus — un écran noir sans
  aucun texte le temps de la connexion/du chargement du monde, facilement pris pour un
  plantage. Un texte "Chargement de Fedoheim..." est affiché en haut de l'écran dès
  qu'on saute vers la connexion (perso déjà lié, ou juste après en avoir créé un
  nouveau), masqué dès que le HUD apparaît vraiment (`Hud.Awake`) — ou après 30s dans
  tous les cas, filet de sécurité si la connexion échoue et que le HUD n'apparaît
  jamais.
- Page "Joueurs" du launcher : l'avatar Discord du compte lié est affiché à côté du nom
  de perso (`GET /modpacks/:slug/player-stats`, jointure par `characterName`) — `null`
  pour un perso vu avant l'existence de cette fonctionnalité ou jamais lié.
- **Testée en conditions réelles, quatre bugs trouvés et corrigés jusqu'ici** :
  1. Le menu principal (`m_mainMenu`) restait affiché en dessous de l'écran de création
     de perso — `ShowCharacterSelection()` seule ne le masque jamais. Corrigé en
     appelant plutôt `OnStartGame()` (le vrai handler public du bouton "Lancer une
     partie", confirmé par désassemblage IL : il fait `m_mainMenu.SetActive(false)`
     avant d'appeler `ShowCharacterSelection()`).
  2. Après création d'un nouveau perso (compte pas encore lié), la connexion ne se
     déclenchait jamais : `AutoConnect.Connect` tournait avant `OnCharacterStart()`
     (qui peuple `m_worlds` via `ShowStartGame()` et appelle `Game.SetProfile(...)`),
     donc héberger un monde local échouait silencieusement ("could not read the local
     world list") et le joueur restait bloqué sur l'écran de sélection de perso.
     Corrigé en appelant `OnCharacterStart()` avant `AutoConnect.Connect`, comme le
     fait déjà le chemin "perso déjà lié" — voir les commentaires de tête de
     `FejdStartupAutoNavigatePatch`/`FejdStartupNewCharacterDonePatch`.
  3. Le perso d'un admin hébergeant lui-même (host solo/co-op, cas de test le plus
     courant) ne se liait jamais à son compte, quel que soit le nombre de reconnexions —
     voir le point "PeerSteamId.Resolve distingue deux cas" plus haut pour le détail
     (l'hôte n'a pas de `ZNetPeer`, donc `GetPeerByPlayerName` ne le trouve jamais).
     Corrigé en résolvant le SteamID64 de l'hôte via la couche plateforme
     (`Splatform`) plutôt que par pair.
  4. Même après une liaison réussie, une reconnexion rapide recréait quand même un
     nouveau personnage (`character_name` vide dans `fedoheim-session.txt`) — voir le
     point "`state.session` rafraîchi juste avant l'écriture de ce fichier" plus haut :
     `state.session` (launcher) pouvait rester périmé de plusieurs minutes, le process
     Valheim étant lancé en fire-and-forget, jamais attendu. Corrigé côté launcher
     (`refresh_session_inner` appelé avant `write_mod_session`), pas dans ce mod.

  **Reste à revalider avec ces quatre correctifs** : l'enchaînement complet jusqu'à une
  vraie connexion (host local ou serveur dédié) suivie d'une liaison réussie du compte
  n'a pas encore été observé de bout en bout. Les champs privés `m_profiles`/`m_worlds`/
  `<ServerPassword>k__BackingField` et le point d'accroche `OnWorldStart`/
  `SetServerToJoin`+`JoinServer` restent vérifiés seulement par dump de reflection contre
  le vrai assembly (signatures correctes) avant de considérer cette partie
  de `FedoServerTools` fiable, même principe que les autres patchs `FejdStartup`/`ZNet`
  de ce repo (voir `mods/CLAUDE.md`, "Notes techniques de modding").

## ServerSync (synchronisation de config entre serveur et clients)

`mods/_shared/ConfigSync.cs` — librairie communautaire standard du modding Valheim
(blaxxun-boop, très largement réutilisée dans l'écosystème BepInEx/Thunderstore),
intégrée telle quelle en fichier source partagé (`<Compile Include="../_shared/
ConfigSync.cs" />` dans le `.csproj` de chaque mod qui l'utilise, pas un DLL séparé —
voir `mods/_shared/README.md` pour la provenance/version exacte). Elle patche elle-même
`ZNet`/`ZRpc` en Harmony pour ajouter sa propre RPC de poignée de main : à la connexion
d'un client, le serveur lui pousse la valeur actuelle de chaque `ConfigEntry` enregistré
via `configSync.AddConfigEntry(...)`, appliquée en mémoire côté client (jamais écrite
dans son `.cfg` local). Avec `configSync.IsLocked = true`, un client connecté ne peut
plus du tout modifier localement un réglage enregistré — seul le serveur (source de
vérité, `ConfigSync.IsAdmin` toujours vrai côté serveur) fait autorité.

- **Ne jamais enregistrer un secret** (`ServerToken` de FedoServerTools par exemple) :
  `AddConfigEntry` diffuse la valeur à tous les clients connectés dès qu'elle change —
  l'inverse exact de ce qu'on veut pour un jeton.
- Dépendance NuGet nécessaire dans chaque `.csproj` : **aucune** — `PublicAPIAttribute`/
  `UsedImplicitlyAttribute` (utilisés par `[PublicAPI]` dans `ConfigSync.cs`) sont déjà
  fournis par `UnityEngine.CoreModule` (déjà référencé par tous les mods) ; ajouter le
  package NuGet `JetBrains.Annotations` provoque un conflit de type (`CS0433`). Le
  fichier a aussi besoin de références déjà présentes dans certains mods mais pas tous :
  `assembly_utils`, `UnityEngine.UI`, `Unity.TextMeshPro` — à ajouter au `.csproj` de
  chaque mod qui l'intègre s'il ne les a pas déjà.
- Utilisé aujourd'hui uniquement par `FedoServerTools` (`ForcePublicPosition`, voir
  section précédente). Objectif à terme : l'étendre aux réglages de gameplay des autres
  mods maison (portée de détection, taux de spawn...) pour que tous les joueurs d'un
  même modpack jouent avec les mêmes valeurs, peu importe ce que chacun a dans son
  `.cfg` local.

## Contenu géré par les admins

Règlement, FAQ, mods et annonces sont tous en base (Drizzle/SQLite), avec le même
principe : **lecture publique** (pas de login requis — un joueur doit pouvoir lire le
règlement avant même de se connecter), **écriture réservée aux admins** via
`requireAdmin`. Chaque type a sa page dans le launcher avec un mode édition visible
seulement si `isAdmin` :

- **Règlement** (`rules` + `rules_meta`) — remplace toute la liste en un `PUT`. Éditable
  aussi sur Discord (voir ci-dessous), édité en place à chaque changement.
- **FAQ** (`faq_entries`) — même principe de remplacement complet.
- **Mods** (`mods`) — CRUD complet (nom, version, chemin d'install, URL, sha256,
  description, catégorie). `GET /modpacks/:slug/mods/full` (admin) précharge l'éditeur
  avec les champs techniques absents de la liste publique.
- **Annonces** (`announcements`) — CRUD individuel (pas un remplacement global comme les
  autres) : titre optionnel, `message` en **markdown façon Discord** (`**gras**`,
  `*italique*`, `__souligné__`, `~~barré~~`, `||spoiler||` — rendu par un petit parseur
  maison, `launcher/src/components/MarkdownText.tsx`, pas de dépendance externe), images
  (upload multipart vers `POST /announcements/images`, servies via `/uploads/*`,
  sélection de fichier native côté launcher via `tauri-plugin-dialog`).
- **Settings** (`settings`, ligne singleton) — lien "Buy Me a Coffee", sous-titre et
  accroche de l'accueil. Page "Paramètres" dans le launcher, visible seulement si admin.

### Repost sur Discord (annonces + règlement)

Best-effort, jamais bloquant pour l'API : si `DISCORD_ANNOUNCEMENT_CHANNEL_ID` /
`DISCORD_RULES_CHANNEL_ID` sont configurés (sinon la fonctionnalité est simplement
absente), créer/éditer une annonce ou le règlement dans le launcher poste/édite **le même
message Discord en place** (pas de spam d'un nouveau message à chaque modif) via
`api/src/announcements/discord.ts` et `api/src/content/discord.ts`. Rendu en **embed**
Discord (titre gras+agrandi, barre de couleur, description, image) plutôt qu'en texte
brut — un simple `**gras**` ne donne pas un rendu de titre assez distinct sur Discord.
Le `content: ""` explicite dans les requêtes d'édition est important : sans lui, Discord
garderait l'ancien texte brut affiché à côté du nouvel embed. `PUBLIC_API_URL` (optionnel)
est nécessaire pour que Discord puisse intégrer les images (`/uploads/...` doit être une
URL joignable par Discord, pas `127.0.0.1`).

Salon à verrouiller côté Discord (config manuelle, rien à coder) : @everyone refuse
"Envoyer des messages", autorise "Ajouter des réactions" ; le bot doit avoir "Envoyer des
messages" + "Intégrer des liens" sur ce salon précis.

## État actuel / prochaines étapes

- **Fait et testé end-to-end** : app + bot Discord créés et configurés, login OAuth2
  réel fonctionnel (avec annulation possible en cours de connexion), onboarding complet
  (règlement + SteamID), rôle admin, contenu admin-géré (règlement/FAQ/mods/annonces/
  settings) avec repost Discord pour règlement + annonces, logo/favicon/icône native
  Fedoheim en place. `api/src/db/seed.ts` (`npm run db:seed`) pour des données de dev.
- **Fait, pas encore validé sur une vraie install** : cœur du launcher — packaging d'un
  mod/de BepInEx en zip uploadé en un clic (voir section Modpacks), lancement moddé sur
  Windows (profil externe + Steam + arguments Doorstop) et sur macOS (profil = dossier
  du jeu + injection DYLD/Terminal.app, mécanisme repris de
  [macheim](https://github.com/lofcgi/macheim)), action "Jouer" unique. Les deux
  mécanismes n'ont pu être vérifiés que par lecture de code source (Gale pour Windows,
  macheim pour macOS), pas par un test réel avec Steam + Valheim installés — à faire en
  priorité sur chaque plateforme avant de considérer cette partie fiable.
- **Testée en jeu, quatre bugs trouvés et corrigés jusqu'ici** : la connexion automatique
  de `FedoServerTools` (voir section "Connexion automatique" ci-dessus) — menu principal
  resté visible sous l'écran de création, connexion ne se déclenchant jamais après
  création d'un nouveau perso (`OnCharacterStart()` jamais appelé), liaison compte↔perso
  ne se faisant jamais pour un admin hébergeant lui-même (l'hôte n'a pas de `ZNetPeer`,
  résolution SteamID corrigée via `Splatform`), et une reconnexion rapide recréant quand
  même un nouveau perso malgré une liaison déjà réussie (`state.session` périmé côté
  launcher, corrigé par un rafraîchissement avant chaque lancement). Le reste de
  l'enchaînement runtime reste vérifié seulement par reflection dump contre le vrai
  assembly (signatures correctes), pas encore observé en jeu jusqu'au bout d'une vraie
  connexion avec ces quatre correctifs.
- Migrations Drizzle à jour jusqu'à `0025_past_talon` (voir
  `api/drizzle/`) — toujours générer via `db:generate` + appliquer via `db:migrate`, ne
  jamais éditer une migration déjà appliquée.
- **Pas encore fait** :
  - `website/` — à construire en réutilisant `api/` (voir Vue d'ensemble ci-dessus),
    pas en dupliquant la logique règlement/FAQ/mods/annonces/settings.
  - Mods maison (BepInEx) écrits mais pas encore configurés dans un profil de modpack en
    base — un admin doit encore uploader les zips de `mods/dist/` via l'éditeur du
    launcher pour qu'ils soient réellement distribués aux joueurs.
  - UI admin pour bannir/débannir (`PATCH /admin/users/:discordId/ban` existe côté API,
    pas encore appelé depuis le launcher).
  - Barre de progression fine (par octet téléchargé) — la progression actuelle de
    "Jouer" est par mod/étape, pas plus granulaire.
- Variables d'env optionnelles (l'API démarre normalement sans elles, la fonctionnalité
  associée est juste absente) : `DISCORD_ANNOUNCEMENT_CHANNEL_ID`, `DISCORD_RULES_
  CHANNEL_ID`, `PUBLIC_API_URL`. Une variable optionnelle laissée vide dans `.env` doit
  être traitée comme absente côté schema zod (voir `emptyToUndefined` dans
  `api/src/config.ts`) — une chaîne vide n'est pas `undefined` pour zod.
- `tsx watch` (le `npm run dev` de l'API) ne recharge **pas** sur un changement de
  `.env` — après avoir édité `.env`, il faut redémarrer le process à la main, sinon la
  nouvelle config n'est jamais prise en compte silencieusement.
- Pas encore de repo git initialisé, pas de CI.
- Tauri v2 — toute nouvelle commande de plugin (`opener`, `dialog`...) doit être
  explicitement autorisée dans `launcher/src-tauri/capabilities/default.json`, pas
  seulement ajoutée côté Rust : `opener:default` ne couvre que `openUrl`, pas
  `openPath`/`revealItemInDir`, qui ont en plus besoin d'un **scope** explicite
  (`{"identifier": "opener:allow-open-path", "allow": [{"path": "**"}]}`) sans quoi
  l'appel échoue avec "not allowed to open path" même une fois la permission ajoutée.
  Un rechargement (`tauri dev` relancé) est nécessaire après modif des capabilities.
- Tout appel HTTP du launcher vers l'API doit passer son erreur réseau par
  `config::describe_request_error(&e)` (pas `format!("... {e}")` à la main) — sinon
  l'erreur technique brute de reqwest ("error sending request for url (...)") fuit
  jusqu'à l'UI au lieu d'un message clair type "Impossible de joindre l'API Fedoheim".

## Conventions

- TypeScript strict partout côté Node (`api/`).
- Rust : `cargo fmt` + `cargo clippy` avant de considérer un changement terminé.
- Pas de secrets (tokens Discord, JWT secret) commités — voir `.env.example` dans `api/`.
- **Toujours garder l'API propre par défaut** (validation zod, champs sensibles dérivés
  de la session authentifiée plutôt que du body client — ex: l'auteur d'une annonce, pas
  un rôle admin auto-déclaré —, migrations additives, actions Discord toujours
  best-effort/non-bloquantes) sans qu'il faille le redemander à chaque fois.
- Toute route d'écriture admin passe par `requireAuth` **puis** `requireAdmin` (revérifie
  le rôle en direct sur Discord, jamais seulement le JWT/la DB) ; les routes qui donnent
  accès au contenu du jeu passent par `requireOnboarded` en plus.
- Logo/icônes : le fichier source (`launcher/public/logo_fedoheim.png`) est recadré à sa
  vraie zone visible (peu de marge transparente) avant d'en dériver favicon/icône native
  — sinon le dessin apparaît minuscule une fois réduit. `npx tauri icon <source>`
  régénère tout (`src-tauri/icons/`) ; nécessite Node 22 (échoue sous Node 18 avec une
  erreur de binding natif `@tauri-apps/cli-darwin-*`). Sur macOS, l'icône du Dock est
  mise en cache au niveau OS et ne se met pas à jour toute seule après régénération —
  `killall Dock` force la relecture ; le nom affiché au survol (`productName` dans
  `tauri.conf.json`) ne se met à jour qu'après un redémarrage de `tauri dev`.

## Environnement de dev (cette machine)

- Node géré via `nvm` — utiliser Node 22 pour `api/` (Fastify 5 requiert Node ≥ 20 ;
  Node 18 par défaut sur cette machine est trop ancien). Un `.nvmrc` est présent dans `api/`.
  **Chaque nouvelle commande shell repart sur Node 18** (pas de persistance de `nvm use`
  entre commandes) : `npx drizzle-kit migrate` sous Node 18 **segfault silencieusement**
  (exit code 139, aucun message d'erreur, `__drizzle_migrations` pas mis à jour) — le
  binding natif de `better-sqlite3` en cause. `db:generate` ne plante pas (diff de schéma
  en pur JS, pas d'ouverture réelle de la db), seul `db:migrate` a besoin du `nvm use 22`
  explicite dans la même commande.
- Rust installé via `rustup` pour le launcher (`launcher/src-tauri`).
- **Une vraie install Valheim complète existe sur cette machine** (pas juste un bundle
  `.app` vide) : `~/Library/Application Support/Steam/steamapps/common/Valheim/`, avec
  `assembly_valheim.dll` sous `valheim.app/Contents/Resources/Data/Managed/` — c'est
  d'ailleurs déjà le chemin `ValheimManagedDir` utilisé par les `.csproj` des mods (voir
  `mods/FedoServerTools/FedoServerTools.csproj`). Utile à savoir : un dump de reflection
  (`System.Reflection.MetadataLoadContext`, voir `mods/CLAUDE.md`) contre cet assembly
  est possible directement sur cette machine, pas besoin d'un accès Windows pour ça —
  seul un lancement réel du jeu (pour valider le comportement runtime d'un patch Harmony,
  pas juste sa compilation) resterait à faire en priorité sur la cible Windows.

## Plateforme cible du launcher

Développement sur macOS, mais **la cible principale du launcher est Windows** (l'immense
majorité des joueurs Valheim sont sur Windows/Steam ; les mods BepInEx et `valheim.exe`
sont Windows-first). Une **version macOS est aussi prévue** pour les quelques joueurs sur
Mac — le launcher doit donc rester vraiment cross-platform, pas juste "Windows avec un
bac à sable macOS". Conséquences :

- Le code Rust gère explicitement les deux plateformes (via `cfg(target_os)`) plutôt que
  de faire des suppositions : détection du dossier d'install Valheim (registre Steam sur
  Windows, `~/Library/Application Support/Steam` ou équivalent sur macOS), dossier
  `BepInEx/plugins`, et commande de lancement (`valheim.exe` vs le binaire/app macOS).
- BepInEx et l'écosystème de mods Valheim sont pensés Windows-first ; il faudra vérifier
  au cas par cas la compatibilité macOS de chaque mod maison (certains mods natifs/DLL
  Windows-only pourraient ne pas avoir d'équivalent Mac).
- Le dev quotidien (UI React, logique métier commune) se fait sur macOS via Tauri.
- Les builds de distribution pour les deux plateformes se feront via CI (GitHub Actions,
  matrice `windows-latest` + `macos-latest`) — à mettre en place quand on packagera une
  première release. La compilation croisée locale (macOS → Windows) n'est pas fiable
  (dépendances natives WebView2 côté Windows), donc chaque OS est buildé par son propre
  runner CI.
