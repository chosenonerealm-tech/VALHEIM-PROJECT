using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UniGLTF;
using UnityEngine;

namespace Alfrik
{
    // His weapons, copies of vanilla weapons (for their animations, attacks, sounds and hand attach points) with their
    // own models, loaded from glTF files in Models/ next to this plugin. Nobody can craft them.
    //  Hugr & Hamr (phase 1): the Berserkir axes. Hugr (right) is the Viking Axe (Sam, CC-BY-4.0,
    //    https://sketchfab.com/3d-models/viking-axe-af05a0aa00f24016ab2d9b959ca8ab4b); Hamr (left, and thrown) is the Simple
    //    Viking Axe (Elisey, CC-BY-4.0, https://sketchfab.com/3d-models/simple-viking-axe-smpbr-674ebfeb42254b5cae893a5c1491f4b2).
    // A model is fitted into each original mesh's box: longest side to longest side (the haft), head end to head end,
    // blade side to blade side, scaled to the same length. In his hands HeldAxes shows it; [Hugr & Hamr] tunes it live.
    internal static class Weapons
    {
        internal const string HugrHamr = "Alfrik_HugrHamr";
        private const string AxeFile = "alfrik_axe.glb";
        private const string HugrFile = "alfrik_hugr.glb";

        // Fjallbrjótr (phase 2; its prefab keeps the old id Alfrik_Skeggold): the iron sledge's two-handed slam (the
        // Demolisher's if there is no iron sledge), Berserkir-level damage made all blunt, a heavy throw-back and a hard
        // stagger, wearing Dwarven Hammer (MattDonlan, CC-BY-4.0,
        // https://sketchfab.com/3d-models/dwarven-hammer-6237aaf3b7884fddb553f9454a07ab74).
        internal const string Skeggold = "Alfrik_Skeggold";
        internal static string SkeggoldBase = "SledgeIron";
        private const string SkeggoldFile = "alfrik_sledge.glb";
        internal static Mesh SkeggoldMesh;
        internal static Material SkeggoldMaterial;

        // Hamr, the left-hand axe, which is also the one that flies (Flying Hatchet).
        internal static Mesh AxeMesh;
        internal static Material AxeMaterial;

        // Hugr, the right-hand axe (the Viking Axe model); null means Hamr's model in both hands.
        internal static Mesh HugrMesh;
        internal static Material HugrMaterial;

        // One axe as the game holds a one-handed weapon in the right hand (the prefab's "attach"), carrying the model.
        internal static GameObject AttachTemplate;

        private sealed class Model
        {
            internal Vector3[] Vertices, Normals;
            internal Vector2[] Uv;
            internal int[] Triangles;
            internal Texture Main, Normal;
        }

        internal static void Init() => PrefabManager.OnVanillaPrefabsAvailable += Create;

        internal static string DisplayName(string prefab) => prefab == HugrHamr ? "Hugr & Hamr" : prefab == Skeggold ? "Fjallbrjótr" : prefab;

