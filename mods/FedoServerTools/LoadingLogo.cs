using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace FedoServerTools
{
    // Logo Fedoheim de l'écran de chargement (voir LoadingOverlay.cs), livré en fichier
    // brut à côté de la DLL (voir CopyToPlugins du .csproj, même mécanique que le mp3
    // custom documenté dans mods/CLAUDE.md), décodé directement depuis ses octets PNG en
    // Texture2D (`Texture2D.LoadImage`) -- aucun import Unity nécessaire.
    //
    // Une police custom (Cinzel, celle du titre de la page d'accueil du launcher) a été
    // tentée ici via `Font.CreateDynamicFontFromOSFont` après enregistrement du .ttf
    // auprès de l'OS pour ce process uniquement -- abandonné après un vrai test en jeu :
    // l'OS résolvait bien le nom de la police, mais le moteur de rendu de police d'Unity
    // (FreeType) échouait quand même à charger ses données ("Unable to load font face"),
    // vraisemblablement parce qu'un enregistrement process-only n'est pas visible du
    // mécanisme de résolution de fichier interne d'Unity. L'écran de chargement utilise
    // donc la police déjà empruntée au jeu (voir LoadingOverlay.cs), comme avant.
    internal static class LoadingLogo
    {
        private const string LogoFileName = "fedoheim-logo.png";

        private static Texture2D _logoTexture;
        private static bool _logoAttempted;

        public static Texture2D Get()
        {
            if (_logoAttempted)
            {
                return _logoTexture;
            }

            _logoAttempted = true;

            try
            {
                string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = Path.Combine(pluginDirectory ?? "", LogoFileName);
                if (!File.Exists(path))
                {
                    FedoServerToolsPlugin.Log?.LogWarning(
                        $"FedoServerTools: {LogoFileName} not found next to the plugin, loading screen shows no logo.");
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }

                _logoTexture = tex;
                return _logoTexture;
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: Fedoheim logo loading failed: {e}");
                return null;
            }
        }
    }
}
