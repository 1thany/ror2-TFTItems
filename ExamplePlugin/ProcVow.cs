using BepInEx;
using R2API;
using R2API.AddressReferencedAssets;
using RoR2;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using static ExamplePlugin.ExamplePlugin;
using static R2API.RecalculateStatsAPI;
using static Rewired.Controller;
using static RoR2.CameraModes.CameraModeBase;
using static RoR2.UI.HGHeaderNavigationController;


namespace ExamplePlugin
{
    internal static class SoulLink 
    {
        internal static EquipmentDef ItemDef;
        public static float MaxRange;
        public static float Tank;
        public static float Heal;

        // Registry of active bonds (one per wearer)
        internal static readonly HashSet<ProtectorBondController> Active = new HashSet<ProtectorBondController>();

        // Tag redirected hits to avoid recursion
        internal static readonly DamageAPI.ModdedDamageType RedirectTag = DamageAPI.ReserveDamageType();

        internal const bool DEBUG_CHAT = false;

        internal static void Define()
        {
            MaxRange = 50f;
            Tank = 0.20f;
            Heal = 0.15f;

            ItemDef = ScriptableObject.CreateInstance<EquipmentDef>();
            ItemDef.name = "KNIGHTS_VOW_NAME";
            ItemDef.nameToken = "KNIGHTS_VOW_NAME";
            ItemDef.pickupToken = "KNIGHTS_VOW_PICKUP";
            ItemDef.descriptionToken = "KNIGHTS_VOW_DESC";
            ItemDef.loreToken = "KNIGHTS_VOW_LORE";
            ItemDef.pickupIconSprite = ExamplePlugin.LoadSpriteFromFile("KnightsVow.png");

            ItemDef.isConsumed = false;
            ItemDef.canDrop = true;
            ItemDef.canBeRandomlyTriggered = false;
            ItemDef.isLunar = false;
            ItemDef.enigmaCompatible = false;
            ItemDef.cooldown = 15f;

            var prefab = AssetLoader.LoadAsset("KnightsVowItem");
            ItemDef.pickupModelPrefab = prefab;

            var rules = new ItemDisplayRuleDict(null);
            ItemAPI.Add(new CustomEquipment(ItemDef, rules));
        }

        internal static void Hooks()
        {
            On.RoR2.EquipmentSlot.PerformEquipmentAction += (orig, self, equipmentDef) =>
            {
                if (NetworkServer.active && equipmentDef == ItemDef)
                    return OnUseKnightsVow(self);
                return orig(self, equipmentDef);
            };

            Targeting.HookIndicator(); Targeting.PreloadIndicatorPrefab();
            On.RoR2.HealthComponent.TakeDamage += TakeDamage_PreMitRedirect;
            GlobalEventManager.onServerDamageDealt += GlobalOnServerDamageDealt;
        }


        private static bool OnUseKnightsVow(EquipmentSlot slot)
        {
            if (!NetworkServer.active) return false;
            static bool IsPlayer(CharacterBody b) =>
                b && (b.isPlayerControlled || b.master?.playerCharacterMasterController != null);

            var owner = slot.characterBody; // the equipment holder
            var target = Targeting.FindFriendlyAllyInAim(slot, MaxRange, 20f);
            if (!owner || !target) return false;

            CharacterBody wearer, ally;
            if (IsPlayer(target)) { wearer = target; ally = owner; }
            else { wearer = owner; ally = target; }

            // Team/sanity checks
            if (!wearer.teamComponent || !ally.teamComponent ||
                wearer.teamComponent.teamIndex != ally.teamComponent.teamIndex ||
                ally == wearer)
            {
                return false;
            }

            //// Attach/refresh controller on wearer
            var ctrl = wearer.GetComponent<ProtectorBondController>();
            if (!ctrl) ctrl = wearer.gameObject.AddComponent<ProtectorBondController>();
            ctrl.Initialize(wearer, ally, Tank, Heal, MaxRange);

            //// Optional: lightweight aim indicator helper on the slot holder
            //var aimer = wearer.GetComponent<ProtectorVowAimer>();
            //if (!aimer) aimer = wearer.gameObject.AddComponent<ProtectorVowAimer>();
            //aimer.equipDef = ItemDef;

            if (DEBUG_CHAT) Chat.AddMessage($"[PV] bond: owner={owner.GetDisplayName()} | wearer={wearer.GetDisplayName()} | ally={ally.GetDisplayName()}");

            return true;
        }

