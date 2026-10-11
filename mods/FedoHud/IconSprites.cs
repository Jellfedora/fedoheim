using UnityEngine;

namespace FedoHud
{
    // Sprites générés par code, réutilisés par FedoHudSettingsPanel.cs (ronds façon
    // bouton radio) et RecipeTracker.cs (icône d'épingle) -- aucune dépendance à un
    // sprite du jeu, garanti de s'afficher.
    internal static class IconSprites
    {
        // Croix "x" pleine largeur (coin à coin) -- distance d'un pixel à chacune des
        // deux diagonales du carré, rempli si assez proche de l'une ou l'autre. Utilisée
        // par RecipeTrackerOverlay.cs pour le bouton de désépinglage.
        public static Sprite CreateCross(int size, float thicknessFrac, Color color)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float thickness = size * thicknessFrac;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f) - center;
                    float distToDiag1 = Mathf.Abs(p.x - p.y) / Mathf.Sqrt(2f);
                    float distToDiag2 = Mathf.Abs(p.x + p.y) / Mathf.Sqrt(2f);
                    bool onCross = distToDiag1 <= thickness / 2f || distToDiag2 <= thickness / 2f;
                    pixels[y * size + x] = onCross ? color : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        // Dessine un anneau (innerFrac > 0) ou un disque plein (innerFrac == 0) dans une
        // texture carrée.
        public static Sprite CreateCircle(int size, float innerFrac, float outerFrac, Color color)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outerRadius = size / 2f * outerFrac;
            float innerRadius = size / 2f * innerFrac;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    pixels[y * size + x] = distance <= outerRadius && distance >= innerRadius ? color : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        // Étoile à `points` branches (2*points sommets, rayon alternant
        // outerRadiusFrac/innerRadiusFrac), remplie par un test point-dans-polygone
        // classique (ray casting) -- utilisée par RecipeTracker.cs pour l'icône
        // d'épingle.
        public static Sprite CreateStar(int size, int points, float innerRadiusFrac, float outerRadiusFrac, Color color)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = new Vector2(size / 2f, size / 2f);
            float outerRadius = size / 2f * outerRadiusFrac;
            float innerRadius = size / 2f * innerRadiusFrac;

            int vertexCount = points * 2;
            var vertices = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                // -π/2 pour faire pointer la première branche vers le haut.
                float angle = i * Mathf.PI / points - Mathf.PI / 2f;
                float radius = (i % 2 == 0) ? outerRadius : innerRadius;
                vertices[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    pixels[y * size + x] = IsPointInPolygon(point, vertices) ? color : Color.clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        // Test point-dans-polygone par ray casting (pair-impair) -- standard, fonctionne
        // pour n'importe quel polygone simple (convexe ou non, donc une étoile aussi).
        private static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            int j = polygon.Length - 1;
            for (int i = 0; i < polygon.Length; i++)
            {
                bool crosses = (polygon[i].y > point.y) != (polygon[j].y > point.y);
                if (crosses)
                {
                    float xIntersect = (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y)
                        / (polygon[j].y - polygon[i].y) + polygon[i].x;
                    if (point.x < xIntersect)
                    {
                        inside = !inside;
                    }
                }

                j = i;
            }

            return inside;
        }
    }
}
