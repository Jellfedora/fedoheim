using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace FedoKnorri
{
    // Item custom cloné via Jotunn (PrefabManager + ItemManager) plutôt que par les patches
    // Harmony "auto-réparants" documentés dans mods/CLAUDE.md (Postfix maison sur
    // ZNetScene.Awake/GetPrefab/HasPrefab + ObjectDB.Awake/GetItemPrefab, quatre surcharges
    // comprises) : ItemManager.AddItem s'occupe lui-même d'enregistrer le prefab dans
    // ObjectDB.m_items (et ses dictionnaires internes de résolution par hash/nom/SharedData,
    // jamais mis à jour côté vanilla pour un item ajouté après coup -- exactement ce que ces
    // Postfix contournaient à la main) au bon moment du chargement.
    //
    // Point d'entrée : PrefabManager.OnVanillaPrefabsAvailable (ItemManager.OnVanillaItemsAvailable
    // existe aussi mais est marqué [Obsolete] par Jotunn au profit de celui-ci) -- se déclenche
    // aussi bien au menu principal (avant qu'un ZNetScene existe) qu'au chargement réel d'une
    // partie, d'où le même besoin de retenter tant que _clone reste null (voir CreateItem).
    // ItemManager.AddItem, appelé une seule fois ci-dessous dès que le clone réussit, se charge
    // ensuite lui-même de rejouer l'enregistrement dans ObjectDB à chaque rechargement suivant.
    internal static class SummonItemPrefabPatch
    {
        public const string PrefabName = "Fedo_KnorriCharm";

        // Icône custom (voir FedoKnorriPlugin.SummonItemName) déployée à côté de la DLL par
        // le .csproj (CopyToPlugins), même mécanisme que shiny.mp3/healing.mp3. Remplace
        // uniquement l'icône affichée dans l'inventaire -- le modèle 3D en main/au sol reste
        // celui de SummonItemSourceItem tant qu'aucun vrai modèle dédié n'existe.
        // Extension ".jpg" obligatoire -- AssetUtils.LoadTexture (utilisée par
        // LoadSpriteFromFile) vérifie l'EXTENSION du fichier, pas son contenu, et lève
        // "LoadTexture can only load png or jpg textures" pour un ".jpeg" (vécu en jeu :
        // exception non rattrapée dans LoadIcon(), qui faisait échouer TOUTE la création de
        // l'item -- voir le try/catch autour de l'appel ci-dessous).
        private const string IconFileName = "knorri_seed.jpg";

        private static GameObject _clone;

        // Identifié par le nom FIGÉ AU MOMENT DE LA CRÉATION du prefab, mis en cache ici --
        // pas en relisant SummonItemName.Value à chaque appel (l'ancien bug : les .cfg se
        // rechargent à chaud, voir CLAUDE.md, donc renommer l'item pendant que le serveur
        // tourne cassait silencieusement la reconnaissance de toutes les graines déjà en
        // circulation), et pas non plus par identité d'objet SharedData (essayé, puis
        // abandonné -- vécu en jeu : un exemplaire obtenu via Easy Spawner/additem ne
        // partage PAS forcément la même instance SharedData que celle créée dans CreateItem,
        // sans doute copiée par valeur quelque part dans le pipeline vanilla plutôt que
        // partagée par référence -- la comparaison échouait alors TOUJOURS, l'item retombait
        // dans la vraie logique Consumable vanilla et se consommait pour rien, sans jamais
        // invoquer le compagnon). Une chaîne mise en cache une fois n'a aucun de ces deux
        // problèmes : stable quel que soit le chemin d'obtention de l'item, insensible à un
        // renommage ultérieur du .cfg.
        private static string _createdWithName;

        public static bool IsSummonItem(ItemDrop.ItemData item)
        {
            return _createdWithName != null && item?.m_shared != null && item.m_shared.m_name == _createdWithName;
        }

        public static GameObject GetPrefab()
        {
            return _clone;
        }

        // Appelée une fois depuis FedoKnorriPlugin.Awake -- ne fait que s'abonner, la
        // construction elle-même attend qu'ObjectDB/ZNetScene existent (voir CreateItem).
        public static void Init()
        {
            PrefabManager.OnVanillaPrefabsAvailable += CreateItem;
        }

        // Rappelée à chaque fois qu'ObjectDB redevient disponible (menu principal ET/OU
        // chargement réel d'une partie, cf. commentaire de classe) -- une exception ici
        // couperait la diffusion de l'événement Jotunn aux autres abonnés éventuels (délégué
        // multicast), d'où le try/catch, même principe défensif que documenté dans l'ancienne
        // version pour les Postfix Harmony partagés.
        private static void CreateItem()
        {
            if (_clone != null)
            {
                return;
            }

            try
            {
                string sourceName = FedoKnorriPlugin.Instance.SummonItemSourceItem.Value;

                GameObject clone = PrefabManager.Instance.CreateClonedPrefab(PrefabName, sourceName);
                if (clone == null)
                {
                    FedoKnorriPlugin.Log?.LogError($"FedoKnorri: prefab source '{sourceName}' introuvable, impossible de créer l'item d'invocation.");
                    LogSimilarItemNames(sourceName);
                    return;
                }

                var itemDrop = clone.GetComponent<ItemDrop>();
                if (itemDrop == null)
                {
                    FedoKnorriPlugin.Log?.LogError($"FedoKnorri: '{sourceName}' n'a pas de composant ItemDrop, impossible d'en faire un item d'invocation.");
                    return;
                }

                itemDrop.m_itemData.m_shared.m_name = FedoKnorriPlugin.Instance.SummonItemName.Value;
                itemDrop.m_itemData.m_shared.m_description = FedoKnorriPlugin.Instance.SummonItemDescription.Value;
                _createdWithName = itemDrop.m_itemData.m_shared.m_name;

                // Forcé en Consumable quel que soit le type d'origine (Trophy par défaut n'a
                // pas de bouton "Utiliser" dans l'inventaire vanilla) : c'est ce type qui
                // garantit l'apparition de ce bouton, peu importe l'item source choisi comme
                // simple support visuel.
                itemDrop.m_itemData.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;

                Sprite icon = LoadIcon();
                if (icon != null)
                {
                    itemDrop.m_itemData.m_shared.m_icons = new[] { icon };
                }

                SummonItemSparkleEffect.Attach(clone);
                ApplyPurpleTint(clone);

                // fixReference: false -- le clone référence déjà de vrais assets vanilla
                // (hérités de sourceName), pas des Mock<T> Jotunn à résoudre.
                ItemManager.Instance.AddItem(new CustomItem(clone, fixReference: false));

                _clone = clone;
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: échec de création de l'item d'invocation : {e}");
            }
        }

        // AssetUtils.LoadSpriteFromFile lit le fichier (PNG/JPG uniquement -- vérifié par
        // EXTENSION, pas par contenu, voir le commentaire sur IconFileName) et en fait un
        // Sprite directement, sans passer par la coroutine UnityWebRequest utilisée ailleurs
        // dans ce mod pour l'audio (pas de format compressé à streamer ici, juste une image
        // locale). Son propre try/catch, séparé de celui de CreateItem : une icône ratée
        // (fichier absent, mauvaise extension, image corrompue...) ne doit jamais empêcher
        // l'item lui-même d'exister -- juste garder l'icône vanilla de secours -- vécu en jeu
        // avec la mauvaise extension avant ce correctif : l'exception non rattrapée ici
        // remontait jusqu'au try/catch de CreateItem, qui abandonnait la création de l'item
        // ENTIER (jamais enregistré du tout) pour un problème purement cosmétique.
        private static Sprite LoadIcon()
        {
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = Path.Combine(dllDir ?? "", IconFileName);
                if (!File.Exists(path))
                {
                    FedoKnorriPlugin.Log?.LogWarning($"FedoKnorri: '{IconFileName}' introuvable à côté de la DLL, l'item d'invocation gardera l'icône vanilla de '{FedoKnorriPlugin.Instance.SummonItemSourceItem.Value}'.");
                    return null;
                }

                Sprite sprite = AssetUtils.LoadSpriteFromFile(path);
                if (sprite == null)
                {
                    FedoKnorriPlugin.Log?.LogWarning($"FedoKnorri: échec du chargement de '{IconFileName}' en icône, l'item d'invocation gardera l'icône vanilla de '{FedoKnorriPlugin.Instance.SummonItemSourceItem.Value}'.");
                }

                return sprite;
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogError($"FedoKnorri: échec du chargement de '{IconFileName}' en icône, l'item d'invocation gardera l'icône vanilla de '{FedoKnorriPlugin.Instance.SummonItemSourceItem.Value}' : {e}");
                return null;
            }
        }

        // Teinte le modèle 3D vanilla hérité de SummonItemSourceItem vers le violet, en
        // attendant un vrai modèle dédié -- cohérent avec l'icône et les particules
        // (SummonItemSparkleEffect). Renderer.material (pas .sharedMaterial) instancie
        // automatiquement une copie du matériau propre à ce renderer au premier accès --
        // jamais l'asset original partagé par d'autres objets utilisant le même matériau.
        //
        // Ni "_Color" ni "_BaseColor" (essayés en premier) n'ont d'effet visible en jeu -- les
        // shaders custom de Valheim pour les items/props n'exposent apparemment aucun des deux
        // sous ce nom. Plutôt que deviner un troisième nom au hasard, on interroge le shader
        // lui-même (Shader.GetPropertyCount/GetPropertyType, réflexion de shader native Unity,
        // pas de la réflexion .NET) pour trouver TOUTES ses propriétés de type Couleur et les
        // teinter, quel que soit leur nom réel -- sauf celles qui ressemblent à de l'émission,
        // qu'on ne veut surtout pas transformer en halo lumineux inattendu.
        private static readonly Color PrefabTintColor = new Color(0.55f, 0.3f, 0.85f);

        private static void ApplyPurpleTint(GameObject root)
        {
            try
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
                FedoKnorriPlugin.Log?.LogInfo($"FedoKnorri: teinte violette -- {renderers.Length} renderer(s) trouvé(s) sur l'item d'invocation.");

                foreach (var meshRenderer in renderers)
                {
                    Material material = meshRenderer.material;
                    Shader shader = material.shader;
                    bool tinted = false;

                    int propertyCount = shader.GetPropertyCount();
                    for (int i = 0; i < propertyCount; i++)
                    {
                        if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color)
                        {
                            continue;
                        }

                        string propName = shader.GetPropertyName(i);
                        if (propName.IndexOf("Emission", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        Color before = material.GetColor(propName);
                        Color after = Color.Lerp(before, PrefabTintColor, 0.6f);
                        material.SetColor(propName, after);
                        tinted = true;
                        FedoKnorriPlugin.Log?.LogInfo($"FedoKnorri: '{meshRenderer.name}' (shader '{shader.name}') teinté via '{propName}' : {before} -> {after}.");
                    }

                    if (!tinted)
                    {
                        FedoKnorriPlugin.Log?.LogWarning($"FedoKnorri: aucune propriété de couleur exploitable trouvée sur le shader '{shader.name}' ({meshRenderer.name}), l'item d'invocation gardera son apparence vanilla d'origine.");
                    }
                }
            }
            catch (Exception e)
            {
                FedoKnorriPlugin.Log?.LogWarning($"FedoKnorri: échec de la teinte violette de l'item d'invocation : {e}");
            }
        }

        // Aide au diagnostic : si le nom configuré ne correspond à aucun prefab, on liste les
        // objets d'ObjectDB dont le nom contient un des "mots" du nom recherché, pour trouver
        // le vrai nom sans avoir à fouiller les fichiers d'assets à la main.
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
                .Where(go => go != null && keywords.Any(k => go.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(go => go.name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            FedoKnorriPlugin.Log?.LogWarning(matches.Count > 0
                ? $"FedoKnorri: noms de prefabs ressemblants trouvés dans ObjectDB -> {string.Join(", ", matches)}"
                : $"FedoKnorri: aucun prefab ressemblant à '{sourceName}' trouvé dans ObjectDB.m_items.");
        }
    }
}
