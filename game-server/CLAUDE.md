# Fedoheim — Configuration du serveur Valheim

Documentation des configurations du serveur de jeu Valheim. À alimenter au fur et à mesure.

## Contenu de ce dossier

- `CLAUDE.md` — cette documentation
- `backup/<date>/world/` — backup du monde Valheim (`worlds_local/Fedoheimv12`)
- `backup/<date>/bepinex/config/` — configs BepInEx du serveur (Marketplace, mods...), DLL exclues.
  **Secrets masqués** (`<REDACTED>` : webhooks Discord, `ServerToken`, mot de passe serveur) —
  ne jamais commiter les vraies valeurs. `KeyManager/` (clé de licence Hexium de Marketplace)
  est exclu par `.gitignore` : ne jamais le commiter (le repo GitHub est **public** ; la clé et
  un webhook AzuAntiCheat ont fuité via la sauvegarde du 2026-10-05, toujours dans l'historique).
- `backup/<date>/bepinex/plugins/` — plugins installés (ignorés par git, réinstallables via Gale)
- `backup/<date>/mods.md` — liste des mods (serveur + profil Gale) avec versions
- `backup/<date>/system/` — fichiers système du serveur Ubuntu (script `valheim-ctl`, units
  systemd, script de lancement)
- `tools/peche_gen.py` — génère `Quests/peche.cfg` + dialogues Olaf/Eirik/Torvald (à lancer sur
  le serveur avec `python3`, puis réécrire les fichiers sur place, voir Marketplace)
- `power-agent/` — agent systemd qui démarre/arrête/redémarre le serveur depuis le
  launcher (voir son README pour l'installation)

Dernier instantané : `backup/2026-10-11/`. Les backups sont à maintenir à jour manuellement
avant/après chaque grosse modification du serveur.

## Environnement

- **Serveur dédié (prod, pas encore ouvert aux joueurs)** : Ubuntu `192.168.1.26`, user `valheim`
  (SSH par clé depuis le PC Windows ; `sudo` demande le mot de passe — voir mémoire Claude ;
  depuis l'extérieur : hôte `valheim` dans `~/.ssh/config`, IP publique `86.206.254.199`),
  install `~/valheim-server/`, service systemd `valheim.service`, monde `Fedoheimv12`
  (`~/.config/unity3d/IronGate/Valheim/worlds_local/`), port 2456, preset hard, `nomap`.
- **Serveur dédié local (ancien, test)** : `C:\Users\julie\Desktop\valheim-server\`
- **Profil joueur/admin (Gale)** : `C:\Users\julie\AppData\Roaming\com.kesomannen.gale\valheim\profiles\fedoheim admin\`
  — c'est la référence du modpack. Le dev préfère prendre les mods sur **Hexium**
  (`valheim.hexium.gg`), Thunderstore en second.
- **Se connecter depuis le réseau local** : `192.168.1.26:2456` (l'IP publique fonctionne aussi
  depuis chez soi via la box, mais en cas de timeout, tester d'abord l'IP locale).

## Exploitation du serveur Ubuntu

- **Redémarrages + sauvegardes** : `valheim-reboot.timer` (systemd) à **6h00 et 17h00, heure de
  Paris** → `/usr/local/bin/valheim-ctl backup` : arrêt, archive monde + configs dans
  `~/backups/world/fedoheim-<date>.tgz` (28 gardées = 14 jours), redémarrage.
  `valheim-ctl status` pour l'état. Copies dans `backup/<date>/system/`.
- `valheim.service` s'arrête en **SIGINT** (drop-in `valheim.service.d/override.conf`) : Valheim
  ne sauvegarde proprement qu'avec SIGINT. Cron Job fait en plus un `save` à 5h59/16h59.
- **Cron Job** (`BepInEx/config/cron.yaml`) : annonces des redémarrages (30/10/5/1 min avant) et
  reset hebdo des donjons le lundi 5h50 (`locations_reset ...`, bases protégées).
  **Piège vécu** : `cron_last.yaml` copié depuis l'ancien serveur (dernière exécution figée au
  2026-10-04) → Cron Job rejouait toutes les tâches du lundi **chaque minute** (5000+ resets de
  donjons du 07 au 11/10). Corrigé en supprimant `cron_last.yaml` serveur arrêté. Si des
  annonces/resets arrivent hors horaire, regarder ce fichier en premier.
- **Vérifier qu'un redémarrage a vraiment abouti** : `Game server connected` dans le log ne
  suffit pas. Le serveur n'est prêt que quand le monde est chargé (`DungeonDB Awake`, puis
  `ss -ulpn | grep valheim` montre le port **2456**). Vécu : Smoothbrain-Sailing plantait
  `FejdStartup.Start` sur le serveur dédié (`NullReferenceException` dans
  `Sailing.FetchShipPieces` → `LocalizationManager.Localizer`), le monde ne se chargeait jamais
  et toutes les connexions expiraient (timeout), sans autre erreur visible.
- Sauvegardes ponctuelles (avant modif) dans `~/backups/` : `bepinex-*.tgz`, `configs-*.tgz`,
  copies individuelles de `.cfg` ; plugins retirés dans `~/backups/removed-plugins/`.

## Mods installés (serveur)

Liste exhaustive avec versions : `backup/2026-10-11/mods.md`. Principaux :

| Mod | Version | Rôle |
|---|---|---|
| KG-Marketplace_And_Server_NPCs_Revamped | 10.0.6 | Marketplace, quêtes, NPCs, territoires |
| Smoothbrain-Professions / Guilds / Groups / ServerCharacters | 1.4.7 / 1.1.14 / 1.2.12 / 1.4.17 | Professions, guildes, groupes, persos stockés côté serveur |
| Smoothbrain-Farming/Mining/Blacksmithing/etc. | — | Compétences métier |
| shudnal-Seasons | 1.10.5 | Saisons dynamiques |
| JereKuusela-Expand_World_Size | 1.45.0 | Taille de monde étendue |
| JereKuusela-Server_devcommands | 1.115.0 | Commandes admin avancées |
| JereKuusela-Upgrade_World | 1.84.0 | Reset donjons/zones/végétation (commandes) |
| JereKuusela-Cron_Job | 1.14.0 | Tâches planifiées (voir Exploitation) |
| warpalicious-More_World_Locations_AIO | 5.2.0 | Lieux supplémentaires. Traduit : `Use Custom Localization = On` + `config/warpalicious.More_World_Locations_Localization.French.yml` (réglage **non synchronisé** : cfg + yml doivent être dans le modpack joueur). Ses PNJ Bjorn/Eirik renommés Bjarki/Einar en FR (noms déjà pris par nos PNJ). Les pages d'aide des ports sont des .md intégrés au DLL, traduites par FedoheimClient (`config/FedoheimClient/mwl_tutorials/*.md`, aussi à mettre dans le modpack) |
| Geckuss-DeathRoulette | 1.6.0 | Effets à la mort |
| blacks7ar-Hunting | 1.4.5 | Chasse |
| M2Valheim-VikingHorse | 1.0.7 | Chevaux |
| Azumatt-HearthBelow | 0.6.3 | La pioche creuse de vraies grottes (profondeur selon le tier) |
| Azumatt-CurrencyPocket | 1.0.15 | Poche à monnaie (les pièces n'apparaissent pas dans l'inventaire) |
| Digitalroot-Eternal_Fire | 1.1.7 | Feux sans combustible |
| JoelOliMclean-NoRainDamage | 1.3.2 | Pas d'usure des constructions par la pluie |
| WackyMole-WackyEpicMMOSystem | 1.9.71 | Niveaux + points d'attributs (XP partagée via Smoothbrain-Groups) |
| WackyMole-RareMagicPortalPlus | 3.3.2 | Portails : **2 max par joueur** (`MaxAmountOfPortals = 2`), couleur imposée par biome, **clé de la couleur requise** (`Default Mode for New Portals = CrystalKeyMode` — `Normal` + `Enable CrystalKey For New` ne suffit PAS), Gold master désactivé. Clés/cristaux sans recette : clé `rmp_PortalKey<Couleur>` donnée par la quête du boss du biome (Eikthyr→Tan/Bjorn, Ancien→Blue/Hilda, Masse d'os→Green/Gunnar, Moder→Black/Astrid, Yagluth→Orange/Ulf, Reine→Purple/Ingrid, Fader→Red/Orm). Admins : passent sans clé, changent mode (Ctrl gauche+E) et couleur (Alt gauche+E). Compteur par joueur : `config/Portal_Names/Fedoheimv12_PlayerPortals.yml`. Remplace Azumatt-PortalPreload (retiré) |
| WackyMole-Hearthstone | 1.1.3 | Pierre de foyer (retour au lit, recharge 2 h). **Non craftable** (`Crafting Station = Disabled`), obtenue uniquement en récompense du trophée d'Eikthyr chez Ragna |
| Mods maison Fedoheim | — | FedoDeathGif, FedoHud (1.1.2), FedoSignColor, FedoSkillSafety, FedoServerTools, **FedoheimClient (1.0.1, client + serveur, privé — voir Marketplace)** (FedoDeath/FedoGoldRabbit retirés tant qu'ils ne sont pas finis) |
| Azumatt-Build_Camera_Custom_Hammers_Edition | 1.3.4 | Caméra volante du marteau. `Restriction Mode = Off` (la 1.3.x exige par défaut d'être à l'abri pour ramasser depuis la caméra) |

Mods côté joueur uniquement (dans Gale, **pas** sur le serveur) : Smoothbrain-Sailing (affichage
client seulement, fait planter le démarrage du serveur dédié), ConfigurationManager, Gizmo,
EasySpawner, FastLink, Unshamed, SaveCrossbowState, FedoAchievements.

**AzuAntiCheat (listes de mods clients)**, dans `BepInEx/config/AzuAntiCheat/` :
- `Whitelist/` = **obligatoire** pour tout joueur, `Greylist/` = **facultatif** (doc 5.2.x ; pas
  l'inverse). Tout autre mod côté client → kick.
- Les fichiers sont comparés **par contenu** (hash) : il faut les **vraies DLL**, identiques à
  celles du modpack joueur. Un fichier vide ne correspond à rien (l'ancienne Greylist en fichiers
  de 0 octet ne servait à rien).
- Choix Fedoheim : Whitelist = toutes les DLL du modpack (profil Gale), pour que tout le monde
  ait exactement les mêmes mods ; Greylist = outils admin seulement (ConfigurationManager,
  EasySpawner, ServerDevcommands, UpgradeWorld). Les mods serveur seuls (MaxPlayerCount,
  Cron_Job, FedoServerTools) n'ont pas à y être. FedoheimClient y est (obligatoire chez tous).
- **À chaque mise à jour d'un mod côté joueur, recopier sa nouvelle DLL dans `Whitelist/`**
  (et retirer l'ancienne), sinon les joueurs à jour sont kickés (`file does not match`).
  AzuAntiCheat relit les dossiers à chaud (log : `Whitelist plugins: N`).

**Mise à jour depuis Gale** :
1. Comparer les `version_number` des `manifest.json` Gale vs serveur (Gale ne met pas la
   version dans le nom de dossier, une install Thunderstore oui → supprimer l'ancien dossier
   versionné, sinon la DLL est en double et BepInEx peut garder l'ancienne).
2. Sauvegarder `plugins` + `config`, arrêter le service, copier les dossiers (sauf mods
   client seuls), redémarrer, vérifier le démarrage complet (voir Exploitation).
3. Reconstruire `AzuAntiCheat/Whitelist/` à partir des DLL du profil Gale.

**Textes en français** : les mods maison ont leurs textes en anglais par défaut ; la traduction
se fait dans les `.cfg` (`fedo.hud.cfg`, `fedo.deathgif.cfg`, `fedo.skillsafety.cfg`...), dans le
profil Gale **et** sur le serveur pour les réglages synchronisés par ServerSync (FedoDeathGif,
FedoSkillSafety). BepInEx échappe les apostrophes en `\'` dans les `.cfg`.

## Marketplace & Server NPCs Revamped (KG)

Config principale : `BepInEx/config/Marketplace/MarketPlace.cfg`
Configs détaillées : `BepInEx/config/Marketplace/Configs/`

Steam ID admin déclaré dans `MarketPlace.cfg` → `OverrideDebug = <SteamID64>`.
`AutoAddCloseDialogueButton = false` : pas de bouton "Close" auto (chaque réplique a déjà sa
propre sortie — à garder vrai en écrivant de nouveaux dialogues).

**Rechargement à chaud des configs Marketplace** : le serveur ne détecte une modification que si
le fichier est réécrit **sur place** (même inode). `sed -i` crée un nouveau fichier par
renommage et n'est **pas** détecté : la modif reste ignorée jusqu'au prochain redémarrage, sans
aucun message. Toujours éditer avec une écriture sur place (script Python `open(..., "r+")` /
`write`, ou `cat tmp > fichier`), puis vérifier dans `LogOutput.log` la ligne
`[kg.Marketplace] Quests Database Changed` / `NpcDialogues changed` / `Territory Changed`.
Passer par un script Python copié en `scp` plutôt qu'une commande `ssh '...'` inline : les
apostrophes françaises cassent les guillemets du shell.

**Commandes console (admin)** : `mclearallquests <nom du perso>` (efface quêtes **et** cooldowns),
`mclearquest <nom du perso> <id>`, `mcustomvalues` (affiche les compteurs d'étapes), `mpos`
(position x,y,z), `zonevisualizer` (bords des territoires, nécessite `devcommands`).
**Ne pas utiliser `mresetcustomvalues`** : il vide **tout** `m_customData` du perso (données
d'autres mods aussi, ex. EpicMMO), pas seulement Marketplace. Aucune commande ne remet un seul
compteur à zéro → passer par une option de dialogue (`SetCustomValue, <clé>, 0` supprime la clé),
ex. Olaf « [Admin] Recommencer la pêche » (visible seulement avec Easy Spawner :
`Condition: ModInstalled, cooley.easyspawner`). À faire connecté au serveur (persos stockés côté serveur par ServerCharacters).
Mode admin : `devcommands` (Server Devcommands active alors debug + nocost + god automatiquement ;
l'activation auto à la connexion peut échouer si le statut admin arrive trop tard → retaper
`devcommands`).

### PNJ et chaînes de quêtes (Prairie)

Village : **Village des âmes perdues** (territoire, voir plus bas).

- **Sigrid l'Ancienne** — accueil, quête d'intro `visite_forgeron` (id historique, jamais vu par
  les joueurs) : aller parler à **Bjorn le Cuisto**. Type Talk `Autocomplete` : la cible
  `"Bjorn le Cuisto"` doit être **exactement** le nom du PNJ dans le monde (renommage en jeu,
  pas en config). Récompense 20 pièces + 50 XP.
- **Bjorn le Cuisto** (cuisinier) — chaîne via `bjorn_etape` : sangliers → nixes → bourgeons
  (greylings ; « bourgeon » est le vrai nom FR du jeu) → cerfs (+ casque de cuir) → Eikthyr
  (50 pièces, hydromels, chope, **clé Tan**). Indice Vegvisir dans sa réplique (en `nomap`, le
  Vegvisir tourne la caméra vers le boss ; il brille en **rouge**). Puis (étape 6→7)
  **« La forêt se réveille »** (`bjorn_foret`, Kill 5 Greydwarf + 1 Greydwarf_Shaman + 1
  Greydwarf_Elite) : lance le **vrai raid vanilla `army_theelder`** via
  `Command: ConsoleCommand, "fedoevent army_theelder"` (FedoheimClient, voir plus bas) ; option
  « Ils ont reculé dans la forêt... » pour relancer. Ensuite renvoie vers Hilda (qui exige
  `bjorn_etape >= 7`).
- **Ragna la taxidermiste** (vit dans une grotte creusée à la pioche — dialogue clin d'œil à
  HearthBelow) — trophées via `trophee_prairie` ; tête d'Eikthyr → **Hearthstone** + hydromels +
  rubis. Son « Et maintenant ? » rappelle où est la stèle d'Eikthyr (sud-est, quatre petites îles).
- **Pêche** (généré par `tools/peche_gen.py`, compteur `peche_etape`, remise `FinishQuest`,
  récompenses `Skill: Fishing, N` + XP) — biomes/appâts : mémoire Claude `reference_valheim_peche`.
  **Olaf le pêcheur** (village) : ramasser 3 perches sans canne → canne + 40 appâts ; pêcher 10
  perches ; pêcher 10 brochets → appâts mousseux. Puis **Eirik, maître pêcheur** (port à l'est de
  la stèle d'Eikthyr) : poisson-troll (→ chapeau de pêcheur, jamais avant), hareng, tétra, mérou,
  thon. Puis **Torvald** : perche corallienne, poisson-globe, baudroie, poisson de magma. Prévu :
  4 maîtres pêcheurs au total (2 restent à créer). `sven.cfg` retiré (remplacé par Eirik).
- **Ylva la Palefrenière** (`ylva_accueil`, `Quests/chevaux.cfg`, compteur `cheval_etape`) : 50
  pissenlits → selle `SaddleM2Horse` ; 50 de plus → cheval apprivoisé (`Pet: M2Horse, 1`, apparaît
  devant le joueur : placer Ylva dehors). Les chevaux ne mangent que des pissenlits et ne lâchent
  que viande + peau de cerf.
- **Paroisse** (`Quests/eglise.cfg`, compteur `eglise_etape`) : **Gudrun** (`gudrun_accueil`, au
  village) envoie 10 framboises + 5 miel à **frère Ansgar** (`ansgar_accueil`, paroisse au nord
  après la rivière, dernier survivant) ; Ansgar lance la défense (20 squelettes par `SpawnXYZ` en 4
  points à 30 m de (-518, -368), hauteur 53 ; en tuer 5 ; relance possible) → relique
  `AxeHead1` ; puis retrouver **Frère Keli le Bon** (`keli_accueil`, quête Talk, cible = nom
  exact du PNJ) → `AxeHead2` (les deux têtes des Haches de Berserkir, recette non vérifiée).

### Quêtes et XP EpicMMO

Fichiers : `Configs/Quests/*.cfg` (définitions) et `Configs/Dialogues/*.cfg` (PNJ). Deux façons de rendre une quête :

- **`Command: FinishQuest, <id>`** (Bjorn, Hilda, Gunnar, Astrid, Ulf, Ingrid, Orm, pêche, Ylva, paroisse) : la
  récompense donnée est la ligne de récompense du fichier de quête → l'XP y est ajoutée avec `EpicMMO_EXP: N`.
  Pour une quête Collect, `FinishQuest` vérifie et retire les objets lui-même ; garder quand même
  `Condition: HasItem` sur l'option, sinon `SetCustomValue` s'exécute même si la remise a échoué.
- **Remise manuelle** (`RemoveItem` + `RemoveQuest, <id>, false` + `GiveItem` — trophées : Ragna, Leif,
  Grima, Knut, Harald, Solveig, Yrsa) : la ligne de récompense du fichier de quête n'est
  **pas** distribuée, juste affichée → l'XP est donnée par `Command: AddEpicMMOExp, N` dans le dialogue (et
  recopiée dans le fichier de quête pour l'affichage). Toute nouvelle récompense doit être ajoutée aux deux endroits.
- Équipement en récompense : `Item: <Prefab>, <quantité>, <qualité>` (ex. `Item: HelmetLeather, 1, 1`).
- Compétence : `Skill: Fishing, N` (+N niveaux) ou `Skill_EXP: Fishing, N` (XP brute), seulement via le fichier de
  quête (aucune commande de dialogue ne donne de compétence ; `ConsoleCommand, raiseskill` est une triche refusée
  aux joueurs). **Avec Smoothbrain-Professions installé, Marketplace ignore la récompense si la compétence est à 0**
  → ne pas en donner avant que le joueur ait pu l'exercer.
- Créature apprivoisée : `Pet: <Prefab>, 1` (apparaît à 1,5 m devant le joueur, déjà apprivoisée).

**Pièges Marketplace (vérifiés en décompilant `kg.Marketplace.dll`)** :
- Une ligne `Command:` est lue **sans aucun espace hors guillemets** : `ConsoleCommand, fedoevent army_theelder`
  devient `fedoeventarmy_theelder` et échoue sans message → mettre entre guillemets
  (`Command: ConsoleCommand, "fedoevent army_theelder"`).
- Quête Collect, `HasItem`, `RemoveItem` : seuls les objets de **qualité exactement 1** comptent (un poisson à
  étoiles ne compte pas).
- Plusieurs cibles : séparées par `|` (`Raspberry, 10 | Honey, 5`) ; la 3ᵉ valeur après la virgule est la qualité.
- `ConsoleCommand` exécute la commande côté joueur ; une commande triche (`event`, `raiseskill`, `spawn`...) est
  refusée tant que le joueur n'a jamais activé les triches.
- `Spawn, <prefab>, <nb>, <niveau-1>` : dans ±15 m autour du joueur (sol trouvé automatiquement, peut tomber à
  ses pieds). `SpawnXYZ, <prefab>, <nb>, <niveau-1>, x, y, z, <écart>` : autour d'un point fixe, **sans**
  recherche du sol (donner y un peu au-dessus du terrain).
- Kill en groupe : `AllowKillQuestsInParty = true` (MarketPlace.cfg) + Smoothbrain-Groups → un kill compte pour
  tout le groupe s'il a la quête.
- Animations PNJ : champs Greet/Bye/Interact/Periodic = triggers d'emote joueur (`emote_cower`, `emote_wave`...).

**FedoheimClient** (`mods/FedoheimClient`, privé, client + serveur) : commande console non triche
`fedoevent <event>` ; le serveur ne lance l'event que s'il est dans `AllowedEvents`, que le joueur est dans
`AllowedAreas` et hors `CooldownSeconds` (config serveur `fedo.fedoheimclient.cfg` : `army_theelder`,
`-520,-575,150`, 300 s), et qu'aucun event n'est déjà en cours. Refus loggués côté serveur. La liste des events
du jeu est écrite dans le log au démarrage (`army_eikthyr, army_theelder, wolves, skeletons, foresttrolls...`).

Chaque quête donne un peu d'XP EpicMMO, échelonnée par biome (Prairie 50–250, Forêt noire 100–600, Marais
200–1000, Montagnes 350–1500, Plaines/mer 500–2500, Brumes 800–4000, Terres cendrées 1500–6000 ; boss en haut de
fourchette). Repère : niveau 1→2 = 300 XP, un sanglier ≈ 15–20 XP.

### Territoires

Dossier : `BepInEx/config/Marketplace/Configs/Territories/`  
Un fichier `.cfg` par groupe de zones (plusieurs zones possibles dans un même fichier). Le nom
de section (avant `@priorité`) est le texte affiché au joueur en entrant dans la zone.

**Format d'une zone :**
```
[NomZone@priorité]
Shape
Coordonnées
Couleur RGB
Flags
Access
```

**Formes et coordonnées :**
```
# Cercle — centre_X, centre_Z, rayon
Circle
-559, -662, 50

# Rectangle — origine_X, origine_Z, largeur, hauteur
# Origine = coin SUD-OUEST (X et Z les plus négatifs)
# Largeur = NE_X - SO_X, Hauteur = NE_Z - SO_Z (toujours positifs)
Rectangle
-608, -779, 132, 239

# Carré — centre_X, centre_Z, demi-côté
Square
0, 0, 30
```

**Coords depuis `pos` en jeu** : format affiché `x,z,y` (y = altitude, ignorée pour les territoires).

**Flags utiles :**
- `NoBuild` — interdit la construction
- `NoBuildDamage` — interdit de détruire/endommager les structures (≠ "NoDestroy" qui n'existe pas)
- `NoAttack` — pas d'attaque
- `NoMonsters` — pas de monstres
- `PeriodicHeal = N` — soin périodique
- `ForceBiome = N` — force un biome visuel

**Access :**
- `None` — accessible à tout le monde
- Liste de SteamID ou noms de guildes pour restreindre

**Commandes utiles :**
- `zonevisualizer` — affiche les bordures des zones en jeu
- `zonevisualizeralpha` — ajuste la transparence des bordures

**Reload :** les modifications d'un fichier existant sont prises en compte à chaud (écriture sur
place, voir plus haut). Un nouveau fichier ou un changement de forme (`Circle` → `Rectangle`)
peut nécessiter un redémarrage.

### Zones configurées

| Zone | Forme | Coords | Notes |
|---|---|---|---|
| Village des âmes perdues | Circle | -510, -573, r=50 | Village de Sigrid/Bjorn/Ragna/Olaf/Gudrun |
| Le Seuil des Âmes | Rectangle | -627, -801, 79 × 153 | Zone de spawn |
| Pont des Pèlerins | Rectangle | -354, -541, 17 × 53 | Pont vers la paroisse |
| Paroisse de frère Ansgar | Circle | -518, -370, r=25 | Les squelettes de la quête peuvent y apparaître |

Toutes dans `QuestZone.cfg`, flags `NoBuild, NoBuildDamage`, accès `None`.

## Idées en attente

- **Renouvellement des minerais sans toucher au paysage** : Upgrade World `vegetation_reset
  <ids>` (retire et replace les gisements à leur position d'origine, terrain intact sans
  `terrain=`, bases protégées), à programmer dans Cron Job (ex. avec le reset hebdo des
  donjons). Noms de prefabs à valider en jeu avant (`objects_count ...`) : candidats
  `rock4_copper`, `rock4_copper_frac`, `MineRock_Tin`, `silvervein`, `silvervein_frac`,
  `MineRock_Obsidian`, `MineRock_Meteorite`.
