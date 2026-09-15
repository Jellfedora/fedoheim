using UnityEngine;

namespace FedoSignColor
{
    // Textures générées par code pour le picker (carré saturation/luminosité + bande de
    // teinte) -- aucune dépendance à un sprite du jeu.
    internal static class ColorWheelUtil
    {
        // Bande verticale : haut = teinte 0, bas = teinte 1 (parcourt tout le spectre).
        public static Texture2D CreateHueTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float hue = 1f - y / (float)(height - 1);
                var color = Color.HSVToRGB(hue, 1f, 1f);
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // Carré : X = saturation (0 gauche -> 1 droite), Y = luminosité (0 bas -> 1 haut),
        // pour une teinte donnée -- régénéré à chaque changement de teinte.
        public static Texture2D CreateSaturationValueTexture(float hue, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float value = y / (float)(size - 1);
                for (int x = 0; x < size; x++)
                {
                    float saturation = x / (float)(size - 1);
                    pixels[y * size + x] = Color.HSVToRGB(hue, saturation, value);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
