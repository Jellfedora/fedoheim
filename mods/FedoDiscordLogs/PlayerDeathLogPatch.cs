using System;
using System.Reflection;
using HarmonyLib;

namespace FedoDiscordLogs
{
    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class PlayerDeathLogPatch
    {
        // Character.m_lastHit est protégé, donc on passe par AccessTools pour le lire.
        private static readonly FieldInfo LastHitField = AccessTools.Field(typeof(Character), "m_lastHit");

        private static void Postfix(Player __instance)
        {
            try
            {
                if (__instance != Player.m_localPlayer)
                {
                    return;
                }

                var lastHit = (HitData)LastHitField?.GetValue(__instance);
                string cause = DescribeCause(lastHit);
                FedoDiscordLogsPlugin.Instance.OnPlayerDied(__instance.GetPlayerName(), cause);
            }
            catch (Exception e)
            {
                FedoDiscordLogsPlugin.Log?.LogError($"FedoDiscordLogs: OnDeath patch failed: {e}");
            }
        }

        private static string DescribeCause(HitData hit)
        {
            if (hit == null)
            {
                return "unknown causes";
            }

            switch (hit.m_hitType)
            {
                case HitData.HitType.Drowning:
                    return "drowning";
                case HitData.HitType.Fall:
                    return "fall damage";
                case HitData.HitType.Burning:
                case HitData.HitType.CinderFire:
                    return "fire";
                case HitData.HitType.Freezing:
                    return "the cold";
                case HitData.HitType.Poisoned:
                    return "poison";
                case HitData.HitType.EdgeOfWorld:
                    return "the edge of the world";
                case HitData.HitType.Tree:
                    return "a falling tree";
                case HitData.HitType.Cart:
                    return "a cart";
                case HitData.HitType.Boat:
                    return "a boat";
                case HitData.HitType.Turret:
                    return "a turret";
                case HitData.HitType.Catapult:
                    return "a catapult";
                case HitData.HitType.Stalagtite:
                    return "a falling stalactite";
                case HitData.HitType.Water:
                case HitData.HitType.AshlandsOcean:
                    return "the sea";
                case HitData.HitType.Smoke:
                    return "smoke inhalation";
                case HitData.HitType.EnemyHit:
                case HitData.HitType.PlayerHit:
                    return DescribeAttacker(hit);
                default:
                    return "unknown causes";
            }
        }

        private static string DescribeAttacker(HitData hit)
        {
            // GetHoverName() renvoie déjà un nom localisé selon la langue du jeu.
            var attacker = hit.GetAttacker();
            return attacker != null ? attacker.GetHoverName() : "an unknown attacker";
        }
    }
}
