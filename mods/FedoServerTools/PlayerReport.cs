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

        public PlayerReport(string name, string biome, int? armor, string steamId)
        {
            Name = name;
            Biome = biome;
            Armor = armor;
            SteamId = steamId;
        }
    }
}