        private static void TakeDamage_PreMitRedirect(On.RoR2.HealthComponent.orig_TakeDamage orig, HealthComponent self, DamageInfo di)
        {
            if (!NetworkServer.active ||
                di == null ||
                SoulLink.Active.Count == 0 ||
                DamageAPI.HasModdedDamageType(di, RedirectTag))      // skip our own redirected packet
            {
                orig(self, di);
                return;
            }

            // Snapshot original damage once; we’ll base the redirect on this.
            float originalDamage = di.damage;

            foreach (var ctrl in SoulLink.Active.ToArray())          // snapshot in case a controller disables mid-loop
            {
                if (!ctrl || !ctrl.IsValidAndInRange()) continue;
                // Wearer is about to be hit => siphon a share to Ally
                if (self.body == ctrl.Wearer)
                {
                    float redirected = Mathf.Max(0f, originalDamage * ctrl.Tank);
                    float remaining = Mathf.Max(0f, originalDamage - redirected);

                    // Reduce the wearer’s incoming damage (PRE-mit)
                    di.damage = remaining;

                    // Send the redirected amount to the ally as a fresh packet
                    var di2 = new DamageInfo
                    {
                        attacker = di.attacker,
                        inflictor = di.inflictor ?? di.attacker,
                        damage = redirected,
                        position = ctrl.Ally.corePosition,
                        // Mitigation policy:
                        //  - Keep BypassArmor to avoid double-mit on ally (totals closer to “fair”)
                        //  - Drop BypassArmor if you WANT ally to mitigate too (stronger overall protection)
                        damageType = di.damageType | DamageType.Silent | DamageType.BypassBlock | DamageType.BypassArmor,
                        procCoefficient = 0f,
                        crit = false
                    };
                    DamageAPI.AddModdedDamageType(di2, RedirectTag);

                    ctrl.Ally.healthComponent.TakeDamage(di2);
                    // Optional: allow on-hit/death events to fire
                    GlobalEventManager.instance?.OnHitEnemy(di2, ctrl.Ally.gameObject);

                    if (DEBUG_CHAT)
                        Chat.AddMessage($"[PV] pre-mit redirect: wearer -{redirected:F0}, ally +{redirected:F0}");
                    // Important: DO NOT loop further after mutating di; multiple controllers on same wearer would over-siphon.
                    break;
                }
            }

            orig(self, di);
        }

        private static void GlobalOnServerDamageDealt(DamageReport report)
        {
            if (!NetworkServer.active || report == null || report.victimBody == null || report.damageDealt <= 0f) return;
            if (DamageAPI.HasModdedDamageType(report.damageInfo, RedirectTag)) return; // ignore our own redirected hits
            if (Active.Count == 0) return;


            foreach (var ctrl in Active)
            {
                if (!ctrl || !ctrl.IsValidAndInRange()) continue;
                // if (DEBUG_CHAT) { Chat.AddMessage("Found wearer"); }

                // Heal ally based on wearer's damage dealt
                if (report.attackerBody == ctrl.Wearer)
                {
                    ctrl.Ally.healthComponent?.Heal(report.damageDealt * ctrl.Heal, default);
                    if (DEBUG_CHAT) { Chat.AddMessage($"Healed {report.damageDealt * ctrl.Heal}"); }
                }
            }
        }

        public static class Targeting
        {
            static GameObject _visPrefab;
            static readonly Dictionary<EquipmentSlot, Indicator> _inds = new();

