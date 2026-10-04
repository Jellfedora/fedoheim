using System;
using HarmonyLib;

namespace FedoFreeDeathCam
{
    // À la mort, active le mode "vol libre" déjà présent dans le jeu
    // (`GameCamera.m_freeFly`, normalement accessible seulement via le mode debug --
    // `ToggleFreeFly()`/`InFreeFly()` sont publiques, tout le reste vérifié par
    // décompilation) : sans ça, `GameCamera.UpdateCamera()` force la caméra à fixer le
    // cadavre du personnage en boucle (`base.transform.LookAt(ragdoll.
    // GetAverageBodyPosition())`) sans jamais mettre à jour sa position, la laissant
    // figée pile là où elle était au moment de la mort. Une fois `m_freeFly` actif, le
    // jeu prend le relais tout seul (WASD + souris pour voler, molette pour la vitesse --
    // exactement le même contrôle que le vol libre de debug), rien d'autre à faire ici.
    //
    // Repassé à `false` au respawn -- jamais si le joueur avait déjà activé le vol libre
    // lui-même AVANT de mourir (mode debug), pour ne pas lui couper une fonctionnalité
    // qu'il a choisie de son côté.
    internal static class FreeDeathCamera
    {
        private static bool _enabledByUs;

        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class OnDeathPatch
        {
            private static void Postfix(Player __instance)
            {
                try
                {
                    if (__instance != Player.m_localPlayer)
                    {
                        return;
                    }

                    if (FedoFreeDeathCamPlugin.Instance == null || !FedoFreeDeathCamPlugin.Instance.EnableFreeDeathCam)
                    {
                        return;
                    }

                    var camera = GameCamera.instance;
                    if (camera == null || GameCamera.InFreeFly())
                    {
                        return;
                    }

                    camera.ToggleFreeFly();
                    _enabledByUs = true;
                }
                catch (Exception e)
                {
                    FedoFreeDeathCamPlugin.Log?.LogWarning($"FedoFreeDeathCam: enabling free-fly on death failed: {e.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class OnSpawnedPatch
        {
            private static void Postfix(Player __instance)
            {
                try
                {
                    if (__instance != Player.m_localPlayer || !_enabledByUs)
                    {
                        return;
                    }

                    var camera = GameCamera.instance;
                    if (camera != null && GameCamera.InFreeFly())
                    {
                        camera.ToggleFreeFly();
                    }

                    _enabledByUs = false;
                }
                catch (Exception e)
                {
                    FedoFreeDeathCamPlugin.Log?.LogWarning($"FedoFreeDeathCam: restoring the normal camera on respawn failed: {e.Message}");
                }
            }
        }
    }
}
