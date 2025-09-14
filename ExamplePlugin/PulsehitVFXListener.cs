using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace ExamplePlugin
{
    public class PulseMarker : MonoBehaviour
    {
        public Vector3 center; 
        public float ttl = 1f;      // seconds to auto-destroy
        void Update() { ttl -= Time.deltaTime; if (ttl <= 0f) Destroy(gameObject); }
    }
    public class PulseHitVFXListener
    {
        public static GameObject OnHitVFXPrefab;

        public static void Init()
        {
            GlobalEventManager.onServerDamageDealt += OnServerDamageDealt;
        }

        private static void OnServerDamageDealt(DamageReport report)
        {
            if (!NetworkServer.active) return; 
            if (!report?.victimBody) return;

            var inflictor = report.damageInfo.inflictor;
            if (!inflictor) return;

            var marker = inflictor.GetComponent<PulseMarker>();
            if (!marker) return;
            // Debug.LogWarning("Found PulseMarker"); 

            Vector3 pos = report.victimBody.corePosition;

            Vector3 normal = (pos - marker.center);
            if (normal.sqrMagnitude > 1e-6f) normal.Normalize(); else normal = Vector3.up;

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
            OnHitImpactPrefab = Addressables.LoadAssetAsync<GameObject>(
            "RoR2/Base/Common/VFX/OmniExplosionVFX.prefab").WaitForCompletion();

            if (!OnHitImpactPrefab)
                Debug.LogWarning("[UD] TempVFX: Could not find OmniImpactVFX, on-hit effects will be skipped.");
        }
    }
}

