using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace FedoServerTools
{
    // Filet de sécurité générique, pas spécifique à un mod donné : ZNetScene.RemoveObjects
    // plante en boucle (NullReferenceException à CHAQUE frame, indéfiniment -- observé en jeu,
    // dès le chargement d'une sauvegarde) si une entrée de son dictionnaire privé m_instances
    // pointe vers un ZNetView déjà détruit ou dont la ZDO a été mise à null sans que l'entrée
    // correspondante ait été retirée. C'est un type de corruption ZDO/instance déjà documenté
    // dans l'écosystème de mods Valheim (voir par ex. ASharpPen/Valheim.LessZdoZoneCorruption),
    // pas quelque chose qu'on peut empêcher a priori sans savoir laquelle des dizaines de
    // milliers de ZDO d'une sauvegarde en est la cause exacte.
    //
    // Ce Prefix répare l'incohérence juste avant que la méthode d'origine ne s'exécute (retire
    // l'entrée cassée du dictionnaire, et réinitialise `Created` sur sa ZDO pour lui laisser une
    // chance de se recréer proprement à la frame suivante) et logge le nom du GameObject
    // concerné, pour identifier le vrai coupable si ça se reproduit.
    internal static class ZNetSceneStabilityPatch
    {
        private static readonly FieldInfo InstancesField = AccessTools.Field(typeof(ZNetScene), "m_instances");

        [HarmonyPatch(typeof(ZNetScene), "RemoveObjects")]
        private static class RemoveObjectsGuard
        {
            private static void Prefix(ZNetScene __instance)
            {
                var raw = InstancesField?.GetValue(__instance);
                if (!(raw is Dictionary<ZDO, ZNetView> instances))
                {
                    return;
                }

                List<ZDO> broken = null;
                foreach (var kvp in instances)
                {
                    if (kvp.Value == null || kvp.Value.GetZDO() == null)
                    {
                        (broken ?? (broken = new List<ZDO>())).Add(kvp.Key);
                    }
                }

                if (broken == null)
                {
                    return;
                }

                foreach (var zdo in broken)
                {
                    string label = "<unreadable>";
                    try
                    {
                        var view = instances[zdo];
                        label = view != null
                            ? $"{view.name} (prefab hash {zdo.GetPrefab()})"
                            : $"<destroyed> (prefab hash {zdo.GetPrefab()})";
                    }
                    catch
                    {
                        // Purement informatif -- ne doit jamais empêcher la réparation ci-dessous.
                    }

                    FedoServerToolsPlugin.Log?.LogWarning(
                        $"FedoServerTools: repaired a broken ZNetScene instance ({label}) that would otherwise crash RemoveObjects every frame.");

                    zdo.Created = false;
                    instances.Remove(zdo);
                }
            }
        }
    }
}
