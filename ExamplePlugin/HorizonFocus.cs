using BepInEx;
using R2API;
using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using static ExamplePlugin.ExamplePlugin;

namespace ExamplePlugin
{
    internal static class HorizonFocus
    {
        internal static ItemDef ItemDef;
        const float BasePctCurrent = 0.10f;
        const float StackBonusPct = 0.05f;
        const float IcdSeconds = 1.00f;

        static readonly Dictionary<CharacterBody, float> _victimReadyAt = new();

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
            GlobalEventManager.onServerDamageDealt += OnServerDamageDealt;

            // cleanup ICD entries when something dies
            On.RoR2.CharacterBody.OnDeathStart += (orig, self) =>
            {
                if (NetworkServer.active) _victimReadyAt.Remove(self);
                orig(self);
            };
        }

        static void OnServerDamageDealt(DamageReport report)
        {
            if (!NetworkServer.active || report == null) return;

            var victimBody = report.victimBody;
            var attackerBody = report.attackerBody;
            if (!victimBody || !attackerBody) return;

            // item gating
            var inv = attackerBody.inventory;
            int stacks = inv ? inv.GetItemCount(ItemDef) : 0;
            if (stacks <= 0) return;

            var dt = report.damageInfo.damageType;
            bool isStunOrShock =
                (dt & DamageType.Stun1s) != 0 ||
                (dt & DamageType.Shock5s) != 0;
            if (!isStunOrShock) return;

            // ICD per victim
            float now = Time.time;
            if (_victimReadyAt.TryGetValue(victimBody, out var readyAt) && now < readyAt) return;
            _victimReadyAt[victimBody] = now + IcdSeconds;

            var hc = victimBody.healthComponent;
            if (!hc || !hc.alive) return;

            float pct = BasePctCurrent + StackBonusPct * Mathf.Max(0, stacks - 1);
            if (victimBody.isBoss || victimBody.isChampion) pct *= 0.5f;
            float extra = hc.combinedHealth * pct;
            if (extra <= 0f) return;

            var burst = new DamageInfo
            {
                attacker = attackerBody.gameObject,         
                inflictor = report.damageInfo.inflictor,
                damage = extra,
                position = victimBody.corePosition,
                damageType = DamageType.Silent | DamageType.NonLethal,
                procCoefficient = 0f,
                crit = false,
                damageColorIndex = DamageColorIndex.Item,
            };

            hc.TakeDamage(burst);
            GlobalEventManager.instance?.OnHitEnemy(burst, victimBody.gameObject);
        }

        static bool IsPlayer(CharacterBody b)
        {
            if (!b) return false;
            if (b.isPlayerControlled) return true;
            var m = b.master;
            return m && m.playerCharacterMasterController != null;
        }
    }
}
