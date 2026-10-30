using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Alfrik
{
    // Skeggöld's bleed (phase 2): a hit of its edge that draws blood leaves the one hit bleeding, the game's own timed
    // status effect, losing [Boss] BleedPerSecond health every second for [Boss] BleedSeconds (a new hit restarts it),
    // with a blood-drop icon and blood dripping off them. It runs on the game of whoever bleeds.
    internal static class Bleed
    {
        internal const string Name = "Alfrik_Bleed";
        private static readonly int Hash = Name.GetStableHashCode();

        internal static void Register()
        {
            var effect = ScriptableObject.CreateInstance<SE_Stats>();
            effect.name = Name;
            effect.m_name = "Bleeding";
            effect.m_tooltip = "Cut by Skeggöld: losing blood.";
            effect.m_icon = Icon();
            effect.m_ttl = 6f;
            effect.m_tickInterval = 1f;
            effect.m_healthPerTick = -8f;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false));
        }

        internal static void Apply(Character victim)
        {
            SEMan effects = victim == null ? null : victim.GetSEMan();
            if (effects == null || Plugin.BleedSeconds.Value <= 0f || Plugin.BleedPerSecond.Value <= 0f)
                return;
            if (effects.AddStatusEffect(Hash, true) is SE_Stats bleeding)
            {
                bleeding.m_ttl = Plugin.BleedSeconds.Value;
                bleeding.m_healthPerTick = -Plugin.BleedPerSecond.Value;
            }
            Drip(victim, Plugin.BleedSeconds.Value);
        }

        // Blood dripping off the one bleeding, for as long as it lasts.
        private static void Drip(Character victim, float seconds)
        {
            Transform old = victim.transform.Find("Alfrik_Blood");
            if (old != null)
                Object.Destroy(old.gameObject);
            var drip = new GameObject("Alfrik_Blood");
            drip.transform.SetParent(victim.transform, false);
            drip.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            ParticleSystem system = Aura.Particles(drip, new Color(0.55f, 0.02f, 0.02f, 0.9f), 0.06f, 0.8f, 30, true);
            ParticleSystem.MainModule main = system.main;
            main.gravityModifier = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 14f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            system.Play();
            Object.Destroy(drip, seconds);
        }

        // A drop of blood, drawn here.
        private static Sprite Icon()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size;
                    // Round bottom (circle) narrowing to a point at the top.
                    float r = 0.24f;
                    float cy = 0.34f;
                    float inside;
                    if (v <= cy)
                        inside = r - Mathf.Sqrt(u * u + (v - cy) * (v - cy));
                    else
                        inside = Mathf.Lerp(r, 0f, (v - cy) / 0.56f) - Mathf.Abs(u);
                    float a = Mathf.Clamp01(inside * size * 0.7f + 0.5f);
                    float shine = Mathf.Clamp01(1f - Mathf.Sqrt((u + 0.08f) * (u + 0.08f) + (v - 0.3f) * (v - 0.3f)) / 0.12f) * 0.5f;
                    var colour = new Color(0.6f + shine, 0.03f + shine * 0.5f, 0.03f + shine * 0.5f, a);
                    pixels[y * size + x] = colour;
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Alfrik_bleed" };
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
