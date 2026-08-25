using System;
using HarmonyLib;

namespace FedoServerTools
{
    // Message ponctuel posé par un admin depuis le launcher (Admin > Serveur, voir
    // ServerCommands.cs) et affiché au centre de l'écran de chaque joueur connecté, ainsi
    // que dans son tchat en jeu. MessageHud.ShowMessage/Chat.OnNewChatMessage seules
    // n'affichent qu'en local (voir l'usage de MessageHud dans FedoDeath/FedoGoldRabbit)
    // -- il faut un aller-retour réseau pour atteindre les autres clients, d'où cette RPC
    // dédiée plutôt qu'un appel direct côté serveur.
    internal static class BroadcastMessage
    {
        private const string RpcName = "FedoServerTools BroadcastMessage";

        // Enregistrée sur CHAQUE instance (serveur comme client) dès que ZRoutedRpc
        // existe -- même point d'accroche que ServerSync (mods/_shared/ConfigSync.cs,
        // RegisterRPCPatch) : un nom de RPC doit avoir un gestionnaire enregistré des
        // deux côtés pour qu'un message routé trouve preneur à l'arrivée, pas seulement
        // côté serveur qui l'envoie.
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class RegisterRpcPatch
        {
            private static void Postfix()
            {
                try
                {
                    ZRoutedRpc.instance?.Register<string>(RpcName, RPC_ShowMessage);
                }
                catch (Exception e)
                {
                    FedoServerToolsPlugin.Log?.LogError($"FedoServerTools: failed to register {RpcName} RPC: {e}");
                }
            }
        }

        // Appelé côté serveur seulement (voir ServerCommands.ApplyFromReportResponse) --
        // ZRoutedRpc.Everybody atteint aussi l'hôte lui-même en partie solo/hébergée, pas
        // seulement les pairs distants.
        public static void Send(string message)
        {
            if (ZRoutedRpc.instance == null)
            {
                FedoServerToolsPlugin.Log?.LogWarning("FedoServerTools: cannot broadcast admin message, ZRoutedRpc not ready.");
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, message);
        }

        // Exécuté sur chaque client (et l'hôte) à réception -- centré, en jaune (balise
        // de rich text TextMeshPro, MessageHud la rend nativement) pour se distinguer des
        // messages système habituels (ramassage d'objet, etc), et posté dans le tchat.
        private static void RPC_ShowMessage(long sender, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, $"<color=yellow>{message}</color>");
            }

            ShowInChat(message);
        }

        // Ajoute le message dans le tchat de CE client via Chat.OnNewChatMessage --
        // méthode publique d'affichage utilisée en interne par le jeu à la réception de
        // sa propre RPC "ChatMessage" (voir RPC_ChatMessage, privée, sur laquelle celle-ci
        // est calquée par reflection dump, voir mods/CLAUDE.md) -- jamais Chat.SendText
        // (utilisée ailleurs dans ce repo, ex. FedoDeath), qui émettrait elle-même une
        // NOUVELLE RPC réseau depuis chaque client qui la reçoit, provoquant une tempête
        // de messages en cascade. Ancré sur le joueur local (position + GameObject) --
        // seul repère garanti valide sur ce client, aucun "vrai" émetteur réseau
        // n'existant pour ce message. `Talker.Type.Shout` : même style que les messages
        // de mort (voir FedoDeathPlugin), affiché en évidence dans le tchat.
        private static void ShowInChat(string message)
        {
            if (Chat.instance == null || Player.m_localPlayer == null)
            {
                return;
            }

            try
            {
                // Un UserInfo tout neuf (UserId par défaut/vide) fait échouer une
                // vérification de permission de communication texte côté plateforme
                // (observé en jeu : "Failed to check permission
                // CommunicateWithUsingText: UserID was invalid", loggué par Unity mais
                // sans bloquer l'affichage) -- on réutilise donc le PlatformUserID du
                // joueur local lui-même (toujours valide) tout en gardant le nom
                // "Fedoheim" affiché, plutôt que de muter l'instance partagée renvoyée
                // par UserInfo.GetLocalUser().
                var sender = new UserInfo { Name = "Fedoheim", UserId = UserInfo.GetLocalUser().UserId };
                Chat.instance.OnNewChatMessage(
                    Player.m_localPlayer.gameObject,
                    0L,
                    Player.m_localPlayer.transform.position,
                    Talker.Type.Shout,
                    sender,
                    message);
            }
            catch (Exception e)
            {
                FedoServerToolsPlugin.Log?.LogWarning($"FedoServerTools: failed to show admin message in chat: {e.Message}");
            }
        }
    }
}
