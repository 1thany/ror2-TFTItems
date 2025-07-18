using BepInEx;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static ExamplePlugin.ExamplePlugin;

namespace ExamplePlugin
{
    internal static class HorizonFocus
    {
        internal static ItemDef ItemDef;

        internal static void Define()
        {
            ItemDef = ScriptableObject.CreateInstance<ItemDef>();
            ItemDef.name = "HORIZON_FOCUS_NAME";
            ItemDef.nameToken = "HORIZON_FOCUS_NAME";
            ItemDef.pickupToken = "HORIZON_FOCUS_PICKUP";
            ItemDef.descriptionToken = "HORIZON_FOCUS_DESC";
            ItemDef.loreToken = "HORIZON_FOCUS_LORE";
            ItemDef.tier = ItemTier.Tier2;
            ItemDef._itemTierDef = Addressables.LoadAssetAsync<ItemTierDef>(
                                            "RoR2/Base/Common/Tier2Def.asset").WaitForCompletion();
            ItemDef.pickupIconSprite = ExamplePlugin.LoadSpriteFromFile("HorizonFocus.png");
            ItemDef.canRemove = true;
            ItemDef.hidden = false;

            var prefab = AssetLoader.LoadAsset("horizonFocusItem");
            ItemDef.pickupModelPrefab = prefab;

            ItemAPI.Add(new CustomItem(ItemDef, new ItemDisplayRuleDict(null)));
        }

        internal static void Hooks()
        {
            On.RoR2.SetStateOnHurt.OnTakeDamageServer += StunWatcher;
        }

        private static void StunWatcher(On.RoR2.SetStateOnHurt.orig_OnTakeDamageServer orig,
                                        SetStateOnHurt self, DamageReport report)
        {
            orig(self, report);                 // keep vanilla behaviour

            if (report == null) return;

            DamageInfo dmgInfo = report.damageInfo;
            CharacterBody victim = report.victimBody;
            CharacterBody attacker = report.attackerBody;

            if (!victim || !attacker) return; // null-checks
            if (!attacker.inventory) return; // no inventory = no item
            if ((dmgInfo.damageType & StunFlags) == 0) return; // not a stun
            int stacks = attacker.inventory.GetItemCount(ItemDef.itemIndex);
            if (stacks <= 0) return; // attacker has no item

            // 20 % of CURRENT health per stack
            float bonus = victim.healthComponent.combinedHealth * (0.40f + (0.10f * stacks));

            var blast = new DamageInfo
            {
                attacker = attacker.gameObject,
                inflictor = attacker.gameObject,
                crit = false,
                damage = bonus,
                damageColorIndex = DamageColorIndex.Item,
                damageType = DamageType.Generic,
                position = victim.corePosition,
                procCoefficient = 0f     // no extra procs
            };

            victim.healthComponent.TakeDamage(blast);

            // Optional: tiny vanilla hit effect so players notice the burst
            EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>(
                                          "Prefabs/Effects/ImpactEffects/IgniteExplosionVFX"),
                                      new EffectData { origin = victim.corePosition }, false);
        }
        private const DamageType StunFlags =
              DamageType.Stun1s
            | DamageType.Shock5s
            | DamageType.LunarSecondaryRootOnHit
            | DamageType.Freeze2s;
    }
}