            internal static void PreloadIndicatorPrefab()
            {
                string[] keys = {
                    "RoR2/Base/Equipment/PassiveHealing/WoodSpriteIndicator.prefab",
                    "RoR2/Base/Lightning/LightningIndicator.prefab",
                    "RoR2/Base/Recycler/RecyclerIndicator.prefab"
                };

                foreach (var k in keys)
                {
                    try
                    {
                        var go = Addressables
                            .LoadAssetAsync<GameObject>(k).WaitForCompletion();
                        if (go) { _visPrefab = go; break; }
                    }
                    catch {  }
                }

                if (SoulLink.DEBUG_CHAT && !_visPrefab)
                    Chat.AddMessage("[PV] indicator prefab not found via Addressables (check key)");
            }

            internal static void HookIndicator()
            {
                On.RoR2.EquipmentSlot.Update += (orig, self) =>
                {
                    orig(self);
                    UpdateIndicator(self);
                };
                On.RoR2.EquipmentSlot.OnDestroy += (orig, self) =>
                {
                    if (_inds.TryGetValue(self, out var ind) && ind != null) ind.active = false;
                    _inds.Remove(self);
                    orig(self);
                };
            }

            static void UpdateIndicator(EquipmentSlot slot)
            {
                var body = slot.characterBody;
                var inv = body ? body.inventory : null;

                if (slot.stock <= 0 || slot.cooldownTimer > 0f) return;

                bool show = inv && inv.currentEquipmentIndex == SoulLink.ItemDef.equipmentIndex;
                if (!_inds.TryGetValue(slot, out var ind) || ind == null)
                {
                    var prefab = _visPrefab;
                    if (!prefab) return;
                    ind = new Indicator(slot.gameObject, prefab);
                    ind.active = false;
                    _inds[slot] = ind;
                }

                if (!show || !body || !body.teamComponent)
                {
                    ind.active = false;
                    ind.targetTransform = null;
                    return;
                }

                var hb = FindFriendlyHurtboxInAim(slot, SoulLink.MaxRange, 20f);
                ind.targetTransform = hb ? hb.transform : null;
                ind.active = hb;
            }
            static HurtBox FindFriendlyHurtboxInAim(EquipmentSlot slot, float maxRange, float maxAngle)
            {
                var body = slot.characterBody;
                var team = body?.teamComponent;
                if (!body || !team) return null;

                float extra;
                var ray = CameraRigController.ModifyAimRayIfApplicable(slot.GetAimRay(), slot.gameObject, out extra);

                var search = new BullseyeSearch
                {
                    searchOrigin = ray.origin,
                    searchDirection = ray.direction,
                    filterByLoS = true,
                    sortMode = BullseyeSearch.SortMode.Angle,
                    maxAngleFilter = maxAngle,
                    maxDistanceFilter = maxRange + extra,
                    viewer = body
                };
                search.teamMaskFilter = TeamMask.none;
                search.teamMaskFilter.AddTeam(team.teamIndex);   // friendlies
                search.RefreshCandidates();
                search.FilterOutGameObject(body.gameObject);

                return search.GetResults().FirstOrDefault();
            }

