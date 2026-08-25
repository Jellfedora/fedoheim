namespace FedoServerTools
{
    // Nom du biome envoyé tel quel (`Heightmap.Biome.ToString()`, ex: "Meadows",
    // "BlackForest") -- au launcher de le traduire pour l'affichage, pas au mod de
    // décider dans quelle langue.
    public readonly struct PlayerReport
    {
        public string Name { get; }
        public string Biome { get; }

        // Armure totale actuelle (Humanoid.GetBodyArmor()), arrondie côté mod avant
        // l'envoi -- `null` si le personnage n'a pas pu être retrouvé côté serveur.
        public int? Armor { get; }

        // SteamID64 du pair connecté (voir PeerSteamId.Resolve) -- sert uniquement à
        // l'API pour lier ce nom de perso au compte Fedoheim correspondant
        // (modpacks/onlinePlayers.ts::linkCharacterName), jamais affiché. `null` si non
        // résolvable (connexion non-Steam, ou pair introuvable au moment du rapport).
        public string SteamId { get; }

        // `true` uniquement sur le rapport où ce joueur vient de passer vivant->mort
        // (voir FedoServerToolsPlugin.GetConnectedPlayers) -- un edge, pas un état ; reste
        // `false` sur les rapports suivants tant qu'il n'est pas remort entre-temps. Décidé
        // ici plutôt que patché sur Player.OnDeath (voir PlayerDeathAnnouncePatch) : OnDeath
        // ne se déclenche que côté client du joueur qui meurt, alors que ce rapport tourne
        // côté serveur pour tout le monde, y compris les pairs distants sur un vrai serveur
        // dédié.
        public bool Died { get; }

        public PlayerReport(string name, string biome, int? armor, string steamId, bool died)
        {
            Name = name;
            Biome = biome;
            Armor = armor;
            SteamId = steamId;
            Died = died;
        }
    }
}