        private static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            var item = new CustomItem(HugrHamr, "AxeBerzerkr");
            if (item.ItemPrefab == null || item.ItemDrop == null)
            {
                Plugin.Log.LogWarning("No AxeBerzerkr in this game version; Hugr & Hamr not made");
                return;
            }
            ItemDrop.ItemData.SharedData shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_name = "Hugr & Hamr";
            shared.m_description = "Mind and shape: the twin axes of Alfrik, The Left Fist of Odin. No other hand can hold them.";
            shared.m_maxQuality = 1;
            shared.m_aiAttackInterval = 0.35f;
            // A demigod's chops follow a moving target: he keeps turning through each one (a player turns at a fifth of that
            // speed), and they reach a little further (0.9.14: Twin Reaper missed a moving player).
            if (shared.m_attack != null)
            {
                shared.m_attack.m_speedFactorRotation = Mathf.Max(shared.m_attack.m_speedFactorRotation, 1f);
                shared.m_attack.m_attackRange = Mathf.Max(shared.m_attack.m_attackRange, 2.6f);
                // The leap (his heavy attack, the punish after his parry) turns with the one he stunned too, so it lands.
                if (shared.m_secondaryAttack != null)
                    shared.m_secondaryAttack.m_speedFactorRotation = Mathf.Max(shared.m_secondaryAttack.m_speedFactorRotation, 1f);
                Plugin.Log.LogInfo("Hugr & Hamr chop: " + shared.m_attack.m_attackAnimation + " (" + shared.m_attack.m_attackType + ", reach " +
                    shared.m_attack.m_attackRange.ToString("0.0") + " m, arc " + shared.m_attack.m_attackAngle.ToString("0") + " deg, turning " +
                    shared.m_attack.m_speedFactorRotation.ToString("0.0") + ")");
            }
            string models = Path.Combine(Plugin.Folder, "Models");
            Reskin(item.ItemPrefab, Path.Combine(models, AxeFile));
            Model hugr = AxeMaterial == null ? null : Load(Path.Combine(models, HugrFile));
            if (hugr != null)
            {
                HugrMesh = Hatchet(hugr, AxeMesh.bounds.size.x);
                HugrMaterial = new Material(AxeMaterial) { name = "Alfrik_hugr" };
                if (hugr.Main != null)
                    HugrMaterial.mainTexture = hugr.Main;
                if (HugrMaterial.HasProperty("_BumpMap"))
                    HugrMaterial.SetTexture("_BumpMap", hugr.Normal);
            }
            ItemManager.Instance.AddItem(item);
            CreateSkeggold(models, shared);
        }

