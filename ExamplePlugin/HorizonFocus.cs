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
        const float PercentCurrent = 0.20f;
        const float IcdSeconds = 1.00f;
        const float MinStunDuration = 0.50f;
        const float MinShockDuration = 0.20f;
        const bool UseBypassArmor = false;
        const bool UseBypassBlock = false;

        static readonly Dictionary<CharacterBody, float> nextAllowedAt = new();

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

        static readonly Dictionary<CharacterBody, float> _nextAllowedAt = new();

        public static void Hooks()
        {
            // STUN
            On.RoR2.SetStateOnHurt.SetStun += (orig, self, duration) =>
            {
                orig(self, duration);
                if (!NetworkServer.active || duration < MinStunDuration) return;

                var body = self ? self.GetComponent<CharacterBody>() : null;
                var hc = body ? body.healthComponent : null;
                if (!body || !hc || !hc.alive) return;

                TryBurst(body, hc);
            };

            // SHOCK
            On.RoR2.SetStateOnHurt.SetShock += (orig, self, duration) =>
            {
                orig(self, duration);
                if (!NetworkServer.active || duration < MinShockDuration) return;

                var body = self ? self.GetComponent<CharacterBody>() : null;
                var hc = body ? body.healthComponent : null;
                if (!body || !hc || !hc.alive) return;

                TryBurst(body, hc);
            };

            // Clean up
            On.RoR2.CharacterBody.OnDeathStart += (orig, self) =>
            {
                if (NetworkServer.active) _nextAllowedAt.Remove(self);
                orig(self);
            };
        }

        static void TryBurst(CharacterBody victimBody, HealthComponent victimHC)
        {
            float now = Time.time;

            // ICD gate (shared for stun & shock)
            if (_nextAllowedAt.TryGetValue(victimBody, out var ready) && now < ready) return;
            _nextAllowedAt[victimBody] = now + IcdSeconds;

            float current = victimHC.combinedHealth; // CURRENT health pool (not max)
            if (current <= 0f) return;

            float extra = current * PercentCurrent;

            var flags = DamageType.Silent | DamageType.NonLethal; // always non-lethal, no on-kill
            if (UseBypassBlock) flags |= DamageType.BypassBlock;
            if (UseBypassArmor) flags |= DamageType.BypassArmor;

            var di = new DamageInfo
            {
                attacker = null,           // neutral: no credit, no procs, no kills
                inflictor = null,
                damage = extra,
                position = victimBody.corePosition,
                damageType = flags,
                procCoefficient = 0f,             // no on-hit procs
                crit = false
            };

            victimHC.TakeDamage(di);
            GlobalEventManager.instance?.OnHitEnemy(di, victimBody.gameObject);
        }
    }
}
