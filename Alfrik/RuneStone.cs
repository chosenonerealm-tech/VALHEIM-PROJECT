using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Alfrik
{
    // Odin's Challenge Stone: a rune-cut standing stone built with the hammer (Misc). Offering at it ([Summoning]
    // Offering) calls him down: he dives from the sky and lands on the far side of the stone, and the stone crumbles. The stone is shaped and
    // textured here (built next to a Galdr table), its runes glow and pulse red; only the game's stone shader is borrowed so it is lit like the world.
    internal static class RuneStone
    {
        internal const string Prefab = "Alfrik_ChallengeStone";
        internal const string Name = "Odin's Challenge Stone";
        private const float Height = 2.3f;

        internal static void Register() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        private static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            GameObject prefab = PrefabManager.Instance.CreateEmptyPrefab(Prefab);
            if (prefab == null)
                return;
            prefab.layer = LayerMask.NameToLayer("piece");
            prefab.GetComponent<MeshFilter>().sharedMesh = StoneMesh();
            prefab.GetComponent<MeshRenderer>().sharedMaterial = StoneMaterial();
            BoxCollider box = prefab.GetComponent<BoxCollider>();
            box.center = new Vector3(0f, Height * 0.5f, 0f);
            box.size = new Vector3(1.05f, Height, 0.66f);

            AddRune(prefab, 3, new Vector3(0f, 1.35f, 0.37f), 0f);
            AddRune(prefab, 0, new Vector3(0f, 1.35f, -0.37f), 180f);
            var glow = new GameObject("glow");
            glow.transform.SetParent(prefab.transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.2f, 0.08f);
            light.range = 4f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;

            Piece piece = prefab.AddComponent<Piece>();
            piece.m_name = Name;
            piece.m_description = Description;
            piece.m_icon = Textures.Emblem;
            prefab.AddComponent<ChallengeStone>();

            var config = new PieceConfig
            {
                Name = Name,
                Description = Description,
                PieceTable = "Hammer",
                Category = "Misc",
                CraftingStation = "piece_magetable",
                Icon = Textures.Emblem,
                Requirements = Requirements(Plugin.StoneRecipe.Value),
            };
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
        }

        private static string Description =>
            "Lay an offering here and Alfrik, The Left Fist of Odin, comes down from the sky to fight you.";

        // "Stone:20,BlackMetal:10" -> requirements (unknown items are skipped by the game's own check).
        private static RequirementConfig[] Requirements(string recipe)
        {
            var list = new List<RequirementConfig>();
            foreach (string part in (recipe ?? "").Split(','))
            {
                string[] pair = part.Split(':');
                if (pair.Length == 2 && int.TryParse(pair[1].Trim(), out int amount) && amount > 0)
                    list.Add(new RequirementConfig(pair[0].Trim(), amount, 0, true));
            }
            return list.ToArray();
        }

        // "TrophyFenring:1" -> (prefab, count).
        internal static (string Item, int Amount) Offering()
        {
            string[] pair = (Plugin.Offering.Value ?? "").Split(':');
            int amount = pair.Length == 2 && int.TryParse(pair[1].Trim(), out int n) ? n : 1;
            return (pair[0].Trim(), Mathf.Max(0, amount));
        }

        // A tapered, slightly lumpy slab with a slanted top, 16 sides, rooted 0.2 m into the ground.
        private static Mesh StoneMesh()
        {
            const int sides = 16, rings = 10;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);
                float halfWidth = Mathf.Lerp(0.55f, 0.36f, t), halfDepth = Mathf.Lerp(0.32f, 0.24f, t);
                for (int s = 0; s <= sides; s++)
                {
                    float angle = s % sides / (float)sides * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle), z = Mathf.Sin(angle);
                    // A rounded box section: flat broad faces, rounded edges.
                    float px = Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), 0.55f) * halfWidth;
                    float pz = Mathf.Sign(z) * Mathf.Pow(Mathf.Abs(z), 0.55f) * halfDepth;
                    float lump = 1f + 0.06f * (Hash(r * 31 + s % sides * 7) - 0.5f);
                    float y = -0.2f + t * (Height + 0.2f);
                    if (r == rings - 1)
                        y -= 0.28f * (px / 0.36f + 1f) * 0.5f;
                    vertices.Add(new Vector3(px * lump, y, pz * lump));
                    uvs.Add(new Vector2(s / (float)sides * 2f, y / Height * 2f));
                }
            }
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = r * (sides + 1) + s, b = a + sides + 1;
                    triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            // Top cap.
            int top = (rings - 1) * (sides + 1), centre = vertices.Count;
            Vector3 middle = Vector3.zero;
            for (int s = 0; s < sides; s++)
                middle += vertices[top + s];
            vertices.Add(middle / sides + Vector3.up * 0.04f);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s < sides; s++)
                triangles.AddRange(new[] { top + s, centre, top + s + 1 });
            var mesh = new Mesh { name = "Alfrik_ChallengeStone" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483648f;
        }

        // Dark weathered granite, speckled, drawn here; on the game's stone-piece shader when it can be found.
        private static Material StoneMaterial()
        {
            const int size = 256;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float n = 0f, amp = 0.5f, freq = 4f;
                    for (int o = 0; o < 4; o++, amp *= 0.5f, freq *= 2f)
                        n += amp * Mathf.PerlinNoise(x / (float)size * freq + o * 17.3f, y / (float)size * freq + o * 9.1f);
                    float speck = Hash(x * 977 + y * 131) > 0.97f ? 0.12f : 0f;
                    float v = Mathf.Clamp01(0.22f + n * 0.32f + speck);
                    pixels[y * size + x] = new Color(v * 1.02f, v, v * 0.96f, 1f);
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Alfrik_granite" };
            texture.SetPixels32(pixels);
            texture.Apply(true);

            Material basis = null;
            GameObject wall = PrefabManager.Instance.GetPrefab("stone_wall_1x1");
            MeshRenderer renderer = wall == null ? null : wall.GetComponentInChildren<MeshRenderer>();
            if (renderer != null)
                basis = renderer.sharedMaterial;
            Material material = basis != null ? new Material(basis) : new Material(Shader.Find("Standard"));
            material.name = "Alfrik_granite";
            material.mainTexture = texture;
            material.mainTextureScale = Vector2.one;
            if (material.HasProperty("_BumpMap"))
                material.SetTexture("_BumpMap", null);
            return material;
        }

        // One glowing rune cut into a face of the stone.
        private static void AddRune(GameObject stone, int rune, Vector3 position, float turn)
        {
            var quad = new GameObject("rune");
            quad.transform.SetParent(stone.transform, false);
            quad.transform.localPosition = position;
            quad.transform.localRotation = Quaternion.Euler(0f, turn, 0f);
            float u = rune % 2 * 0.5f, v = (1 - rune / 2) * 0.5f;
            var mesh = new Mesh { name = "Alfrik_rune" };
            mesh.vertices = new[] { new Vector3(-0.28f, -0.28f, 0f), new Vector3(0.28f, -0.28f, 0f), new Vector3(-0.28f, 0.28f, 0f), new Vector3(0.28f, 0.28f, 0f) };
            // Seen from outside the face (+z for an unturned rune): uv x runs from the viewer's left.
            mesh.uv = new[] { new Vector2(u + 0.5f, v), new Vector2(u, v), new Vector2(u + 0.5f, v + 0.5f), new Vector2(u, v + 0.5f) };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            mesh.RecalculateNormals();
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(Textures.Get(Textures.Runes)) { color = new Color(1f, 0.25f, 0.1f, 0.9f) };
            MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // On the placed stone: hover text, the offering, and the runes' slow red pulse.
    internal class ChallengeStone : MonoBehaviour, Hoverable, Interactable
    {
        private MeshRenderer[] _runes;
        private Light _light;

        private void Awake()
        {
            var runes = new List<MeshRenderer>();
            foreach (Transform child in transform)
                if (child.name == "rune")
                    runes.Add(child.GetComponent<MeshRenderer>());
            _runes = runes.ToArray();
            _light = GetComponentInChildren<Light>();
        }

        private void Update()
        {
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 1.6f);
            foreach (MeshRenderer rune in _runes)
                if (rune != null)
                    rune.material.color = new Color(1f, 0.25f, 0.1f, 0.5f + 0.45f * pulse);
            if (_light != null)
                _light.intensity = 0.6f + 0.9f * pulse;
        }

        public string GetHoverName() => RuneStone.Name;

        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            var (item, amount) = RuneStone.Offering();
            ItemDrop drop = ObjectDB.instance == null ? null : ObjectDB.instance.GetItemPrefab(item)?.GetComponent<ItemDrop>();
            string what = amount <= 0 ? "" : " " + amount + " " + (drop != null ? drop.m_itemData.m_shared.m_name : item);
            return Localization.instance.Localize(RuneStone.Name + "\n[<color=yellow><b>$KEY_Use</b></color>] Offer" + what +
                " to call down Alfrik, " + AlfrikBoss.Title);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player player) || player != Player.m_localPlayer)
                return false;
            Vector3 toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            toPlayer = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : transform.forward;
            var (item, amount) = RuneStone.Offering();
            bool before = Entrance.Diving;
            string said = VikingVrmAvatar.Summon(player, transform.position - toPlayer * 4.5f, -toPlayer, item, amount);
            player.Message(MessageHud.MessageType.Center, said);
            // He is really coming (his dive has just begun): the stone is spent and crumbles away. A failed call leaves it.
            if (!before && Entrance.Diving)
                Crumble();
            return true;
        }

        private void Crumble()
        {
            ZNetView view = GetComponent<ZNetView>();
            Fx.SendAt(Fx.StoneBreak, transform.position + Vector3.up * 1.2f);
            Plugin.Log.LogInfo("Odin's Challenge Stone crumbles: Alfrik is called");
            if (view != null && view.IsValid() && ZNetScene.instance != null)
            {
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(gameObject);
            }
            else
                Destroy(gameObject);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