        private static void CreateSkeggold(string models, ItemDrop.ItemData.SharedData hugrHamr)
        {
            if (PrefabManager.Instance.GetPrefab(SkeggoldBase) == null)
                SkeggoldBase = "SledgeDemolisher";
            var item = new CustomItem(Skeggold, SkeggoldBase);
            if (item.ItemPrefab == null || item.ItemDrop == null)
            {
                Plugin.Log.LogWarning("No " + SkeggoldBase + " in this game version; Fjallbrjótr not made");
                return;
            }
            ItemDrop.ItemData.SharedData shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_name = "Fjallbrjótr";
            shared.m_description = "The Mountain-Breaker. The great hammer Alfrik takes up when two axes are not enough.";
            shared.m_maxQuality = 1;
            shared.m_aiAttackInterval = 0.6f;
            // The Berserkir axes' damage, a quarter more, every cut turned to crushing weight.
            HitData.DamageTypes damage = hugrHamr.m_damages.Clone();
            damage.Modify(1.25f);
            damage.m_blunt += damage.m_slash + damage.m_pierce;
            damage.m_slash = damage.m_pierce = 0f;
            shared.m_damages = damage;
            shared.m_attackForce = Mathf.Max(shared.m_attackForce, 160f);
            // A sledge has only its ground slam. His normal swing is a battle axe's wide two-handed swing (its animation and
            // reach, none of its sounds: his own whoosh plays, Fx Swing2); the slam becomes his heavy blow, kept for his
            // big moves (Brain asks for it).
            Attack slam = shared.m_attack;
            Attack swing = BorrowedSwing(out string swingFrom);
            if (swing != null && slam != null)
            {
                shared.m_secondaryAttack = slam;
                shared.m_attack = swing;
                // A demigod's blow follows a moving target: he keeps turning through the swing (a player's battle axe turns
                // at a fifth of the speed), and it reaches a little further and sweeps wider.
                swing.m_speedFactorRotation = Mathf.Max(swing.m_speedFactorRotation, 1f);
                swing.m_attackRange = Mathf.Max(swing.m_attackRange, 3f);
                swing.m_attackAngle = Mathf.Max(swing.m_attackAngle, 180f);
                Plugin.Log.LogInfo("Fjallbrjotr swing: " + swing.m_attackAnimation + " from " + swingFrom + " (" + swing.m_attackType +
                    ", reach " + swing.m_attackRange.ToString("0.0") + " m, arc " + swing.m_attackAngle.ToString("0") + " deg, turning " +
                    swing.m_speedFactorRotation.ToString("0.0") + "); the slam (" + slam.m_attackAnimation + ") is his heavy blow");
            }
            else
                Plugin.Log.LogWarning("Fjallbrjotr: no battle axe to borrow a swing from; the slam stays his only attack");
            foreach (Attack attack in new[] { shared.m_attack, shared.m_secondaryAttack })
                if (attack != null)
                {
                    attack.m_staggerMultiplier = Mathf.Max(attack.m_staggerMultiplier, 3f);
                    attack.m_forceMultiplier = Mathf.Max(attack.m_forceMultiplier, 1.5f);
                }
            Plugin.Log.LogInfo("Fjallbrjotr on " + SkeggoldBase + ": blunt " + damage.m_blunt.ToString("0") + ", force " + shared.m_attackForce.ToString("0") +
                ", heavy attack " + (shared.m_secondaryAttack != null && !string.IsNullOrEmpty(shared.m_secondaryAttack.m_attackAnimation)
                    ? shared.m_secondaryAttack.m_attackAnimation : "none"));
            // The sledge's own ground slam stays: its area blow (made half again as wide for him) with the game's own
            // shockwave, dust and sound where it lands (its trigger effects), on top of his own (Fx Swing2, Hit2).
            if (slam != null)
            {
                float width = slam.m_attackRayWidth;
                if (slam.m_attackType == Attack.AttackType.Area)
                    slam.m_attackRayWidth *= 1.5f;
                // The item's trigger effects play with every attack it makes: moved onto the slam, so the shockwave and
                // dust come only with the slam, not with every swing.
                if (shared.m_attack != slam && (slam.m_triggerEffect?.m_effectPrefabs?.Length ?? 0) == 0 && shared.m_triggerEffect != null)
                {
                    slam.m_triggerEffect = shared.m_triggerEffect;
                    shared.m_triggerEffect = new EffectList { m_effectPrefabs = new EffectList.EffectData[0] };
                }
                Plugin.Log.LogInfo("Fjallbrjotr slam: " + slam.m_attackType + ", reach " + slam.m_attackRange.ToString("0.0") + " m, radius " +
                    width.ToString("0.0") + " -> " + slam.m_attackRayWidth.ToString("0.0") + " m, effects " +
                    (slam.m_triggerEffect?.m_effectPrefabs?.Length ?? 0) + " + " + (shared.m_triggerEffect?.m_effectPrefabs?.Length ?? 0));
            }

            Model model = Load(Path.Combine(models, SkeggoldFile));
            if (model != null)
            {
                MeshRenderer original = item.ItemPrefab.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.sharedMaterial != null);
                SkeggoldMaterial = Skin(original != null ? original.sharedMaterial : AxeMaterial, model);
                SkeggoldMaterial.name = "Alfrik_skeggold";
                SkeggoldMesh = Hatchet(model, 1.2f);
                // Lying on the ground it wears the model too.
                foreach (MeshFilter filter in item.ItemPrefab.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && filter.GetComponent<MeshRenderer>() != null)
                    {
                        filter.sharedMesh = Fit(model, filter.sharedMesh);
                        filter.GetComponent<MeshRenderer>().sharedMaterials = new[] { SkeggoldMaterial };
                    }
            }
            ItemManager.Instance.AddItem(item);
        }

        private static readonly string[] SwingBases = { "Battleaxe", "BattleaxeCrystal" };
        private static readonly System.Reflection.MethodInfo ShallowCopy =
            typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // A copy of a vanilla battle axe's normal swing. The copy shares the battle axe's effect lists, so it is given empty
        // ones of its own (emptying the shared ones would silence the players' battle axes).
        private static Attack BorrowedSwing(out string from)
        {
            foreach (string name in SwingBases)
            {
                Attack attack = PrefabManager.Instance.GetPrefab(name)?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_attack;
                if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation))
                    continue;
                var copy = (Attack)ShallowCopy.Invoke(attack, null);
                foreach (System.Reflection.FieldInfo field in typeof(Attack).GetFields(System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    if (field.FieldType == typeof(EffectList))
                        field.SetValue(copy, new EffectList { m_effectPrefabs = new EffectList.EffectData[0] });
                from = name;
                return copy;
            }
            from = null;
            return null;
        }

        // Empties every effect list (sounds and particles) on an item's data or one of its attacks.
        private static void Silence(object owner)
        {
            if (owner == null)
                return;
            foreach (System.Reflection.FieldInfo field in owner.GetType().GetFields(System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                if (field.FieldType == typeof(EffectList) && field.GetValue(owner) is EffectList effects)
                    effects.m_effectPrefabs = new EffectList.EffectData[0];
        }

        private static void Reskin(GameObject prefab, string path)
        {
            Model model = Load(path);
            if (model == null)
                return;
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null).ToArray();
            foreach (MeshFilter filter in filters)
            {
                Mesh fitted = Fit(model, filter.sharedMesh);
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Material material = Skin(renderer.sharedMaterial, model);
                Plugin.Log.LogInfo("Hugr & Hamr: " + filter.name + " (" + filter.sharedMesh.bounds.size.ToString("F2") + ") gets the axe model");
                filter.sharedMesh = fitted;
                renderer.sharedMaterials = new[] { material };
                if (AxeMaterial == null)
                {
                    AxeMaterial = material;
                    AxeMesh = Hatchet(model, filter.sharedMesh.bounds.size.magnitude);
                }
            }
            foreach (SkinnedMeshRenderer skin in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Reskin(skin, model);
            AttachTemplate = prefab.transform.Find("attach")?.gameObject;
            if (filters.Length == 0)
                Plugin.Log.LogWarning("Hugr & Hamr: the Berserkir axes have no meshes to replace");
            foreach (Transform part in prefab.GetComponentsInChildren<Transform>(true))
                Plugin.Log.LogInfo("Hugr & Hamr part: " + PartPath(part, prefab.transform) + " [" +
                    string.Join(", ", part.GetComponents<Component>().Select(c => c.GetType().Name)) + "]");
        }

        // The thrown axe: centred on itself, haft along x, blade along y and the flat side along z, so it spins end over
        // end around z.
        private static Mesh Hatchet(Model model, float length)
        {
            Frame frame = FrameOf(model.Vertices);
            float scale = Mathf.Clamp(length, 0.5f, 1.2f) / Mathf.Max(frame.Length, 0.0001f) * Plugin.AxeScale.Value;
            var mesh = new Mesh { name = "Alfrik_hatchet" };
            mesh.vertices = model.Vertices.Select(v => frame.ToLocal(v) * scale).ToArray();
            mesh.normals = model.Normals.Select(frame.Unrotate).ToArray();
            mesh.uv = model.Uv;
            mesh.triangles = model.Triangles;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static string PartPath(Transform part, Transform root) =>
            part == root || part.parent == null ? part.name : PartPath(part.parent, root) + "/" + part.name;

        // The axes as held ("attach_skin"): one skinned mesh holding both axes, each bound to one hand bone. Each hand's
        // axe is replaced by the model, fitted along its principal axes and bound wholly to that hand bone.
        private static void Reskin(SkinnedMeshRenderer skin, Model model)
        {
            Mesh original = skin.sharedMesh;
            if (original == null)
                return;
            if (!original.isReadable)
            {
                Plugin.Log.LogWarning("Hugr & Hamr: the held axes (" + skin.name + ") cannot be read; they keep the Berserkir look");
                return;
            }
            Vector3[] vertices = original.vertices;
            BoneWeight[] weights = original.boneWeights;
            if (vertices.Length == 0 || weights.Length != vertices.Length)
                return;
            int[] hands = weights.GroupBy(w => w.boneIndex0).OrderByDescending(g => g.Count()).Take(2).Select(g => g.Key).ToArray();
            var clouds = hands.Select(_ => new List<Vector3>()).ToArray();
            var centres = hands.Select(bone => Average(vertices.Where((v, i) => weights[i].boneIndex0 == bone))).ToArray();
            foreach (Vector3 v in vertices)
            {
                int nearest = 0;
                for (int h = 1; h < hands.Length; h++)
                    if ((v - centres[h]).sqrMagnitude < (v - centres[nearest]).sqrMagnitude)
                        nearest = h;
                clouds[nearest].Add(v);
            }

            Frame source = FrameOf(model.Vertices);
            var newVertices = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newUv = new List<Vector2>();
            var newTriangles = new List<int>();
            var newWeights = new List<BoneWeight>();
            for (int h = 0; h < hands.Length; h++)
            {
                Frame target = FrameOf(clouds[h]);
                float scale = target.Length / Mathf.Max(source.Length, 0.0001f) * Plugin.AxeScale.Value;
                int start = newVertices.Count;
                newVertices.AddRange(model.Vertices.Select(v => target.FromLocal(source.ToLocal(v) * scale)));
                newNormals.AddRange(model.Normals.Select(n => target.Rotate(source.Unrotate(n))));
                newUv.AddRange(model.Uv);
                newTriangles.AddRange(model.Triangles.Select(t => t + start));
                newWeights.AddRange(model.Vertices.Select(_ => new BoneWeight { boneIndex0 = hands[h], weight0 = 1f }));
            }
            var mesh = new Mesh { name = "Alfrik_" + original.name };
            mesh.SetVertices(newVertices);
            mesh.SetNormals(newNormals);
            mesh.SetUVs(0, newUv);
            mesh.SetTriangles(newTriangles, 0);
            mesh.boneWeights = newWeights.ToArray();
            mesh.bindposes = original.bindposes;
            mesh.RecalculateTangents();
            mesh.bounds = original.bounds;
            skin.sharedMesh = mesh;
            skin.sharedMaterials = new[] { Skin(skin.sharedMaterial, model) };
            Plugin.Log.LogInfo("Hugr & Hamr: held axes (" + skin.name + ") get the axe model on bones " + string.Join(", ", hands));
        }

        private static Vector3 Average(IEnumerable<Vector3> points)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Vector3 p in points)
            {
                sum += p;
                count++;
            }
            return count == 0 ? Vector3.zero : sum / count;
        }

        // A shape's own axes: the haft (longest, pointing at the head), the blade (pointing to the edge) and their cross,
        // around the middle of its box, so one axe can be laid exactly where another one lies.
        private sealed class Frame
        {
            internal Vector3 Centre, Haft, Blade, Side;
            internal float Length;

            internal Vector3 ToLocal(Vector3 p)
            {
                Vector3 d = p - Centre;
                return new Vector3(Vector3.Dot(d, Haft), Vector3.Dot(d, Blade), Vector3.Dot(d, Side));
            }

            internal Vector3 FromLocal(Vector3 l) => Centre + Haft * l.x + Blade * l.y + Side * l.z;
            internal Vector3 Unrotate(Vector3 n) => new Vector3(Vector3.Dot(n, Haft), Vector3.Dot(n, Blade), Vector3.Dot(n, Side));
            internal Vector3 Rotate(Vector3 l) => Haft * l.x + Blade * l.y + Side * l.z;
        }

        private static Frame FrameOf(IList<Vector3> points)
        {
            Vector3 mean = Average(points);
            var cov = new float[3, 3];
            foreach (Vector3 p in points)
            {
                Vector3 d = p - mean;
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 3; j++)
                        cov[i, j] += d[i] * d[j];
            }
            Vector3[] axes = PrincipalAxes(cov);
            Vector3 haft = axes[0], blade = axes[1];

            float min = float.MaxValue, max = float.MinValue;
            foreach (Vector3 p in points)
            {
                float t = Vector3.Dot(p - mean, haft);
                min = Mathf.Min(min, t);
                max = Mathf.Max(max, t);
            }
            float quarter = (max - min) * 0.25f;
            float Spread(bool top)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (Vector3 p in points)
                {
                    float t = Vector3.Dot(p - mean, haft);
                    if (top ? t > max - quarter : t < min + quarter)
                    {
                        float b = Vector3.Dot(p - mean, blade);
                        lo = Mathf.Min(lo, b);
                        hi = Mathf.Max(hi, b);
                    }
                }
                return hi < lo ? 0f : hi - lo;
            }
            if (Spread(false) > Spread(true))
            {
                haft = -haft;
                (min, max) = (-max, -min);
            }

            // The blade side: where the head reaches further from the line of the handle.
            float line = 0f, lineCount = 0f, edgeHi = float.MinValue, edgeLo = float.MaxValue;
            foreach (Vector3 p in points)
            {
                float t = Vector3.Dot(p - mean, haft), b = Vector3.Dot(p - mean, blade);
                if (t < (min + max) * 0.5f)
                {
                    line += b;
                    lineCount++;
                }
                else if (t > max - quarter)
                {
                    edgeHi = Mathf.Max(edgeHi, b);
                    edgeLo = Mathf.Min(edgeLo, b);
                }
            }
            line = lineCount > 0f ? line / lineCount : 0f;
            if (edgeHi >= edgeLo && line - edgeLo > edgeHi - line)
                blade = -blade;
            Vector3 side = Vector3.Cross(haft, blade);

            Vector3 lo3 = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi3 = -lo3;
            foreach (Vector3 p in points)
            {
                var l = new Vector3(Vector3.Dot(p - mean, haft), Vector3.Dot(p - mean, blade), Vector3.Dot(p - mean, side));
                lo3 = Vector3.Min(lo3, l);
                hi3 = Vector3.Max(hi3, l);
            }
            Vector3 mid = (lo3 + hi3) * 0.5f;
            return new Frame
            {
                Centre = mean + haft * mid.x + blade * mid.y + side * mid.z,
                Haft = haft,
                Blade = blade,
                Side = side,
                Length = hi3.x - lo3.x,
            };
        }

        // Eigenvectors of a symmetric 3x3 matrix (Jacobi rotations), largest eigenvalue first.
        private static Vector3[] PrincipalAxes(float[,] a)
        {
            var v = new float[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 32; sweep++)
            {
                int p = 0, q = 1;
                if (Mathf.Abs(a[0, 2]) > Mathf.Abs(a[p, q])) { p = 0; q = 2; }
                if (Mathf.Abs(a[1, 2]) > Mathf.Abs(a[p, q])) { p = 1; q = 2; }
                if (Mathf.Abs(a[p, q]) < 1e-9f)
                    break;
                float theta = 0.5f * Mathf.Atan2(2f * a[p, q], a[q, q] - a[p, p]);
                float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
                for (int k = 0; k < 3; k++)
                {
                    float akp = a[k, p], akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[k, q] = s * akp + c * akq;
                }
                for (int k = 0; k < 3; k++)
                {
                    float apk = a[p, k], aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk;
                    a[q, k] = s * apk + c * aqk;
                }
                for (int k = 0; k < 3; k++)
                {
                    float vkp = v[k, p], vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq;
                    v[k, q] = s * vkp + c * vkq;
                }
            }
            return new[] { 0, 1, 2 }.OrderByDescending(i => a[i, i])
                .Select(i => new Vector3(v[0, i], v[1, i], v[2, i]).normalized).ToArray();
        }

        private static Model Load(string path)
        {
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("Weapon model " + path + " is missing; the vanilla model is kept");
                return null;
            }
            try
            {
                GltfData data = new GlbBinaryParser(File.ReadAllBytes(path), path).Parse();
                VikingVrmAvatar.FindShaders(true);
                RuntimeGltfInstance instance;
                try
                {
                    instance = new ImporterContext(data).Load();
                }
                finally
                {
                    VikingVrmAvatar.FindShaders(false);
                }
                GameObject root = instance.Root;
                root.SetActive(false);
                Object.DontDestroyOnLoad(root);
                MeshFilter filter = root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null);
                if (filter == null || filter.sharedMesh.vertexCount == 0)
                {
                    Plugin.Log.LogWarning("Weapon model " + path + " has no readable mesh");
                    return null;
                }
                Matrix4x4 toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Mesh mesh = filter.sharedMesh;
                Material material = filter.GetComponent<Renderer>()?.sharedMaterial;
                return new Model
                {
                    Vertices = mesh.vertices.Select(v => toRoot.MultiplyPoint3x4(v)).ToArray(),
                    Normals = mesh.normals.Select(n => toRoot.MultiplyVector(n).normalized).ToArray(),
                    Uv = mesh.uv,
                    Triangles = mesh.triangles,
                    Main = material == null ? null : material.mainTexture,
                    Normal = material != null && material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null,
                };
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Weapon model " + path + " could not be loaded: " + e.Message);
                return null;
            }
        }

        private static Mesh Fit(Model model, Mesh target)
        {
            Bounds sb = BoundsOf(model.Vertices);
            Bounds tb = target.bounds;
            int[] sAx = AxesBySize(sb.size), tAx = AxesBySize(tb.size);
            float tHead = 1f, tBlade = 1f;
            if (target.isReadable && target.vertexCount > 0)
            {
                Vector3[] tv = target.vertices;
                tHead = HeadSign(tv, tAx);
                tBlade = BladeSign(tv, tAx, tHead);
            }
            float sHead = HeadSign(model.Vertices, sAx);
            float sBlade = BladeSign(model.Vertices, sAx, sHead);

            // Rotation taking each of the model's axes onto the matching axis of the original, without mirroring.
            var rotation = Matrix4x4.zero;
            rotation[tAx[0], sAx[0]] = sHead * tHead;
            rotation[tAx[1], sAx[1]] = sBlade * tBlade;
            rotation[tAx[2], sAx[2]] = 1f;
            rotation[3, 3] = 1f;
            if (rotation.determinant < 0f)
                rotation[tAx[2], sAx[2]] = -1f;
            float scale = tb.size[tAx[0]] / Mathf.Max(sb.size[sAx[0]], 0.0001f);
            Quaternion extra = Quaternion.identity;
            Vector3 center = tb.center;

            var mesh = new Mesh { name = "Alfrik_" + target.name };
            mesh.vertices = model.Vertices.Select(v => center + extra * (rotation.MultiplyVector(v - sb.center) * scale)).ToArray();
            mesh.normals = model.Normals.Select(n => extra * rotation.MultiplyVector(n)).ToArray();
            mesh.uv = model.Uv;
            mesh.triangles = model.Triangles;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Bounds BoundsOf(Vector3[] vertices)
        {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 v in vertices)
                bounds.Encapsulate(v);
            return bounds;
        }

        private static int[] AxesBySize(Vector3 size) => new[] { 0, 1, 2 }.OrderByDescending(i => size[i]).ToArray();

        // The head is the end of the haft where the shape is widest across.
        private static float HeadSign(Vector3[] v, int[] ax)
        {
            int a = ax[0], b = ax[1];
            float min = v.Min(p => p[a]), max = v.Max(p => p[a]), quarter = (max - min) * 0.25f;
            float Spread(bool top)
            {
                var part = v.Where(p => top ? p[a] > max - quarter : p[a] < min + quarter).ToArray();
                return part.Length == 0 ? 0f : part.Max(p => p[b]) - part.Min(p => p[b]);
            }
            return Spread(true) >= Spread(false) ? 1f : -1f;
        }

        // The blade is on the side of the haft where the head reaches further out.
        private static float BladeSign(Vector3[] v, int[] ax, float head)
        {
            int a = ax[0], b = ax[1];
            float min = v.Min(p => p[a]), max = v.Max(p => p[a]), mid = (min + max) * 0.5f, quarter = (max - min) * 0.25f;
            var haft = v.Where(p => head > 0f ? p[a] < mid : p[a] > mid).ToArray();
            var top = v.Where(p => head > 0f ? p[a] > max - quarter : p[a] < min + quarter).ToArray();
            if (haft.Length == 0 || top.Length == 0)
                return 1f;
            float line = haft.Average(p => p[b]);
            return top.Max(p => p[b]) - line >= line - top.Min(p => p[b]) ? 1f : -1f;
        }

        private static Material Skin(Material original, Model model)
        {
            var material = original != null ? new Material(original) : new Material(Shader.Find("Standard"));
            material.name = "Alfrik_axe";
            material.color = Color.white;
            if (model.Main != null)
                material.mainTexture = model.Main;
            if (material.HasProperty("_BumpMap"))
                material.SetTexture("_BumpMap", model.Normal);
            if (material.HasProperty("_EmissionMap"))
                material.SetTexture("_EmissionMap", null);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", Color.black);
            return material;
        }
    }
}
