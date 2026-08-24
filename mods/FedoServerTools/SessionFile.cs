using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace FedoServerTools
{
    // Cible d'auto-connexion résolue pour le profil actif (voir GET
    // /modpacks/:slug/manifest côté API) -- soit un monde local à héberger, soit un
    // serveur dédié à rejoindre.
    internal class AutoConnectTarget
    {
        public string Type; // "world" ou "server"
        public string World;
        public string Host;
        public int Port;
        public string Password;
    }

    // Écrit par le launcher juste avant de lancer le jeu, jamais synchronisé/zippé
    // comme le contenu d'un mod (même logique que ServerToken ci-dessus, voir
    // CLAUDE.md) -- <profil>/BepInEx/fedoheim-session.txt. Format "clé=valeur" une
    // ligne par champ plutôt que du JSON : aucun mod de ce repo n'a de dépendance de
    // parsing JSON (voir OnlinePlayersReporter.cs, qui écrit du JSON à la main sans
    // jamais en lire), ce format se parse de façon fiable sans dépendance externe.
    internal class SessionFile
    {
        public string CharacterName;
        // Pseudo Discord du compte connecté -- utilisé uniquement pour pré-remplir le
        // champ de nom à la création de perso (voir FejdStartupAutoNavigatePatch),
        // jamais pour la logique de liaison compte<->perso elle-même (ça reste
        // `CharacterName`, décidé côté API).
        public string DiscordUsername;
        public AutoConnectTarget AutoConnect;

        // Le dossier de travail du jeu au lancement n'est pas forcément le profil
        // externe (voir valheim.rs::profile_dir, notamment sur Windows où c'est
        // l'install Steam) -- on remonte donc depuis l'emplacement de cette DLL
        // (BepInEx/plugins/FedoServerTools/…) plutôt que d'utiliser un chemin courant.
        public static SessionFile LoadNearPlugin()
        {
            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                // .../BepInEx/plugins/FedoServerTools -> .../BepInEx
                string bepinexDir = Path.GetDirectoryName(Path.GetDirectoryName(pluginDir));
                if (bepinexDir == null)
                {
                    return null;
                }

                return Load(Path.Combine(bepinexDir, "fedoheim-session.txt"));
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogWarning($"FedoServerTools: could not resolve session file path: {e.Message}");
                return null;
            }
        }

        private static SessionFile Load(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var values = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            string Get(string key) => values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

            var session = new SessionFile
            {
                CharacterName = Get("character_name"),
                DiscordUsername = Get("discord_username"),
            };

            string type = Get("auto_connect_type");
            string world = Get("auto_connect_world");
            string host = Get("auto_connect_host");
            if (type == "world" && world != null)
            {
                session.AutoConnect = new AutoConnectTarget { Type = "world", World = world };
            }
            else if (type == "server" && host != null && int.TryParse(Get("auto_connect_port"), out var port))
            {
                session.AutoConnect = new AutoConnectTarget
                {
                    Type = "server",
                    Host = host,
                    Port = port,
                    Password = Get("auto_connect_password") ?? "",
                };
            }

            return session;
        }
    }
}
