using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FedoGuardian
{
    // Même principe que GuardianPrefabPatch, mais pour un objet : clone d'un item vanilla existant
    // (configurable, MaceEldner par défaut -- en attendant un vrai modèle dédié), renommé, et
    // enregistré dans ZNetScene.m_prefabs + ObjectDB.m_items avec le même Postfix auto-réparant
    // sur les méthodes de résolution par nom/hash.
    internal static class SummonWandPrefabPatch
    {
        public const string PrefabName = "FedoGuardian_SummonWand";

        private static readonly int PrefabHash = PrefabName.GetStableHashCode();

        private static GameObject _clone;

        // La comparaison par référence sur SharedData s'est révélée peu fiable : ObjectDB.Awake()
        // tourne plusieurs fois pendant le chargement (menu, scène de jeu, rechargement du monde),
        // et chaque tentative qui retrouve _clone à null en reconstruit un nouveau -- avec une
        // toute nouvelle SharedData à chaque fois. L'objet réellement tenu par le joueur peut donc
        // référencer une SharedData "périmée" par rapport à la dernière reconstruite. Le nom
        // affiché, lui, reste stable (toujours réappliqué depuis la même valeur de config) : on
        // identifie la baguette par ce nom plutôt que par identité d'objet.
        public static ItemDrop.ItemData.SharedData SharedData { get; private set; }

        public static bool IsWand(ItemDrop.ItemData item)
        {
            return item?.m_shared != null && item.m_shared.m_name == FedoGuardianPlugin.Instance.SummonWandName.Value;
        }

        public static GameObject GetPrefab()
        {
            return GetOrCreate();
        }

        private static GameObject BuildClone()
        {
            string sourceName = FedoGuardianPlugin.Instance.SummonWandSourceItem.Value;

            // ObjectDB en priorité : "MaceEldner" est un item, ObjectDB.instance.m_items est déjà
            // rempli au moment où le Postfix sur ObjectDB.Awake nous appelle. ZNetScene.instance
            // peut encore être null ou pas totalement initialisé à ce stade précis du chargement
            // (deux singletons distincts, ordre d'Awake non garanti) -- d'où le repli en second
            // recours seulement.
            GameObject source = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(sourceName) : null;
            if (source == null && ZNetScene.instance != null)
            {
                source = ZNetScene.instance.GetPrefab(sourceName);
            }

            if (source == null)
            {
                FedoGuardianPlugin.Log?.LogError($"FedoGuardian: prefab source '{sourceName}' introuvable, impossible de créer la baguette d'invocation.");
                LogSimilarItemNames(sourceName);
                return null;
            }

            // Enfant du conteneur racine désactivé : aucun script (ItemDrop.Awake compris) ne
            // s'exécute tant qu'il reste là -- cf. note dans GuardianPrefabPatch pour le pourquoi.
            var clone = UnityEngine.Object.Instantiate(source, FedoGuardianPlugin.TemplateRoot, worldPositionStays: false);
            clone.name = PrefabName;

            var itemDrop = clone.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                itemDrop.m_itemData.m_shared.m_name = FedoGuardianPlugin.Instance.SummonWandName.Value;
                itemDrop.m_itemData.m_shared.m_description = "Summons a FedoGuardian in front of you when used.";
                SharedData = itemDrop.m_itemData.m_shared;
            }

            return clone;
        }

        // Aide au diagnostic : si le nom configuré ne correspond à aucun prefab, on liste les
        // objets d'ObjectDB dont le nom contient un des "mots" du nom recherché (ex: "Mace"),
        // pour trouver le vrai nom sans avoir à fouiller les fichiers d'assets à la main.
        private static void LogSimilarItemNames(string sourceName)
        {
            if (ObjectDB.instance == null)
            {
                return;
            }

            var keywords = System.Text.RegularExpressions.Regex.Matches(sourceName, "[A-Z][a-z]*")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .Where(w => w.Length >= 3)
                .ToArray();

            if (keywords.Length == 0)
            {
                return;
            }

            var matches = ObjectDB.instance.m_items
                .Where(go => go != null && keywords.Any(k => go.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(go => go.name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            FedoGuardianPlugin.Log?.LogWarning(matches.Count > 0
                ? $"FedoGuardian: noms de prefabs ressemblants trouvés dans ObjectDB -> {string.Join(", ", matches)}"
                : $"FedoGuardian: aucun prefab ressemblant à '{sourceName}' trouvé dans ObjectDB.m_items.");
        }

        // Cf. commentaire équivalent dans GuardianPrefabPatch.GetOrCreate : appelée depuis des
        // Postfix sur des méthodes vanilla partagées (ObjectDB.Awake/GetItemPrefab, ZNetScene...),
        // une exception ici casserait toute la chaîne de patches Harmony dessus et peut bloquer le
        // chargement du monde entier.
        private static GameObject GetOrCreate()
        {
            if (_clone != null)
            {
                return _clone;
            }

            try
            {
                _clone = BuildClone();
                if (_clone == null)
                {
                    return null;
                }

                if (ZNetScene.instance != null && !ZNetScene.instance.m_prefabs.Contains(_clone))
                {
                    ZNetScene.instance.m_prefabs.Add(_clone);
                }

                if (ObjectDB.instance != null && !ObjectDB.instance.m_items.Contains(_clone))
                {
                    ObjectDB.instance.m_items.Add(_clone);
                }
            }
            catch (Exception e)
            {
                FedoGuardianPlugin.Log?.LogError($"FedoGuardian: échec de création de la baguette d'invocation : {e}");
                _clone = null;
            }

            return _clone;
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDbAwakePatch
        {
            private static void Postfix()
            {
                GetOrCreate();
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.GetPrefab), typeof(int))]
        private static class GetPrefabByHashPatch
        {
            private static void Postfix(int hash, ref GameObject __result)
            {
                if (__result != null || hash != PrefabHash)
                {
                    return;
                }

                __result = GetOrCreate();
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.GetPrefab), typeof(string))]
        private static class GetPrefabByNamePatch
        {
            private static void Postfix(string name, ref GameObject __result)
            {
                if (__result != null || name != PrefabName)
                {
                    return;
                }

                __result = GetOrCreate();
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.HasPrefab))]
        private static class HasPrefabPatch
        {
            private static void Postfix(int hash, ref bool __result)
            {
                if (__result || hash != PrefabHash)
                {
                    return;
                }

                __result = true;
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.GetItemPrefab), typeof(int))]
        private static class GetItemPrefabByHashPatch
        {
            private static void Postfix(int hash, ref GameObject __result)
            {
                if (__result != null || hash != PrefabHash)
                {
                    return;
                }

                __result = GetOrCreate();
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.GetItemPrefab), typeof(string))]
        private static class GetItemPrefabByNamePatch
        {
            private static void Postfix(string name, ref GameObject __result)
            {
                if (__result != null || name != PrefabName)
                {
                    return;
                }

                __result = GetOrCreate();
            }
        }

        // Résolution par SharedData -- probablement ce qu'utilise VisEquipment pour retrouver le
        // modèle visuel à accrocher en main. Basée sur un dictionnaire (m_itemByData) reconstruit
        // une seule fois par ObjectDB, jamais mis à jour pour un item ajouté après coup à m_items :
        // sans ce Postfix, l'objet reste fonctionnel (attaque, nom...) mais invisible en main.
        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.GetItemPrefab), typeof(ItemDrop.ItemData.SharedData))]
        private static class GetItemPrefabBySharedDataPatch
        {
            private static void Postfix(ItemDrop.ItemData.SharedData sharedData, ref GameObject __result)
            {
                if (__result != null || sharedData == null || sharedData != SharedData)
                {
                    return;
                }

                __result = GetOrCreate();
            }
        }
    }
}
