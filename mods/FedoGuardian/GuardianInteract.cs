using System;
using System.Linq;
using UnityEngine;

namespace FedoGuardian
{
    // Équivalent simplifié d'un mannequin (Ashlands) : pas de fenêtre d'inventaire dédiée, le
    // joueur porte lui-même l'équipement qu'il veut donner au garde puis clic droit dessus pour
    // le transférer d'un coup. Alt+clic droit fait l'inverse (rend tout l'équipement du garde).
    public class GuardianInteract : MonoBehaviour, Interactable
    {
        private static readonly ItemDrop.ItemData.ItemType[] SwappableArmorTypes =
        {
            ItemDrop.ItemData.ItemType.Helmet,
            ItemDrop.ItemData.ItemType.Chest,
            ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Shoulder,
            ItemDrop.ItemData.ItemType.Utility,
            ItemDrop.ItemData.ItemType.Trinket,
            ItemDrop.ItemData.ItemType.Shield,
        };

        // Interact() est appelée directement depuis le code d'input du joueur (Player.Update) --
        // une exception non rattrapée ici remonterait dans cet appelant et pourrait perturber la
        // gestion des inputs (survol, déplacement) pour cette frame.
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold)
            {
                return false;
            }

            var guard = GetComponent<Humanoid>();
            if (guard == null || user == null)
            {
                return false;
            }

            try
            {
                if (alt)
                {
                    StripGuard(guard, user);
                }
                else
                {
                    DressGuard(guard, user);
                }
            }
            catch (Exception e)
            {
                FedoGuardianPlugin.Log?.LogError($"FedoGuardian: GuardianInteract.Interact a levé une exception : {e}");
            }

            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }

        private static void DressGuard(Humanoid guard, Humanoid user)
        {
            foreach (var type in SwappableArmorTypes)
            {
                SwapSlot(guard, user, type);
            }

            SwapWeapon(guard, user);
        }

        private static void StripGuard(Humanoid guard, Humanoid user)
        {
            Inventory guardInv = guard.GetInventory();
            Inventory userInv = user.GetInventory();

            foreach (var item in guardInv.GetAllItems().ToList())
            {
                if (item.m_equipped)
                {
                    guard.UnequipItem(item);
                }

                userInv.MoveItemToThis(guardInv, item);
            }
        }

        private static void SwapSlot(Humanoid guard, Humanoid user, ItemDrop.ItemData.ItemType type)
        {
            Inventory guardInv = guard.GetInventory();
            Inventory userInv = user.GetInventory();

            ItemDrop.ItemData userItem = userInv.GetAllItemsOfType(type).FirstOrDefault(i => i.m_equipped);
            if (userItem == null)
            {
                return;
            }

            ItemDrop.ItemData guardItem = guardInv.GetAllItemsOfType(type).FirstOrDefault(i => i.m_equipped);
            if (guardItem == userItem)
            {
                return;
            }

            if (guardItem != null)
            {
                guard.UnequipItem(guardItem);
                userInv.MoveItemToThis(guardInv, guardItem);
            }

            guardInv.MoveItemToThis(userInv, userItem);
            guard.EquipItem(userItem);
        }

        private static void SwapWeapon(Humanoid guard, Humanoid user)
        {
            ItemDrop.ItemData userWeapon = user.GetCurrentWeapon();
            if (userWeapon == null)
            {
                return;
            }

            ItemDrop.ItemData guardWeapon = guard.GetCurrentWeapon();
            if (guardWeapon == userWeapon)
            {
                return;
            }

            Inventory guardInv = guard.GetInventory();
            Inventory userInv = user.GetInventory();

            if (guardWeapon != null)
            {
                guard.UnequipItem(guardWeapon);
                userInv.MoveItemToThis(guardInv, guardWeapon);
            }

            guardInv.MoveItemToThis(userInv, userWeapon);
            guard.EquipItem(userWeapon);
        }
    }
}
