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
- **Mods maison** — mods Valheim (BepInEx) développés en interne, distribués via l'API
  et installés automatiquement par le launcher. Pas encore démarré (mais le launcher gère
  déjà toute la mécanique de distribution/catégorisation des mods, prêt à en recevoir).

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
- Migrations Drizzle à jour jusqu'à `0020_careless_annihilus` (voir
  `api/drizzle/`) — toujours générer via `db:generate` + appliquer via `db:migrate`, ne
  jamais éditer une migration déjà appliquée.
- **Pas encore fait** :
  - `website/` — à construire en réutilisant `api/` (voir Vue d'ensemble ci-dessus),
    pas en dupliquant la logique règlement/FAQ/mods/annonces/settings.
  - Mods maison (BepInEx) — le launcher est prêt à les distribuer (packaging + sync),
    mais aucun mod réel n'existe encore, ni en DB ni en code.
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
- Rust installé via `rustup` pour le launcher (`launcher/src-tauri`).

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
