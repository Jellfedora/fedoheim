# _shared

Fichiers réutilisés tels quels par plusieurs mods (`<Compile Include="../_shared/xxx.cs" />`
dans leur `.csproj`), pas un mod en soi — jamais buildé, jamais packagé dans `mods/dist/`.

## ConfigSync.cs

Librairie communautaire standard du modding Valheim (par blaxxun-boop, largement réutilisée
par l'écosystème BepInEx/Thunderstore — 170+ mods en dépendent) pour synchroniser des
`ConfigEntry` entre le serveur et les clients connectés : le serveur pousse ses valeurs à la
connexion (et à chaud si elles changent), sans jamais toucher au `.cfg` local du client. Avec
`ConfigSync.IsLocked = true`, un client ne peut plus du tout modifier localement les réglages
enregistrés via `AddConfigEntry` — exactement ce qu'il fallait pour que `ForcePublicPosition`
(FedoServerTools) ne puisse pas être désactivé par un joueur.

Source : https://github.com/blaxxun-boop/ServerSync/blob/master/ConfigSync.cs
(commit `c57c2aa` au moment de l'intégration). Fichier intégré tel quel, jamais modifié
directement ici — pour mettre à jour, retélécharger et remplacer.

Dépendance NuGet requise dans chaque mod qui l'utilise : `JetBrains.Annotations` (pour
l'attribut `[PublicAPI]`, purement documentaire, aucun effet à l'exécution).

**Ne jamais synchroniser un secret** (voir `FedoServerTools/README.md`) : `AddConfigEntry`
diffuse la valeur à tous les clients connectés dès qu'elle change — jamais pour un
`ConfigEntry` comme `ServerToken`.
