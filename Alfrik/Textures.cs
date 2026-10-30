using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Alfrik
{
    // His particle textures, .png files in Textures/ next to this plugin (Kenney Particle Pack, CC0), white on
    // transparent so each effect tints them; loaded once, one material each. A missing file falls back to the plain
    // soft puff.
    internal static class Textures
    {
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        // ImageConversion.LoadImage(Texture2D, byte[], bool), by reflection: its span overload cannot be compiled against here.
        private static readonly System.Reflection.MethodInfo LoadImage = typeof(ImageConversion).GetMethod("LoadImage",
            new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

        internal static Material Get(string name)
        {
            if (Materials.TryGetValue(name, out Material material))
                return material;
            string path = Path.Combine(Path.Combine(Plugin.Folder, "Textures"), name + ".png");
            material = Aura.Soft;
            if (name == Runes)
                material = DrawRunes();
            else if (File.Exists(path))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Alfrik_" + name };
                if (LoadImage != null && (bool)LoadImage.Invoke(null, new object[] { texture, File.ReadAllBytes(path), false }))
                {
                    BlackToClear(texture);
                    material = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture, name = "Alfrik_" + name };
                }
                else
                    Plugin.Log.LogWarning("Texture " + path + " could not be read");
            }
            Materials[name] = material;
            return material;
        }

        // Four Elder Futhark runes, drawn here (2 x 2 sheet): Tiwaz, Thurisaz, Algiz and Ansuz, Odin's rune.
        internal const string Runes = "runes";
        internal const int Ansuz = 3;

        private static readonly Vector2[][][] RuneStrokes =
        {
            new[] { L(0.5f, 0.1f, 0.5f, 0.9f), L(0.5f, 0.9f, 0.24f, 0.62f), L(0.5f, 0.9f, 0.76f, 0.62f) },
            new[] { L(0.36f, 0.1f, 0.36f, 0.9f), L(0.36f, 0.7f, 0.66f, 0.5f), L(0.66f, 0.5f, 0.36f, 0.3f) },
            new[] { L(0.5f, 0.1f, 0.5f, 0.9f), L(0.5f, 0.52f, 0.22f, 0.86f), L(0.5f, 0.52f, 0.78f, 0.86f) },
            new[] { L(0.36f, 0.1f, 0.36f, 0.9f), L(0.36f, 0.9f, 0.68f, 0.68f), L(0.36f, 0.66f, 0.68f, 0.44f) },
        };

        private static Vector2[] L(float x1, float y1, float x2, float y2) => new[] { new Vector2(x1, y1), new Vector2(x2, y2) };

        private static Material DrawRunes()
        {
            const int cell = 128, size = cell * 2;
            const float stroke = 0.045f, glow = 0.1f;
            var pixels = new Color32[size * size];
            for (int rune = 0; rune < 4; rune++)
            {
                // Sheet frames run left to right, top row first; texture rows run bottom up.
                int left = (rune % 2) * cell, bottom = (1 - rune / 2) * cell;
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        var p = new Vector2((x + 0.5f) / cell, (y + 0.5f) / cell);
                        float d = float.MaxValue;
                        foreach (Vector2[] line in RuneStrokes[rune])
                            d = Mathf.Min(d, Distance(p, line[0], line[1]));
                        float core = Mathf.Clamp01((stroke - d) / 0.012f + 0.5f);
                        float halo = Mathf.Clamp01(1f - (d - stroke) / glow) * 0.45f;
                        pixels[(bottom + y) * size + left + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Max(core, halo * halo)));
                    }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Alfrik_runes" };
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return new Material(Shader.Find("Sprites/Default")) { mainTexture = texture, name = "Alfrik_runes" };
        }

        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // Particles using the rune sheet: upright, one rune each (a random one, or the one given).
        internal static void RuneSheet(ParticleSystem system, int rune = -1)
        {
            ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = rune < 0 ? new ParticleSystem.MinMaxCurve(0f, 3.99f) : new ParticleSystem.MinMaxCurve(rune);
            sheet.cycleCount = 1;
            ParticleSystem.MainModule main = system.main;
            main.startRotation = 0f;
        }

        private static Sprite _emblem;

        // His emblem, drawn here: Hugr and Hamr crossed in steel on a blood-red disc in an iron ring.
        internal static Sprite Emblem
        {
            get
            {
                if (_emblem != null)
                    return _emblem;
                const int size = 128, samples = 3;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        Color sum = Color.clear;
                        for (int sy = 0; sy < samples; sy++)
                            for (int sx = 0; sx < samples; sx++)
                                sum += EmblemAt(new Vector2((x + (sx + 0.5f) / samples) / size, (y + (sy + 0.5f) / samples) / size));
                        pixels[y * size + x] = sum / (samples * samples);
                    }
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Alfrik_emblem" };
                texture.SetPixels32(pixels);
                texture.Apply(true);
                _emblem = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                _emblem.name = "Alfrik_emblem";
                return _emblem;
            }
        }

        // One axe in its own frame: u along the haft from the grip, v across it toward the blade. A bearded blade.
        private static readonly Vector2[] Blade =
        {
            new Vector2(0.02f, 0f), new Vector2(0.07f, 0.19f), new Vector2(0.01f, 0.25f), new Vector2(-0.07f, 0.27f),
            new Vector2(-0.15f, 0.26f), new Vector2(-0.21f, 0.21f), new Vector2(-0.13f, 0f),
        };

        private static Color EmblemAt(Vector2 p)
        {
            Vector2 centre = new Vector2(0.5f, 0.5f);
            float r = Vector2.Distance(p, centre);
            if (r > 0.49f)
                return Color.clear;
            Color colour;
            if (r > 0.43f)
            {
                // Iron ring, lit from above.
                float light = 0.5f + 0.5f * Mathf.Clamp01((p.y - 0.2f) / 0.8f);
                colour = Color.Lerp(new Color(0.16f, 0.16f, 0.18f), new Color(0.48f, 0.47f, 0.5f), light);
                if (r > 0.475f || r < 0.44f)
                    colour *= 0.6f;
            }
            else
                colour = Color.Lerp(new Color(0.62f, 0.09f, 0.05f), new Color(0.22f, 0.02f, 0.02f), r / 0.43f);
            colour.a = 1f;

            // Two axes crossed, blades out.
            float axe = Mathf.Min(AxeDistance(p, new Vector2(0.24f, 0.17f), new Vector2(0.7f, 0.76f), -1f),
                AxeDistance(new Vector2(1f - p.x, p.y), new Vector2(0.24f, 0.17f), new Vector2(0.7f, 0.76f), -1f));
            if (axe < 0f)
            {
                float shine = Mathf.Clamp01(0.55f + p.y * 0.5f);
                return Color.Lerp(new Color(0.55f, 0.57f, 0.62f), new Color(0.95f, 0.96f, 1f), shine);
            }
            if (axe < 0.018f)
                return new Color(0.05f, 0.03f, 0.03f, 1f);
            return colour;
        }

        // Signed distance to one axe (haft from grip to head, blade on the side 'side' points to); < 0 inside.
        private static float AxeDistance(Vector2 p, Vector2 grip, Vector2 head, float side)
        {
            Vector2 along = (head - grip).normalized, across = new Vector2(-along.y, along.x) * side;
            float haft = Distance(p, grip, head + along * 0.04f) - 0.024f;
            Vector2 local = new Vector2(Vector2.Dot(p - head, along), Vector2.Dot(p - head, across));
            float blade = PolygonDistance(local, Blade);
            return Mathf.Min(haft, blade);
        }

        private static float PolygonDistance(Vector2 p, Vector2[] polygon)
        {
            float d = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                d = Mathf.Min(d, Distance(p, a, b));
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside ? -d : d;
        }

        // A texture with no transparency at all is the black-background kind (drawn as a square): its brightness
        // becomes the transparency, on white, so black is clear and the effect keeps its shape.
        private static void BlackToClear(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            foreach (Color32 pixel in pixels)
                if (pixel.a < 255)
                    return;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.Max(p.r, Mathf.Max(p.g, p.b)));
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
        }
    }
}
