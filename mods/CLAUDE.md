# Instructions du projet

- Toujours répondre à l'utilisateur en français.
- Les commentaires générés dans les fichiers de config BepInEx (`*.cfg`) doivent toujours être en anglais. Ces commentaires proviennent directement des descriptions passées à `Config.Bind(...)` dans le code C# : c'est donc là qu'il faut écrire ces descriptions en anglais, pas dans le `.cfg` lui-même (généré automatiquement au lancement du jeu).
- Après toute modification du code d'un mod, toujours lancer `dotnet build -c Release` sur le `.csproj` du mod concerné, et vérifier que la DLL compilée est bien copiée dans son dossier `BepInEx/plugins/<NomDuMod>/` (target `CopyToPlugins` du `.csproj`). Ne jamais laisser le mod dans un état où le code a changé mais où la DLL installée dans le jeu n'a pas été régénérée.
- Textes affichés au joueur (noms, messages, bulles de dialogue...) : les valeurs par défaut dans le code C# doivent être en anglais. Une fois le `.cfg` de l'utilisateur généré, éditer ce fichier directement pour y mettre le texte français voulu -- ne jamais mettre le français comme valeur par défaut dans `Config.Bind(...)`.
- BepInEx charge les DLL une seule fois au lancement du process : aucune modification de code ne prend effet sans un redémarrage complet de Valheim (quitter puis relancer, pas juste recharger une sauvegarde). Les valeurs de `.cfg`, elles, sont rechargées à chaud sans redémarrer.
- Ces mods ciblent des serveurs multijoueur : tout son ajouté par du code (`AudioSource` créée dynamiquement, pas juste un `EffectList` du jeu) doit rester positionnel en 3D (`spatialBlend = 1`) avec un `maxDistance`/rolloff maîtrisé -- jamais un son "plat" (`spatialBlend = 0`), qui serait entendu à plein volume par tout joueur ayant la zone chargée, peu importe sa distance réelle.
- **Tout réglage qui affecte le monde/gameplay partagé (portée, taux de spawn, loot, textes vus par tout le monde...) doit passer par ServerSync**, pas un `Config.Bind` brut : voir `mods/_shared/ConfigSync.cs` (fichier partagé, `<Compile Include="../_shared/ConfigSync.cs" />` dans le `.csproj`) et `FedoDeath`/`FedoGoldRabbit`/`FedoGuardian`/`FedoServerTools` pour le patron déjà en place (`ConfigSync` + méthode `SyncedConfig<T>` qui wrap `Config.Bind` + `AddConfigEntry`, `IsLocked = true` avant `_harmony.PatchAll()`). Sinon un joueur qui édite son `.cfg` local joue avec des valeurs différentes du reste du serveur, ou peut désactiver un comportement voulu par l'admin. **Exception : jamais un secret** (webhook Discord, jeton) — `AddConfigEntry` diffuse la valeur à tous les clients connectés dès qu'elle change, l'inverse de ce qu'on veut pour un secret ; `FedoDeathGif`/`FedoServerTools` (webhook Discord) et `FedoServerTools.ServerToken` (jeton API Fedoheim) restent volontairement hors ServerSync pour cette raison. Références `.csproj` nécessaires en plus des habituelles : `assembly_utils`, `UnityEngine.UI`, `Unity.TextMeshPro` — jamais le package NuGet `JetBrains.Annotations` (conflit de type avec `UnityEngine.CoreModule`, qui fournit déjà les mêmes attributs).

## Notes techniques de modding (assembly_valheim / ZNetScene)

