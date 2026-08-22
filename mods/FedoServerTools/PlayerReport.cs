namespace FedoServerTools
{
    // Nom du biome envoyé tel quel (`Heightmap.Biome.ToString()`, ex: "Meadows",
    // "BlackForest") -- au launcher de le traduire pour l'affichage, pas au mod de
    // décider dans quelle langue.
    public readonly struct PlayerReport
    {
        public string Name { get; }
        public string Biome { get; }

        public PlayerReport(string name, string biome)
        {
            Name = name;
            Biome = biome;
        }
    }
}
