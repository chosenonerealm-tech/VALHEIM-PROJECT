using System.Collections.Generic;
using UnityEngine;

namespace Alfrik
{
    // Phase 2's veins, on the avatar: its toon materials' rim light (the glow along its outline) turned blood red and
    // pulsing like a heartbeat. Removing this puts the avatar's own rim light back and takes its runes away.
    internal class AuraVeins : MonoBehaviour
    {
        private static readonly int RimColor = Shader.PropertyToID("_RimColor");
        private static readonly int RimPower = Shader.PropertyToID("_RimFresnelPower");

        internal readonly List<GameObject> Marks = new List<GameObject>();
        private readonly List<(Material Material, Color Colour, float Power)> _materials = new List<(Material, Color, float)>();

        private void Awake()
        {
            var seen = new HashSet<Material>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && seen.Add(material) && material.HasProperty(RimColor))
                        _materials.Add((material, material.GetColor(RimColor),
                            material.HasProperty(RimPower) ? material.GetFloat(RimPower) : 0f));
        }

        private void Update()
        {
            // A double beat about once a second.
            float t = Time.time % 1.1f;
            float beat = Mathf.Exp(-t * 9f) + 0.6f * Mathf.Exp(-Mathf.Max(0f, t - 0.22f) * 9f) * (t > 0.22f ? 1f : 0f);
            Color red = new Color(1f, 0.08f, 0.03f) * (0.45f + 0.9f * Mathf.Clamp01(beat));
            foreach (var (material, _, _) in _materials)
                if (material != null)
                {
                    material.SetColor(RimColor, red);
                    if (material.HasProperty(RimPower))
                        material.SetFloat(RimPower, 2.5f);
                }
        }

        private void OnDestroy()
        {
            foreach (var (material, colour, power) in _materials)
                if (material != null)
                {
                    material.SetColor(RimColor, colour);
                    if (material.HasProperty(RimPower))
                        material.SetFloat(RimPower, power);
                }
            foreach (GameObject mark in Marks)
                if (mark != null)
                    Destroy(mark);
        }
    }
}
