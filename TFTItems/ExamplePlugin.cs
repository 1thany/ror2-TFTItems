using BepInEx;
using R2API;
using RoR2;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using static R2API.RecalculateStatsAPI;

namespace ExamplePlugin
{
    [BepInDependency(ItemAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class ExamplePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Phorg";
        public const string PluginName = "LeagueItems";
        public const string PluginVersion = "1.0.0";

        public void Awake()
        {
            Log.Init(Logger);

            AllyArmor.Define(); AllyArmor.Hooks();
            FlickerBlade.Define(); FlickerBlade.Hooks();
            HorizonFocus.Define(); HorizonFocus.Hooks();
            UnendingDespair.Define(); UnendingDespair.Hooks();

            RegisterLanguage();
        }

        private void Update()
        {
            /**
            if (Input.GetKeyDown(KeyCode.F2))
            {
                var body = PlayerCharacterMasterController.instances[0].master.GetBodyObject();
                var pos = body.transform.position + body.transform.forward * 2f;
                var pickup = PickupCatalog.FindPickupIndex(FlickerBlade.ItemDef.itemIndex);

                PickupDropletController.CreatePickupDroplet(pickup, pos, Vector3.up * 10f);

                AssetLoader.ListAllAssets();
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                var body = PlayerCharacterMasterController.instances[0].master.GetBodyObject();
                var pos = body.transform.position + body.transform.forward * 2f;
                var pickup = PickupCatalog.FindPickupIndex(HorizonFocus.ItemDef.itemIndex);

                PickupDropletController.CreatePickupDroplet(pickup, pos, Vector3.up * 10f);
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                var body = PlayerCharacterMasterController.instances[0].master.GetBodyObject();
                var pos = body.transform.position + body.transform.forward * 2f;
                var pickup = PickupCatalog.FindPickupIndex(AllyArmor.ItemDef.itemIndex);

                PickupDropletController.CreatePickupDroplet(pickup, pos, Vector3.up * 10f);
            }
            **/
        }

        private static void RegisterLanguage()
        {
            /* ────────────────  Flicker Blade ──────────────── */
            LanguageAPI.Add("FLICKERBLADE_NAME", "Flicker Blade");
            LanguageAPI.Add("FLICKERBLADE_PICKUP", "Primary hits grant stacking attack-speed.");
            LanguageAPI.Add("FLICKERBLADE_DESC",
                "Hits with your <style=cIsUtility>Primary skill</style> grant a stacking buff that increases " +
                "<style=cIsDamage>Attack Speed by 5% (+1% per stack)</style> for <style=cIsUtility>0.5s (+0.5s per stack)</style>. " +
                "Duration refreshes on each hit.");
            LanguageAPI.Add("FLICKERBLADE_LORE", "“The blade is faster than thought…”");

            /* ────────────────  Locket of the Iron Solari ──────────────── */
            LanguageAPI.Add("LOCKET_OF_THE_IRON_SOLARI_NAME", "Locket of the Iron Solari");
            LanguageAPI.Add("LOCKET_OF_THE_IRON_SOLARI_PICKUP", "Allies gain armour from your presence.");
            LanguageAPI.Add("LOCKET_OF_THE_IRON_SOLARI_DESC",
                "<style=cIsHealing>Increase armor</style> for minions by <style=cIsHealing>10</style> (+10 per stack).");
            LanguageAPI.Add("LOCKET_OF_THE_IRON_SOLARI_LORE", "The sun’s protection, entrusted to you.");

            /* ────────────────  Unending Despair ──────────────── */
            LanguageAPI.Add("UNENDING_DESPAIR_NAME", "Unending Despair");
            LanguageAPI.Add("UNENDING_DESPAIR_PICKUP", "Lost barrier erupts in damaging pulses.");
            LanguageAPI.Add("UNENDING_DESPAIR_DESC",
                "Out-of-combat, regenerate a <style=cIsHealing>temporary barrier</style> for <style=cIsHealing>10%</style> max health." +
                "Losing more than <style=cIsHealing>10%</style> max health in barrier, " +
                "will emit a pulse dealing <style=cIsDamage>250% of the lost amount</style> " +
                "(+150% per additional stack). ");
            LanguageAPI.Add("UNENDING_DESPAIR_LORE", "Hope is a candle; despair the wind.");

            /* ────────────────  Horizon Focus ──────────────── */
            LanguageAPI.Add("HORIZON_FOCUS_NAME", "Horizon Focus");
            LanguageAPI.Add("HORIZON_FOCUS_PICKUP", "Stuns chunk enemy health.");
            LanguageAPI.Add("HORIZON_FOCUS_DESC",
                "When you <style=cIsDamage>stun, freeze, root or shock</style> an enemy, " +
                "deal <style=cIsDamage>20%</style> of their current health " +
                "(+10% per stack) as additional damage.");
            LanguageAPI.Add("HORIZON_FOCUS_LORE", "The world narrows to a single point…");
        }

        public static Sprite LoadSpriteFromFile(string fileName)
        {
            string pluginPath = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string fullPath = System.IO.Path.Combine(pluginPath, "icons", fileName);

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"{fileName} Icon not found at path: {fullPath}");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(fullPath);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!tex.LoadImage(fileData))
            {
                Debug.LogError($"{fileName} Failed to load image data.");
                return null;
            }

            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        public static class AssetLoader
        {
            static string pluginDir = System.IO.Path.GetDirectoryName(
                  System.Reflection.Assembly.GetExecutingAssembly().Location
                );
            static string bundlePath = System.IO.Path.Combine(pluginDir, "AssetBundles", "prefabs");
            static AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);

            public static GameObject LoadAsset(string assetName)
            {
                if (bundle == null)
                {
                    UnityEngine.Debug.LogError($"Failed to load bundle at {bundlePath}");
                }
                return bundle.LoadAsset<GameObject>(assetName);
            }

            public static void ListAllAssets()
            {
                foreach (var name in bundle.GetAllAssetNames())
                {
                    Chat.AddMessage("Bundle contains asset: " + name);
                }
            }
        }


    }

}
