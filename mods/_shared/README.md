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

**Pas de dépendance NuGet à ajouter** : `[PublicAPI]` (utilisé dans `ConfigSync.cs`) est
déjà fourni par `UnityEngine.CoreModule` (déjà référencé par tous les mods) — ajouter le
package NuGet `JetBrains.Annotations` provoquerait un conflit de type (`CS0433`).

**Ne jamais synchroniser un secret** (voir `FedoServerTools/README.md`) : `AddConfigEntry`
diffuse la valeur à tous les clients connectés dès qu'elle change — jamais pour un
`ConfigEntry` comme `ServerToken`.

## BroadcastMessage.cs

RPC partagée entre `FedoServerTools` (envoi, `ServerCommands.ApplyBroadcastMessage`) et
`FedoClientTools` (réception/affichage, `RPC_ShowMessage`) — un message admin diffusé
depuis le launcher doit atteindre tout client connecté, donc le protocole RPC (nom,
format) doit être identique des deux côtés ; un seul fichier partagé évite que les deux
mods divergent. Logging générique (`UnityEngine.Debug`, pas le `Log` d'un plugin précis)
puisqu'il tourne dans deux assemblies distinctes.

## ZNetSceneStabilityPatch.cs

Patch générique de stabilité (voir `FedoServerTools/README.md`, section "Stability
patch") — pas spécifique au reporting ni à aucune autre fonctionnalité, doit tourner sur
toute installation (client ou serveur). Partagé entre `FedoServerTools` et
`FedoClientTools` pour ne pas dupliquer le correctif deux fois.
