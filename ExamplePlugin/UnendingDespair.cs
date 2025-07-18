using BepInEx;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using static R2API.RecalculateStatsAPI;

namespace ExamplePlugin
{
    internal static class UnendingDespair
    {
        internal static ItemDef ItemDef;
        internal static GameObject PulseVFX;

        internal static void Define()
        {
            ItemDef = ScriptableObject.CreateInstance<ItemDef>();
            ItemDef.name = "UNENDING_DESPAIR_NAME";
            ItemDef.nameToken = "UNENDING_DESPAIR_NAME";
            ItemDef.pickupToken = "UNENDING_DESPAIR_PICKUP";
            ItemDef.descriptionToken = "UNENDING_DESPAIR_DESC";
            ItemDef.loreToken = "UNENDING_DESPAIR_LORE";
            ItemDef.tier = ItemTier.Tier3;
            ItemDef._itemTierDef = Addressables.LoadAssetAsync<ItemTierDef>(
                                            "RoR2/Base/Common/Tier3Def.asset").WaitForCompletion();
            ItemDef.pickupIconSprite = ExamplePlugin.LoadSpriteFromFile("UnendingDespair.png");
            ItemDef.canRemove = true;
            ItemDef.hidden = false;

            ItemAPI.Add(new CustomItem(ItemDef, new ItemDisplayRuleDict(null)));

            PulseVFX = Addressables.LoadAssetAsync<GameObject>(
            "RoR2/Base/Common/VFX/OmniExplosionVFX.prefab").WaitForCompletion();
        }

        internal static void Hooks()
        {
            CharacterBody.onBodyStartGlobal += AddTrackerIfNeeded;
            On.RoR2.CharacterBody.FixedUpdate += BarrierOutOfCombat;
        }

        private static void AddTrackerIfNeeded(CharacterBody body)
        {
            // Attach the tracker only to characters that can gain barrier
            if (!body.GetComponent<BarrierPulseTracker>())
                body.gameObject.AddComponent<BarrierPulseTracker>();
        }

        private class BarrierPulseTracker : MonoBehaviour
        {
            private CharacterBody body;
            private HealthComponent hc;
            private float lastBarrier;
            static readonly float BASE_THRESHOLD_FRAC = 0.10f;   // 10 % of max HP per item
            float accumulatedLoss = 0f;                           // reset each time you pulse

            // convenience
            private ItemIndex ItemIndex => UnendingDespair.ItemDef.itemIndex;

            void Awake()
            {
                body = GetComponent<CharacterBody>();
                hc = body.healthComponent;
                lastBarrier = hc.barrier;
            }

            void FixedUpdate()              // called every physics tick (50 Hz)
            {
                if (!body || !hc) return;

                int stacks = body.inventory ? body.inventory.GetItemCount(ItemIndex) : 0;
                if (stacks == 0) { lastBarrier = hc.barrier; return; }      // player doesn’t own item

                float current = hc.barrier;
                if (current >= lastBarrier) { lastBarrier = current; return; } // barrier grew or unchanged

                float lost = lastBarrier - current;
                if (lost <= 0f) { lastBarrier = current; return; }

                accumulatedLoss += lost;

                float threshold = BASE_THRESHOLD_FRAC * body.maxHealth;

                if (accumulatedLoss >= threshold)
                {
                    FirePulse(accumulatedLoss, stacks);   // do your 250 %-of-loss damage, VFX, etc.
                    accumulatedLoss = 0f;              // empty the bucket
                }

                lastBarrier = current;
            }

            private void FirePulse(float depletedBarrier, int stacks)
            {
                float DAMAGE_MULT = 2.50f + 1.50f * (stacks - 1);
                float damage = depletedBarrier * DAMAGE_MULT * stacks;

                BlastAttack pulse = new BlastAttack
                {
                    attacker = body.gameObject,
                    attackerFiltering = AttackerFiltering.NeverHitSelf,
                    baseDamage = damage,
                    baseForce = 0f,
                    bonusForce = Vector3.zero,
                    crit = body.RollCrit(),
                    damageColorIndex = DamageColorIndex.Bleed,
                    damageType = DamageType.Generic,
                    falloffModel = BlastAttack.FalloffModel.None,
                    position = body.corePosition,
                    procCoefficient = 0.25f,
                    radius = 12f + 2f * (stacks - 1)     // radius scales a bit with stacks
                };

                pulse.Fire();
                EffectManager.SpawnEffect(UnendingDespair.PulseVFX,
                                          new EffectData { origin = body.corePosition, scale = pulse.radius },
                                          true);

                // Chat.AddMessage($"<style=cIsUtility>{ItemDef.nameToken}</style> pulse! {damage:0} dmg  (lost {depletedBarrier:0} barrier)");
            }
        }

        private static void BarrierOutOfCombat(On.RoR2.CharacterBody.orig_FixedUpdate orig, CharacterBody body)
        {
            orig(body);                               

            if (!NetworkServer.active) return;
            if (body == null) return;
            if (body.inventory == null) return;

            int stacks = body.inventory.GetItemCount(UnendingDespair.ItemDef.itemIndex);
            if (stacks == 0) return;                  // owner doesn’t have our item

            if (!body.outOfDanger) return;

            float targetBarrier = body.maxHealth * 0.10f;
            float missing = targetBarrier - body.healthComponent.barrier;

            if (missing > 1f)                           // add only if we need > 1 HP
            {
                body.healthComponent.AddBarrier(missing);
                // ---- OPTIONAL DEBUG ----
                // Chat.AddMessage($"[DEBUG] +{missing:F0} barrier ({stacks} stack(s))");
            }
        }
    }
}