- `ZNetView.GetPrefabName()` est **privée** (inaccessible depuis un mod). Pour identifier le prefab d'une ZDO, comparer `zdo.GetPrefab()` (hash int, public) à `StringExtensionMethods.GetStableHashCode("NomDuPrefab")` (méthode publique de `assembly_utils.dll`, à référencer dans le `.csproj`).
- Ajouter un prefab custom (clone d'un prefab vanilla, ex. pour un mob) : `ZNetScene.m_prefabs` (`List<GameObject>`, public) peut être complété directement -- ça suffit pour qu'un mod qui énumère cette liste (ex. Easy Spawner) le liste. En revanche `ZNetScene.m_namedPrefabs` (le dictionnaire privé utilisé en interne par `GetPrefab`/`HasPrefab`) ne doit pas être modifié par réflexion : le clone se fait détruire par le jeu peu après sa création (probablement une transition de scène tôt dans le boot), rendant une écriture ponctuelle inutile. Patcher plutôt `ZNetScene.GetPrefab` (les deux surcharges, int et string) et `ZNetScene.HasPrefab` avec un Postfix **auto-réparant** : si le résultat original est vide, recréer le clone à la volée à partir du prefab source si besoin, puis mémoriser la référence. `HasPrefab` doit être patchée en plus de `GetPrefab` : `ZNetScene.CreateObject` (matérialisation d'une ZDO, ex. via `SpawnSystem.Spawn`) vérifie `HasPrefab` avant d'appeler `GetPrefab`.
  - **Piège vécu (FedoGoldRabbit) : ne JAMAIS `SetActive(false)` sur le clone-gabarit
    lui-même** pour l'empêcher d'agir comme une vraie entité tant qu'il n'est qu'une
    cible de lookup -- `activeSelf` (pas `activeInHierarchy`) est ce qu'`Instantiate()`
    recopie sur chaque VRAIE instance créée par `ZNetScene.CreateObject` (qui la place à
    la racine du monde, sans parent), donc un gabarit désactivé produit de vrais mobs
    désactivés à leur tour. Pire qu'un simple bug visuel (invisible, sans IA, `Awake` qui
    ne se déclenche jamais tant que l'objet reste inactif -- donc toute logique qui en
    dépend, nom/faction/apparence) : `ZNetView.Awake()` doit consommer un hint statique
    (`ZNetView.m_initZDO`) posé par `CreateObject` juste avant `Instantiate()` pour
    savoir à quelle ZDO s'associer -- s'il ne s'exécute pas tout de suite (objet inactif),
    la fenêtre se referme (`CreateObject` la nettoie et logue "ZDO ... not used..." dès
    qu'il détecte qu'elle n'a pas été consommée) et la ZDO d'origine ne passe jamais
    `Created = true`. Un mob dont le spawn passe par le pipeline vanilla (table de
    `SpawnSystem`, ZDO rechargée d'une sauvegarde) se fait alors ré-instancier à
    l'identique à CHAQUE frame, indéfiniment -- observé en jeu comme un crash en boucle
    de `ZNetScene.RemoveObjects`/`CreateDestroyObjects`. **Réactiver après coup (Postfix
    sur `CreateObject`) ne suffit pas** : c'est trop tard, la fenêtre de `m_initZDO` est
    déjà refermée. La bonne technique (déjà utilisée par `GuardianPrefabPatch`) : laisser
    le clone `activeSelf = true`, et le rendre inerte en l'instanciant comme **enfant
    d'un conteneur racine désactivé en permanence** (`Instantiate(source, templateRoot,
    worldPositionStays: false)`) -- Unity ne déclenche jamais Awake/OnEnable/Start sur un
    objet inactif *dans la hiérarchie*, mais une fois `Instantiate()`-é séparément à la
    racine du monde (sans parent, ce que fait `ZNetScene.CreateObject`), son propre
    `activeSelf` (resté `true`) redevient ce qui compte, et `Awake()` s'exécute
    normalement, à temps.
- `ItemDrop.ItemData.m_dropPrefab` n'est renseigné qu'au runtime par `ItemDrop.Awake()` (auto-référence vers son propre prefab). Récupérer un `ItemData` directement depuis un prefab jamais instancié (ex. `ZNetScene.GetPrefab("Coins").GetComponent<ItemDrop>().m_itemData`) donne un `m_dropPrefab` null, et `ItemDrop.DropItem(...)` plante avec "Object you want to instantiate is null" -- il faut renseigner `m_dropPrefab` soi-même avant de l'utiliser. `CharacterDrop.Drop` (table de loot à la mort) n'a pas ce problème : il référence directement un `GameObject`.
- `Character.Faction.Boss` rend une créature ignorée par tous les autres monstres/factions sauvages (alliée à tout sauf aux joueurs en vanilla), sans afficher de barre de vie/musique de boss tant que `m_boss` reste à `false`. Pratique pour protéger un mob spécial des prédateurs sauvages.
- Charger un fichier audio custom (mp3 fourni par l'utilisateur, pas un asset Unity) : `UnityWebRequestMultimedia.GetAudioClip("file://" + chemin, AudioType.MPEG)` dans une coroutine du plugin (référencer `UnityEngine.UnityWebRequestModule.dll` et `UnityEngine.UnityWebRequestAudioModule.dll`), chemin résolu via `Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)`. Ajouter le fichier au target `CopyToPlugins` du `.csproj` pour qu'il soit livré à côté de la DLL.
- Pour inspecter les signatures/accessibilité exactes d'`assembly_valheim.dll` avant d'écrire un patch Harmony (éviter de deviner un nom de champ/méthode) : petit programme jetable utilisant `System.Reflection.MetadataLoadContext` (package NuGet) pointé sur le dossier `Managed` du jeu -- plus fiable que de se fier à la mémoire/documentation communautaire.
