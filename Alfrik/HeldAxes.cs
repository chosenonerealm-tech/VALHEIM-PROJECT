using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Alfrik
{
    // Hugr & Hamr in his hands, on every game. The game draws held dual axes as one skinned mesh ("attach_skin") whose
    // data cannot be read, so it cannot be refitted. Instead that mesh is hidden on him and one axe model is put on each
    // hand joint (Hugr, the Viking Axe, in the right; Hamr, the Simple Viking Axe, in the left), gripped the way the game grips a vanilla one-handed iron axe in the right hand and a torch in the left
    // (their "attach" boxes: haft along the long side, head at the far end, same length as the iron axe). The avatar's
    // pose moves the hand joints, so the axes follow its hands. [Hugr & Hamr] Scale/Rotation/Offset/LeftRotation tune
    // them live. While Flying Hatchet is in the air his left hand is empty.
    internal static class HeldAxes
    {
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> RightInstance =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, int> RightHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentRightItemHash");
        private static readonly int Hash = Weapons.HugrHamr.GetStableHashCode();
        private static readonly int SkeggoldHash = Weapons.Skeggold.GetStableHashCode();

        private struct Grip
        {
            internal Quaternion Rotation;
            internal Vector3 Position;
            internal float Length;
        }

        private static GameObject _dressed, _right, _left;
        private static Transform _greatHand;

        // What is in his hands right now: 0 neither of his own weapons, 1 Hugr & Hamr, 2 Fjallbrjótr (BackWeapons shows
        // the rest on his back).
        internal static int HeldKind { get; private set; }

        // How long each weapon is in the hand, so the ones on his back are the same size.
        internal static float AxeLength => RightGrip().Length;
        internal static float GreatLength => (_greatGrip ?? (_greatGrip = GripOf(Weapons.SkeggoldBase, 1.4f)).Value).Length;
        private static readonly List<Renderer> _hidden = new List<Renderer>();
        private static Grip? _rightGrip, _leftGrip, _greatGrip;

        internal static void Update()
        {
            Humanoid body = AlfrikBoss.Char;
            VisEquipment visual = body == null ? null : body.GetComponent<VisEquipment>();
            GameObject held = visual == null ? null : RightInstance(visual);
            int hash = visual == null ? 0 : RightHash(visual);
            bool twin = hash == Hash && Weapons.AxeMesh != null, great = hash == SkeggoldHash && Weapons.SkeggoldMesh != null;
            HeldKind = held == null ? 0 : twin ? 1 : great ? 2 : 0;
            if (held == null || (!twin && !great))
            {
                Clear();
                return;
            }
            if (held != _dressed || _right == null)
            {
                Clear();
                if (great)
                    DressGreat(visual, held);
                else
                    Dress(visual, held);
            }
            // Trails behind the blade(s) while he swings.
            bool swinging = body.InAttack();
            foreach (TrailRenderer trail in _trails)
                if (trail != null)
                    trail.emitting = swinging;
            if (!twin)
                return;
            if (_right != null)
                _right.SetActive(!HatchetFlight.InFlight(body, Hatchet.Right));
            if (_left != null)
                _left.SetActive(!HatchetFlight.InFlight(body, Hatchet.Left));
        }

        // ---- Held the way the avatar's hand holds it ----
        // The axes hang on the hidden original body's hands; the avatar copies that body's pose but its wrists do not
        // turn exactly the same way, so the axes looked skewed in the visible hands. After the avatar is posed (just
        // before the frame is drawn) each axe is laid in the avatar's own hand instead: the haft through the palm, the
        // head on the thumb side, the blade the way the knuckles face, gripped low on the haft. Its usual place on the
        // original hand comes back on its own next frame (it stays parented there), so nothing builds up.

        private static bool _hooked;

        internal static void Hook()
        {
            if (_hooked)
                return;
            _hooked = true;
            Application.onBeforeRender += AlignToAvatar;
        }

        private static void AlignToAvatar()
        {
            if (!Plugin.AlignAxes.Value)
                return;
            GameObject avatar = VikingVrmAvatarCore.Avatar;
            Animator animator = avatar == null ? null : avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
                return;
            bool great = _right != null && _right.name == "Alfrik_Skeggold";
            if (great)
                LayGreat(_right, animator);
            else
            {
                Lay(_right, animator, false, 0.3f);
                Lay(_left, animator, true, 0.3f);
            }
        }

        // The great axe: held the way the game holds the Blackmetal battle axe in this frame of its animation (the hidden
        // body's right hand and the battle axe's grip), which its swings were made for - so the blade faces the way the
        // swing goes and nothing rolls with the wrist. Then moved so the haft runs through the avatar's visible right
        // fist, and bent through the left fist the closer that is to the haft (fully within 10 cm, not at all beyond
        // 30 cm): a smooth blend, never a switch from one hold to another, so it does not flip mid-swing.
        private static void LayGreat(GameObject axe, Animator animator)
        {
            if (axe == null || !axe.activeInHierarchy || _greatHand == null || !_greatGrip.HasValue)
                return;
            Grip grip = _greatGrip.Value;
            Quaternion natural = _greatHand.rotation * grip.Rotation * Quaternion.Euler(Plugin.AxeRotation.Value);
            Vector3 centre = _greatHand.TransformPoint(grip.Position + Plugin.AxeOffset.Value);
            Vector3 haft = natural * Vector3.right, naturalHaft = haft;
            float along = Vector3.Dot(centre - _greatHand.position, haft);

            Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (right == null || middle == null)
                return;
            Vector3 rightPalm = right.position + (middle.position - right.position) * 0.55f;
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform leftMiddle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            float off = -1f, weight = 0f;
            if (left != null)
            {
                Vector3 leftPalm = leftMiddle != null ? left.position + (leftMiddle.position - left.position) * 0.55f : left.position;
                Vector3 toLeft = leftPalm - rightPalm;
                float on = Vector3.Dot(toLeft, haft);
                off = (toLeft - haft * on).magnitude;
                weight = Mathf.Abs(on) > 0.1f ? Mathf.InverseLerp(0.3f, 0.1f, off) : 0f;
                if (weight > 0f)
                    haft = Vector3.Slerp(haft, toLeft.normalized * Mathf.Sign(on), weight).normalized;
            }
            Vector3 blade = Vector3.ProjectOnPlane(natural * Vector3.up, haft);
            if (blade.sqrMagnitude < 0.000001f)
                return;
            blade.Normalize();
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(haft, blade), blade);
            axe.transform.SetPositionAndRotation(rightPalm + haft * along, rotation);
            ReportHold(off, weight, Vector3.Angle(naturalHaft, haft), Vector3.Distance(rightPalm, _greatHand.position));
        }

        private static float _nextHoldReport;
        private static int _holdReports;

        // For the log, now and then while he swings: how far his left fist is from the haft, how much the haft is bent
        // to it, and how far the visible right fist is from the hidden hand the game's hold is built on.
        private static void ReportHold(float off, float weight, float bent, float shift)
        {
            Humanoid body = AlfrikBoss.Char;
            if (body == null || !body.InAttack() || Time.time < _nextHoldReport || _holdReports >= 30)
                return;
            _nextHoldReport = Time.time + 8f;
            _holdReports++;
            Plugin.Log.LogInfo(string.Format("Skeggold hold: left fist {0:0.00} m off the haft, bent {1:0}% ({2:0} deg), right fist {3:0.00} m from the game's hand",
                off, weight * 100f, bent, shift));
        }

        private static void Lay(GameObject axe, Animator animator, bool left, float gripLow)
        {
            if (axe == null || !axe.activeInHierarchy)
                return;
            Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform middle = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            Transform index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            if (hand == null || middle == null || index == null || little == null)
                return;
            Vector3 knuckles = middle.position - hand.position;
            if (knuckles.sqrMagnitude < 0.000001f)
                return;
            Vector3 blade = knuckles.normalized;
            Vector3 haft = Vector3.ProjectOnPlane(index.position - little.position, blade);
            if (haft.sqrMagnitude < 0.000001f)
                return;
            haft.Normalize();
            // The model: haft along x toward the head, blade along y.
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(haft, blade), blade) * Quaternion.Euler(Plugin.AxeRotation.Value) *
                (left ? Quaternion.Euler(Plugin.AxeLeftRotation.Value) : Quaternion.identity);
            MeshFilter filter = axe.GetComponent<MeshFilter>();
            float length = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.size.x * axe.transform.lossyScale.x : 0.8f;
            Vector3 palm = hand.position + knuckles * 0.55f;
            axe.transform.SetPositionAndRotation(palm + rotation * (Vector3.right * (length * gripLow)) + rotation * Plugin.AxeOffset.Value, rotation);
        }

        private static readonly List<TrailRenderer> _trails = new List<TrailRenderer>();

        // A blood-red streak off the blade's edge, glowing ember at its head (Skeggöld's darker and wider).
        private static void AddTrail(GameObject axe, Mesh mesh, bool great = false)
        {
            var at = new GameObject("Alfrik_AxeTrail");
            at.transform.SetParent(axe.transform, false);
            at.transform.localPosition = new Vector3(mesh.bounds.max.x * 0.85f, mesh.bounds.center.y, 0f);
            TrailRenderer trail = at.AddComponent<TrailRenderer>();
            trail.sharedMaterial = Aura.Soft;
            trail.time = 0.18f;
            trail.minVertexDistance = 0.03f;
            trail.widthMultiplier = great ? 0.5f : 0.32f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var colours = new Gradient();
            colours.SetKeys(
                great
                    ? new[] { new GradientColorKey(new Color(0.8f, 0.08f, 0.04f), 0f), new GradientColorKey(new Color(0.45f, 0f, 0f), 0.5f), new GradientColorKey(new Color(0.15f, 0f, 0f), 1f) }
                    : new[] { new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0f), new GradientColorKey(new Color(0.85f, 0.05f, 0.02f), 0.4f), new GradientColorKey(new Color(0.35f, 0f, 0f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = colours;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;
            _trails.Add(trail);
        }

        // Skeggöld: one great axe in the right hand, gripped like the Blackmetal battle axe it is built on.
        private static void DressGreat(VisEquipment visual, GameObject held)
        {
            if (visual.m_rightHand == null)
                return;
            Hide(held);
            Grip grip = _greatGrip ?? (_greatGrip = GripOf(Weapons.SkeggoldBase, 1.4f)).Value;
            var axe = new GameObject("Alfrik_Skeggold");
            axe.transform.SetParent(visual.m_rightHand, false);
            axe.AddComponent<MeshFilter>().sharedMesh = Weapons.SkeggoldMesh;
            axe.AddComponent<MeshRenderer>().sharedMaterial = Weapons.SkeggoldMaterial;
            axe.transform.localRotation = grip.Rotation * Quaternion.Euler(Plugin.AxeRotation.Value);
            axe.transform.localPosition = grip.Position + Plugin.AxeOffset.Value;
            axe.transform.localScale = Vector3.one * (grip.Length / Mathf.Max(Weapons.SkeggoldMesh.bounds.size.x, 0.0001f)) * Plugin.AxeScale.Value;
            // No trail on Skeggöld: it drew a line cutting across the screen.
            _greatHand = visual.m_rightHand;
            _right = axe;
            _dressed = held;
        }

        private static void Hide(GameObject held)
        {
            foreach (Renderer renderer in held.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && (renderer is SkinnedMeshRenderer || renderer is MeshRenderer))
                {
                    renderer.enabled = false;
                    _hidden.Add(renderer);
                }
        }

        internal static void Clear()
        {
            if (_right != null)
                Object.Destroy(_right);
            if (_left != null)
                Object.Destroy(_left);
            foreach (Renderer renderer in _hidden)
                if (renderer != null)
                    renderer.enabled = true;
            _hidden.Clear();
            _right = _left = _dressed = null;
            _greatHand = null;
            _trails.Clear();
        }

        private static void Dress(VisEquipment visual, GameObject held)
        {
            Hide(held);
            _right = Make(visual.m_rightHand, false);
            _left = Make(visual.m_leftHand, true);
            _dressed = held;
        }

        private static GameObject Make(Transform hand, bool left)
        {
            if (hand == null)
                return null;
            Grip grip = left ? LeftGrip() : RightGrip();
            var axe = new GameObject(left ? "Alfrik_Hamr" : "Alfrik_Hugr");
            axe.transform.SetParent(hand, false);
            bool hugr = !left && Weapons.HugrMesh != null;
            Mesh mesh = hugr ? Weapons.HugrMesh : Weapons.AxeMesh;
            axe.AddComponent<MeshFilter>().sharedMesh = mesh;
            axe.AddComponent<MeshRenderer>().sharedMaterial = hugr ? Weapons.HugrMaterial : Weapons.AxeMaterial;
            Quaternion extra = Quaternion.Euler(Plugin.AxeRotation.Value) *
                (left ? Quaternion.Euler(Plugin.AxeLeftRotation.Value) : Quaternion.identity);
            axe.transform.localRotation = grip.Rotation * extra;
            axe.transform.localPosition = grip.Position + Plugin.AxeOffset.Value;
            float length = RightGrip().Length;
            axe.transform.localScale = Vector3.one * (length / Mathf.Max(mesh.bounds.size.x, 0.0001f)) * Plugin.AxeScale.Value;
            AddTrail(axe, mesh);
            return axe;
        }

        private static Grip RightGrip() => _rightGrip ?? (_rightGrip = GripOf("AxeIron", 0.85f)).Value;

        private static Grip LeftGrip() => _leftGrip ?? (_leftGrip = GripOf("Torch", 0.85f)).Value;

        // The axe model (haft along x towards the head, blade along y, centred) laid into a vanilla item's "attach" box.
        private static Grip GripOf(string prefabName, float fallbackLength)
        {
            GameObject prefab = ObjectDB.instance == null ? null : ObjectDB.instance.GetItemPrefab(prefabName);
            Transform attach = prefab == null ? null : prefab.transform.Find("attach");
            Bounds? box = attach == null ? null : BoxOf(attach);
            if (!box.HasValue)
            {
                Plugin.Log.LogWarning("Hugr & Hamr: no " + prefabName + " to copy a grip from; tune [Hugr & Hamr] by hand");
                return new Grip { Rotation = Quaternion.identity, Position = Vector3.zero, Length = fallbackLength };
            }
            Bounds b = box.Value;
            int haft = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
            int blade = haft == 0 ? (b.size.y >= b.size.z ? 1 : 2) : haft == 1 ? (b.size.x >= b.size.z ? 0 : 2) : (b.size.x >= b.size.y ? 0 : 1);
            var x = Vector3.zero;
            x[haft] = Mathf.Abs(b.max[haft]) >= Mathf.Abs(b.min[haft]) ? 1f : -1f;
            var y = Vector3.zero;
            y[blade] = b.center[blade] >= 0f ? 1f : -1f;
            Vector3 z = Vector3.Cross(x, y);
            var grip = new Grip { Rotation = Quaternion.LookRotation(z, y), Position = b.center, Length = b.size[haft] };
            Plugin.Log.LogInfo("Hugr & Hamr grip from " + prefabName + ": haft " + x + ", blade " + y + ", length " + grip.Length.ToString("F2") +
                ", centre " + b.center.ToString("F2"));
            return grip;
        }

        private static Bounds? BoxOf(Transform attach)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in attach.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                Matrix4x4 toAttach = attach.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = toAttach.MultiplyPoint3x4(corner);
                    if (!any)
                        bounds = new Bounds(p, Vector3.zero);
                    else
                        bounds.Encapsulate(p);
                    any = true;
                }
            }
            return any ? bounds : (Bounds?)null;
        }
    }
}
