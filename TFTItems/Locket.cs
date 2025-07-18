using BepInEx;
using R2API;
using R2API.AddressReferencedAssets;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static ExamplePlugin.ExamplePlugin;
using static R2API.RecalculateStatsAPI;


namespace ExamplePlugin
{
    internal static class AllyArmor
    {
        internal static ItemDef ItemDef;

        internal static void Define()
        {
            ItemDef = ScriptableObject.CreateInstance<ItemDef>();
            ItemDef.name = "LOCKET_OF_THE_IRON_SOLARI_NAME";
            ItemDef.nameToken = "LOCKET_OF_THE_IRON_SOLARI_NAME";
            ItemDef.pickupToken = "LOCKET_OF_THE_IRON_SOLARI_PICKUP";
            ItemDef.descriptionToken = "LOCKET_OF_THE_IRON_SOLARI_DESC";
            ItemDef.loreToken = "LOCKET_OF_THE_IRON_SOLARI_LORE";
            ItemDef.tier = ItemTier.Tier1;
            ItemDef._itemTierDef = Addressables.LoadAssetAsync<ItemTierDef>(
                                            "RoR2/Base/Common/Tier1Def.asset").WaitForCompletion();
            ItemDef.pickupIconSprite = ExamplePlugin.LoadSpriteFromFile("Locket.png");
            ItemDef.canRemove = true;
            ItemDef.hidden = false;

            var prefab = AssetLoader.LoadAsset("locketItem");
            ItemDef.pickupModelPrefab = prefab;

            ItemAPI.Add(new CustomItem(ItemDef, new ItemDisplayRuleDict(null)));
        }

        internal static void Hooks()
        {
            RecalculateStatsAPI.GetStatCoefficients += GrantAllyArmor;
            // On.RoR2.CharacterBody.RecalculateStats += AfterRecalc;
        }

        private static void GrantAllyArmor(CharacterBody body, StatHookEventArgs args)
        {
            if (!body || !body.master) return;

            CharacterMaster master = body.master;

            if (master.playerCharacterMasterController != null) return;

            if (master.minionOwnership.ownerMaster)
            {
                var ownerInventory = master.minionOwnership.ownerMaster.inventory;
                if (!ownerInventory) return;

                int itemCount = ownerInventory.GetItemCount(ItemDef.itemIndex);
                if (itemCount > 0)
                {
                    float bonusArmor = itemCount * 10f;
                    args.armorAdd += bonusArmor;

                    // Chat.AddMessage($"[DEBUG] Ally: {body.name}");
                    // Chat.AddMessage($"[DEBUG] Owner has {itemCount} item(s), current armor: {body.armor}");
                }
                else
                {
                    // Chat.AddMessage($"[DEBUG] Owner has 0 items. No armor applied to {body.name}");
                }
            }
        }

        private static void AfterRecalc(On.RoR2.CharacterBody.orig_RecalculateStats orig,
                                CharacterBody body)
        {
            float before = body.armor; 
            orig(body);                      
            float after = body.armor;       

            if (after != before)
                Chat.AddMessage($"[DEBUG] {body.GetDisplayName()} armor: {before} → {after}");
        }
    }
}
