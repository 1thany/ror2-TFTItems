using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static ExamplePlugin.ExamplePlugin;
using static R2API.RecalculateStatsAPI;

namespace ExamplePlugin
{
    internal static class FlickerBlade
    {
        internal static ItemDef ItemDef;      // note the capital I
        internal static BuffDef RageBuff;

        internal static void Define()
        {
            /* -----------------  BUFF  ----------------- */
            RageBuff = ScriptableObject.CreateInstance<BuffDef>();
            RageBuff.name = "RAGEBLADE_STACK";
            RageBuff.iconSprite = ExamplePlugin.LoadSpriteFromFile("FlickerBladeBuff.png");
            RageBuff.canStack = true;
            RageBuff.isDebuff = false;
            RageBuff.isCooldown = false;
            RageBuff.isHidden = false;

            ContentAddition.AddBuffDef(RageBuff);

            /* -----------------  ITEM  ----------------- */
            ItemDef = ScriptableObject.CreateInstance<ItemDef>();
            ItemDef.name = "FLICKERBLADE_NAME";
            ItemDef.nameToken = "FLICKERBLADE_NAME";
            ItemDef.pickupToken = "FLICKERBLADE_PICKUP";
            ItemDef.descriptionToken = "FLICKERBLADE_DESC";
            ItemDef.loreToken = "FLICKERBLADE_LORE";
            ItemDef.tier = ItemTier.Tier3;
            ItemDef._itemTierDef = Addressables.LoadAssetAsync<ItemTierDef>(
                "RoR2/Base/Common/Tier3Def.asset").WaitForCompletion();
            ItemDef.pickupIconSprite = ExamplePlugin.LoadSpriteFromFile("FlickerBlade.png");
            ItemDef.canRemove = true;
            ItemDef.hidden = false;

            var prefab = AssetLoader.LoadAsset("flickerBladeItem");
            ItemDef.pickupModelPrefab = prefab;

            ItemAPI.Add(new CustomItem(ItemDef, new ItemDisplayRuleDict(null)));
        }

        /* ----------------------------------------------------------------- */
        /* hooks                                                             */
        /* ----------------------------------------------------------------- */
        internal static void Hooks()
        {
            On.RoR2.GlobalEventManager.ProcessHitEnemy += AddAttackSpeedBuff;
            GetStatCoefficients += ApplyRageBuff;
        }

        private static void AddAttackSpeedBuff(On.RoR2.GlobalEventManager.orig_ProcessHitEnemy orig,
                                               GlobalEventManager self, DamageInfo di, GameObject victim)
        {
            orig(self, di, victim);

            var body = di.attacker ? di.attacker.GetComponent<CharacterBody>() : null;
            if (!body || !body.inventory) return;

            bool isPrimary = di.damageType.damageSource.HasFlag(DamageSource.Primary);
            int itemStacks = body.inventory.GetItemCount(ItemDef.itemIndex);
            if (!isPrimary || itemStacks == 0) return;

            /* Refresh every stack to the new duration so they expire simultaneously */
            float dur = 0.5f + 0.5f * itemStacks;
            int currStacks = body.GetBuffCount(RageBuff);
            body.ClearTimedBuffs(RageBuff);
            for (int i = 0; i <= currStacks; i++)
                body.AddTimedBuff(RageBuff, dur);
        }

        private static void ApplyRageBuff(CharacterBody body, StatHookEventArgs args)
        {
            int buffCount = body.GetBuffCount(RageBuff);
            if (buffCount == 0) return;

            int itemStacks = body.inventory ? body.inventory.GetItemCount(ItemDef.itemIndex) : 0;
            float bonusPerStack = 0.05f + 0.01f * (itemStacks - 1);
            args.attackSpeedMultAdd += bonusPerStack * buffCount;
        }
    }
}
