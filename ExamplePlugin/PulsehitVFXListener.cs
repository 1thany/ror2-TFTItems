using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking; // for NetworkServer

namespace ExamplePlugin
{
    // simple tag component
    public class PulseMarker : MonoBehaviour
    {
        public Vector3 center;      // optional: for impact normal
        public float ttl = 1f;      // seconds to auto-destroy
        void Update() { ttl -= Time.deltaTime; if (ttl <= 0f) Destroy(gameObject); }
    }
    public class PulseHitVFXListener
    {
        public static GameObject OnHitVFXPrefab; // assign from your AssetBundle or an existing RoR2 impact prefab

        public static void Init()
        {
            GlobalEventManager.onServerDamageDealt += OnServerDamageDealt;
        }

        private static void OnServerDamageDealt(DamageReport report)
        {
            if (!NetworkServer.active) return;                // server spawns, net-replicated to clients
            if (!report?.victimBody) return;

            var inflictor = report.damageInfo.inflictor;
            if (!inflictor) return;

            var marker = inflictor.GetComponent<PulseMarker>();
            if (!marker) return; // not our pulse
            // Debug.LogWarning("Found PulseMarker"); 

            // where to spawn: victim core is reliable
            Vector3 pos = report.victimBody.corePosition;

            // optional “normal” pointing away from the pulse center (for SimpleImpactEffect & aligned effects)
            Vector3 normal = (pos - marker.center);
            if (normal.sqrMagnitude > 1e-6f) normal.Normalize(); else normal = Vector3.up;

            // --- TEMP VFX (built-in) ---
            if (TempVFX.OnHitImpactPrefab)
            {
                Debug.LogWarning("[UD] OnHitImpactPrefab exists");
                EffectManager.SimpleImpactEffect(TempVFX.OnHitImpactPrefab, pos, normal, transmit: true);
            }

            // --- CUSTOM VFX (uncomment when ready) ---
            // if (CustomOnHitVFX)
            // {
            //     EffectManager.SpawnEffect(CustomOnHitVFX,
            //         new EffectData { origin = pos, rotation = Quaternion.LookRotation(normal), scale = 1f },
            //         transmit: true);
            // }
        }
    }

    public static class TempVFX
    {
        public static GameObject OnHitImpactPrefab;

        public static void Load()
        {
            // Try common built-in impact VFX paths
            OnHitImpactPrefab = Addressables.LoadAssetAsync<GameObject>(
            "RoR2/Base/Common/VFX/OmniExplosionVFX.prefab").WaitForCompletion();

            if (!OnHitImpactPrefab)
                Debug.LogWarning("[UD] TempVFX: Could not find OmniImpactVFX, on-hit effects will be skipped.");
        }
    }
}

