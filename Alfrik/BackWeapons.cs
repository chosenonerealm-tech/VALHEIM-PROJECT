using UnityEngine;

namespace Alfrik
{
    // His weapons that are not in his hands, slung on his back, on every game: Fjallbrjótr diagonally across his back,
    // head over his right shoulder; Hugr & Hamr crossed lower down, heads up and out. Phase 1 (axes in hand) shows the
    // hammer, phase 2 (hammer in hand) the axes, and as a companion whichever he is not using (both when his hands are
    // empty). They are laid on the avatar's own back just before each frame is drawn (after its pose), from its chest,
    // neck, hips and shoulders, so they move with the visible body whatever its bones' own axes are.
    // [Back] Offset moves them (right, up, back) live.
    internal static class BackWeapons
    {
        private static GameObject _hammer, _hugr, _hamr;
        private static bool _hooked, _showHammer, _showAxes;

        internal static void Hook()
        {
            if (_hooked)
                return;
            _hooked = true;
            Application.onBeforeRender += Place;
        }

        // LateUpdate: which of them hang on his back now.
        internal static void Update()
        {
            Humanoid body = AlfrikBoss.Char;
            bool on = body != null && !body.IsDead() && AlfrikBoss.State != 0 && VikingVrmAvatarCore.Avatar != null;
            int held = HeldAxes.HeldKind;
            _showHammer = on && held != 2 && Weapons.SkeggoldMesh != null;
            _showAxes = on && held != 1 && Weapons.AxeMesh != null;
            if (_showHammer && _hammer == null)
                _hammer = Make("Alfrik_BackHammer", Weapons.SkeggoldMesh, Weapons.SkeggoldMaterial, HeldAxes.GreatLength);
            if (_showAxes && _hugr == null)
            {
                _hugr = Make("Alfrik_BackHugr", Weapons.HugrMesh ?? Weapons.AxeMesh, Weapons.HugrMesh != null ? Weapons.HugrMaterial : Weapons.AxeMaterial,
                    HeldAxes.AxeLength);
                _hamr = Make("Alfrik_BackHamr", Weapons.AxeMesh, Weapons.AxeMaterial, HeldAxes.AxeLength);
            }
            if (!_showHammer)
                Remove(ref _hammer);
            if (!_showAxes)
            {
                Remove(ref _hugr);
                Remove(ref _hamr);
            }
        }

        private static GameObject Make(string name, Mesh mesh, Material material, float length)
        {
            var piece = new GameObject(name);
            Object.DontDestroyOnLoad(piece);
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            piece.AddComponent<MeshRenderer>().sharedMaterial = material;
            piece.transform.localScale = Vector3.one * (length / Mathf.Max(mesh.bounds.size.x, 0.0001f)) * Plugin.AxeScale.Value;
            // Hidden until it has been laid on his back the first time.
            piece.SetActive(false);
            return piece;
        }

        private static void Remove(ref GameObject piece)
        {
            if (piece != null)
                Object.Destroy(piece);
            piece = null;
        }

        private static void Place()
        {
            if (!_showHammer && !_showAxes)
                return;
            GameObject avatar = VikingVrmAvatarCore.Avatar;
            Animator animator = avatar == null ? null : avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
                return;
            Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            Transform leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (chest == null || hips == null || neck == null || leftArm == null || rightArm == null)
                return;
            // The back's own frame: up the spine, across the shoulders, and out of the chest.
            Vector3 up = (neck.position - hips.position).normalized;
            Vector3 right = Vector3.ProjectOnPlane(rightArm.position - leftArm.position, up).normalized;
            if (up.sqrMagnitude < 0.5f || right.sqrMagnitude < 0.5f)
                return;
            Vector3 forward = Vector3.Cross(right, up);
            Vector3 offset = Plugin.BackOffset.Value;
            Vector3 back = chest.position - forward * (0.17f + offset.z) + right * offset.x + up * offset.y;

            if (_showHammer && _hammer != null)
            {
                // Haft across the back from the left hip to over the right shoulder, the head flat against the back.
                Vector3 haft = (up + right * 0.45f).normalized;
                Lay(_hammer, back - up * 0.1f, haft, Vector3.Cross(forward, haft));
            }
            if (_showAxes && _hugr != null && _hamr != null)
            {
                // Crossed lower on the back (below the hammer when both are there), heads up and out, blades outward.
                Vector3 at = back - up * (_showHammer ? 0.3f : 0.12f) - forward * 0.04f;
                Vector3 hugrHaft = (up + right * 0.7f).normalized, hamrHaft = (up - right * 0.7f).normalized;
                Lay(_hugr, at, hugrHaft, -Vector3.Cross(forward, hugrHaft));
                Lay(_hamr, at - forward * 0.03f, hamrHaft, Vector3.Cross(forward, hamrHaft));
            }
        }

        // The model: centred, haft along x toward the head, blade (or the hammer head's length) along y.
        private static void Lay(GameObject piece, Vector3 centre, Vector3 haft, Vector3 blade)
        {
            blade = Vector3.ProjectOnPlane(blade, haft);
            if (blade.sqrMagnitude < 0.000001f)
                return;
            blade.Normalize();
            piece.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(Vector3.Cross(haft, blade), blade));
            if (!piece.activeSelf)
                piece.SetActive(true);
        }
    }
}