            public static CharacterBody FindFriendlyAllyInAim(EquipmentSlot slot, float maxRange, float maxAngle = 20f)
            {
                var body = slot.characterBody;
                var team = body?.teamComponent;
                if (!body || !team)
                {
                    if (SoulLink.DEBUG_CHAT) Chat.AddMessage("[PV] Find: missing body/team");
                    return null;
                }

                float extraRaycastDistance;
                var ray = CameraRigController.ModifyAimRayIfApplicable(slot.GetAimRay(), slot.gameObject, out extraRaycastDistance);
                if (SoulLink.DEBUG_CHAT) Chat.AddMessage($"[PV] Find start r={maxRange:F0} a={maxAngle:F0}° extra={extraRaycastDistance:F1}");

                var search = new BullseyeSearch
                {
                    searchOrigin = ray.origin,
                    searchDirection = ray.direction,
                    filterByLoS = true,
                    sortMode = BullseyeSearch.SortMode.Angle,
                    maxAngleFilter = maxAngle,
                    maxDistanceFilter = maxRange + extraRaycastDistance,
                    viewer = body
                };

                search.teamMaskFilter = TeamMask.none;
                search.teamMaskFilter.AddTeam(team.teamIndex);   // friendlies only
                search.RefreshCandidates();
                var before = System.Linq.Enumerable.Count(search.GetResults());
                if (SoulLink.DEBUG_CHAT) Chat.AddMessage($"[PV] cands(before self)={before}");

                search.FilterOutGameObject(body.gameObject);     // do not target self
                var results = System.Linq.Enumerable.ToList(search.GetResults());
                if (SoulLink.DEBUG_CHAT) Chat.AddMessage($"[PV] cands(after self)={results.Count}");

                var hb = (results.Count > 0) ? results[0] : null;
                if (hb)
                {
                    var targetBody = hb.healthComponent?.body;
                    float dist = Vector3.Distance(ray.origin, hb.transform.position);
                    float ang = Vector3.Angle(ray.direction, (hb.transform.position - ray.origin).normalized);
                    string label = targetBody ? Util.GetBestBodyName(targetBody.gameObject) : "<?>";
                    if (SoulLink.DEBUG_CHAT) Chat.AddMessage($"[PV] picked {label} d={dist:F1}m a={ang:F0}°");
                    return targetBody;
                }

                if (SoulLink.DEBUG_CHAT) Chat.AddMessage("[PV] no ally in cone/LoS/range");
                return null;
            }
        }
    }

    public class ProtectorBondController : MonoBehaviour
    {
        public CharacterBody Wearer { get; private set; }
        public CharacterBody Ally { get; private set; }
        public float Tank { get; private set; }
        public float Heal { get; private set; }
        public float MaxRange { get; private set; }
        public bool AllyIsPlayerControlled { get; private set; }


        public void Initialize(CharacterBody wearer, CharacterBody ally, float tank, float heal, float maxRange)
        {
            Wearer = wearer;
            Ally = ally;
            Tank = tank;
            Heal = heal;
            MaxRange = maxRange;
            AllyIsPlayerControlled = IsPlayer(ally);


            enabled = true;
        }


        void OnEnable()
        {
            SoulLink.Active.Add(this);
            if (SoulLink.DEBUG_CHAT) Chat.AddMessage($"[PV] Active++ = {SoulLink.Active.Count}");
        }

        void OnDisable()
        {
            SoulLink.Active.Remove(this);
            if (SoulLink.DEBUG_CHAT) Chat.AddMessage($"[PV] Active-- = {SoulLink.Active.Count}");
        }

        void Update()
        {
            // Print reason before destroying so you know what tripped it
            //if (!IsValidAndInRange())
            //{
            //    if (SoulLink.DEBUG_CHAT) Chat.AddMessage("[PV] destroy: invalid or out of range");
            //    Destroy(this);
            //    return;
            //}
            if (!HasProtectorVowEquipped(Wearer))
            {
                if (SoulLink.DEBUG_CHAT) Chat.AddMessage("[PV] destroy: wearer unequipped");
                Destroy(this);
                return;
            }
        }



        public bool IsValidAndInRange()
        {
            if (!Wearer || !Ally) return false;
            var wh = Wearer.healthComponent; var ah = Ally.healthComponent;
            if (wh == null || ah == null || !wh.alive || !ah.alive) return false;
            if (!Wearer.teamComponent || !Ally.teamComponent) return false;
            if (Wearer.teamComponent.teamIndex != Ally.teamComponent.teamIndex) return false;


            var d2 = (Ally.corePosition - Wearer.corePosition).sqrMagnitude;
            return d2 <= MaxRange * MaxRange;
        }


        static bool HasProtectorVowEquipped(CharacterBody body)
        {
            var inv = body?.inventory;
            if (!inv) return false;
            return inv.currentEquipmentIndex == SoulLink.ItemDef.equipmentIndex;
        }


        static bool IsPlayer(CharacterBody body)
        {
            if (!body) return false;
            if (body.isPlayerControlled) return true;
            var m = body.master; return m && m.playerCharacterMasterController;
        }
    }
}
