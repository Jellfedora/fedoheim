using System;
using System.Reflection;
using HarmonyLib;
using Splatform;

namespace FedoServerTools
{
    // Résolution du SteamID64 d'un joueur connecté -- vérifié par reflection dump
    // contre le vrai assembly_valheim.dll (voir CLAUDE.md, "Notes techniques de
    // modding") : ZNet.GetPeerByPlayerName(string) et ZNetPeer.m_socket sont tous les
    // deux publics, pas besoin de reflection/AccessTools ici. ISocket.GetHostName() est
    // la même valeur que celle comparée par le jeu lui-même à adminlist.txt/
    // bannedlist.txt (voir ZNet.IsAdmin(string hostName)) -- le SteamID64 pour une
    // connexion Steam (ZSteamSocket), une chaîne différente (endpoint réseau) pour une
    // connexion directe par IP, qui ne correspondra simplement à aucun compte.
    //
    // Testé en jeu (host solo/co-op) : `GetPeerByPlayerName` ne trouve JAMAIS l'hôte
    // lui-même -- confirmé par désassemblage IL de `ZNet.UpdatePlayerList()`, qui ajoute
    // l'entrée de l'hôte à `m_players` directement depuis `Game.GetPlayerProfile()`,
    // jamais via `m_peers` (rempli uniquement par `OnNewConnection`, donc uniquement pour
    // de vraies connexions entrantes d'autres joueurs). Sans traitement à part, le perso
    // de l'hôte ne recevait donc jamais de `steamId` et ne se liait jamais au compte
    // Fedoheim -- voir `ResolveSelf` ci-dessous.
    internal static class PeerSteamId
    {
        // Splatform.PlatformManager n'expose son singleton que via un champ statique
        // privé (`s_distributionPlatform`) -- seul point de reflection nécessaire ici,
        // tout le reste (IDistributionPlatform/ILocalUser/IUser/PlatformUserID) est
        // public, référencé directement (voir FedoServerTools.csproj).
        private static readonly FieldInfo DistributionPlatformField =
            AccessTools.Field(typeof(PlatformManager), "s_distributionPlatform");

        public static string Resolve(string playerName)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                return null;
            }

            // L'hôte (nous-mêmes, en solo ou en partie hébergée) n'a pas de ZNetPeer le
            // représentant -- voir le commentaire de tête. On le détecte en comparant au
            // nom du profil actuellement chargé plutôt que de chercher un pair pour lui.
            string localName = Game.instance?.GetPlayerProfile()?.GetName();
            if (localName != null && localName == playerName)
            {
                return ResolveSelf();
            }

            if (ZNet.instance == null)
            {
                return null;
            }

            try
            {
                var peer = ZNet.instance.GetPeerByPlayerName(playerName);
                return peer?.m_socket?.GetHostName();
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogWarning($"FedoServerTools: SteamID resolution failed: {e.Message}");
                return null;
            }
        }

        private static string ResolveSelf()
        {
            try
            {
                var distributionPlatform = DistributionPlatformField?.GetValue(null) as IDistributionPlatform;
                PlatformUserID id = distributionPlatform?.LocalUser?.PlatformUserID ?? PlatformUserID.None;
                return id.TryParseAsUInt64(out ulong steamId64) ? steamId64.ToString() : null;
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogWarning($"FedoServerTools: local SteamID resolution failed: {e.Message}");
                return null;
            }
        }
    }
}
