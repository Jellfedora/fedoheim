using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace FedoServerTools
{
    // Déclenche réellement la connexion (host d'un monde local ou join d'un serveur
    // dédié) via les mêmes méthodes que les boutons vanilla du menu -- vérifiées par
    // reflection dump contre le vrai assembly_valheim.dll (voir CLAUDE.md), MAIS jamais
    // testées en conditions réelles (le jeu n'a pas pu être lancé avec ce patch pendant
    // son écriture -- à valider sur une vraie partie avant de considérer ce fichier
    // fiable, en particulier l'enchaînement immédiat SetSelectedWorld -> OnWorldStart
    // et SetServerToJoin -> JoinServer sans navigation UI intermédiaire).
    internal static class AutoConnect
    {
        // m_worlds/SetSelectedWorld sont privés sur FejdStartup -- même style de
        // reflection que le reste du repo (voir ZNetJoinLeaveAnnouncePatches.cs).
        private static readonly FieldInfo WorldsField = AccessTools.Field(typeof(FejdStartup), "m_worlds");
        private static readonly MethodInfo SetSelectedWorldMethod = AccessTools.Method(typeof(FejdStartup), "SetSelectedWorld");

        // FejdStartup.ServerPassword n'a qu'un setter privé (le jeu le remplit depuis le
        // champ de saisie du mot de passe, jamais depuis l'extérieur) -- on écrit donc
        // directement le champ de stockage (backing field) plutôt que la propriété.
        private static readonly FieldInfo ServerPasswordBackingField =
            AccessTools.Field(typeof(FejdStartup), "<ServerPassword>k__BackingField");

        public static void Connect(FejdStartup instance, AutoConnectTarget target)
        {
            if (target.Type == "world")
            {
                ConnectToWorld(instance, target.World);
            }
            else if (target.Type == "server")
            {
                ConnectToServer(instance, target.Host, target.Port, target.Password);
            }
        }

        private static void ConnectToWorld(FejdStartup instance, string worldName)
        {
            if (WorldsField == null || SetSelectedWorldMethod == null)
            {
                FedoServerToolsPlugin.Log?.LogWarning("FedoServerTools: world selection API not found.");
                return;
            }

            if (!(WorldsField.GetValue(instance) is List<World> worlds))
            {
                FedoServerToolsPlugin.Log?.LogWarning("FedoServerTools: could not read the local world list.");
                return;
            }

            // Insensible à la casse : la cible est un champ texte libre côté launcher
            // (page Profils), pas une liste des mondes réels -- rien n'empêche un admin
            // de la taper avec une casse légèrement différente du nom réel du monde
            // (ex. "Fedodev3" vs "fedodev3"), observé en conditions réelles.
            int index = worlds.FindIndex(w => string.Equals(w.m_name, worldName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                FedoServerToolsPlugin.Log?.LogWarning($"FedoServerTools: world '{worldName}' not found locally -- falling back to the normal menu.");
                return;
            }

            SetSelectedWorldMethod.Invoke(instance, new object[] { index, false });
            instance.OnWorldStart();
        }

        private static void ConnectToServer(FejdStartup instance, string host, int port, string password)
        {
            if (ServerPasswordBackingField != null)
            {
                ServerPasswordBackingField.SetValue(null, password ?? "");
            }

            instance.SetServerToJoin(new ServerJoinData(new ServerJoinDataDedicated(host, (ushort)port)));
            instance.JoinServer();
        }
    }
}
