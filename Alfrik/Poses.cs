using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Alfrik
{
    // The poses of his warnings, on every game: Twin Reaper sinks into a crouch with the arms spread, Blade Catch draws
    // the axes together across the chest, Ravens' Flight pulls both arms up and back. Each is a small push on a few
    // humanoid muscles added to whatever the animation is doing (muscles stay within human limits, -1..1), so the body
    // keeps moving with its animation and never goes stiff or into a T-pose. The push eases in over 0.12 s and out over
    // 0.18 s, once a frame after the animation and before the avatar copies the pose. The game that owns him writes the
    // pose to his ZDO; [Poses] TellPoses turns them off.
    internal static class Poses
    {
        internal const int None = 0, Crouch = 1, Cross = 2, ArmsBack = 3, Dive = 4, Landing = 5, Twist = 6, Raise = 8, Plant = 9, Reel = 10,
            Stomp = 11;
        private static readonly int PoseKey = "alfrik_pose".GetStableHashCode();
        internal static readonly AccessTools.FieldRef<Character, Animator> AnimatorOf = AccessTools.FieldRefAccess<Character, Animator>("m_animator");

        private static readonly Dictionary<int, (string Muscle, float Push)[]> Pushes = new Dictionary<int, (string, float)[]>
        {
            [Crouch] = new[]
            {
                ("Spine Front-Back", 0.15f), ("Chest Front-Back", 0.1f),
                ("Left Upper Leg Front-Back", 0.3f), ("Right Upper Leg Front-Back", 0.3f),
                ("Left Lower Leg Stretch", -0.6f), ("Right Lower Leg Stretch", -0.6f),
                ("Left Arm Down-Up", 0.25f), ("Right Arm Down-Up", 0.25f),
                ("Left Arm Front-Back", -0.1f), ("Right Arm Front-Back", -0.1f),
                ("Left Forearm Stretch", -0.15f), ("Right Forearm Stretch", -0.15f),
            },
            [Cross] = new[]
            {
                ("Left Shoulder Front-Back", 0.2f), ("Right Shoulder Front-Back", 0.2f),
                ("Left Arm Front-Back", 0.35f), ("Right Arm Front-Back", 0.35f),
                ("Left Arm Down-Up", 0.1f), ("Right Arm Down-Up", 0.1f),
                ("Left Forearm Stretch", -0.3f), ("Right Forearm Stretch", -0.3f),
            },
            [ArmsBack] = new[]
            {
                ("Spine Front-Back", -0.08f), ("Chest Front-Back", -0.1f),
                ("Left Arm Down-Up", 0.35f), ("Right Arm Down-Up", 0.35f),
                ("Left Arm Front-Back", -0.4f), ("Right Arm Front-Back", -0.4f),
                ("Left Forearm Stretch", -0.25f), ("Right Forearm Stretch", -0.25f),
            },
            // Coming down from the sky: knees drawn up a little, arms out and back, chest open.
            [Dive] = new[]
            {
                ("Chest Front-Back", -0.1f),
                ("Left Upper Leg Front-Back", 0.15f), ("Right Upper Leg Front-Back", 0.15f),
                ("Left Lower Leg Stretch", -0.3f), ("Right Lower Leg Stretch", -0.3f),
                ("Left Arm Down-Up", 0.3f), ("Right Arm Down-Up", 0.3f),
                ("Left Arm Front-Back", -0.2f), ("Right Arm Front-Back", -0.2f),
            },
            // Phase 2. Reaping Sweep: the body wound back, the axe drawn behind.
            [Twist] = new[]
            {
                ("Spine Twist Left-Right", 0.3f), ("Chest Twist Left-Right", 0.25f),
                ("Left Arm Front-Back", -0.2f), ("Right Arm Front-Back", -0.3f),
            },
            // Executioner: the axe raised high over his head, chest back.
            [Raise] = new[]
            {
                ("Spine Front-Back", -0.1f), ("Chest Front-Back", -0.15f),
                ("Left Arm Down-Up", 0.55f), ("Right Arm Down-Up", 0.55f),
                ("Left Arm Front-Back", 0.15f), ("Right Arm Front-Back", 0.15f),
            },
            // FURY: planted low, the axe down at his side.
            [Plant] = new[]
            {
                ("Spine Front-Back", 0.2f),
                ("Left Upper Leg Front-Back", 0.25f), ("Right Upper Leg Front-Back", 0.25f),
                ("Left Lower Leg Stretch", -0.5f), ("Right Lower Leg Stretch", -0.5f),
                ("Left Arm Down-Up", -0.15f), ("Right Arm Down-Up", -0.15f),
            },
            // War Stomp: the right knee drawn up high, the body leaning back over the left leg; the foot comes down
            // as the pose lets go.
            [Stomp] = new[]
            {
                ("Spine Front-Back", -0.08f), ("Chest Front-Back", -0.05f),
                ("Right Upper Leg Front-Back", 0.6f), ("Right Lower Leg Stretch", -0.55f),
                ("Left Lower Leg Stretch", -0.12f),
                ("Left Arm Down-Up", 0.15f), ("Right Arm Down-Up", 0.1f),
            },
            // Staggered: thrown back, head up, arms flung out, knees giving.
            [Reel] = new[]
            {
                ("Spine Front-Back", -0.25f), ("Chest Front-Back", -0.2f), ("Head Nod Down-Up", 0.25f),
                ("Left Arm Down-Up", 0.2f), ("Right Arm Down-Up", 0.2f),
                ("Left Arm Front-Back", -0.25f), ("Right Arm Front-Back", -0.25f),
                ("Left Lower Leg Stretch", -0.25f), ("Right Lower Leg Stretch", -0.25f),
            },
            // On top of the game's kneel: head down, the right fist to the ground, the left arm out.
            [Landing] = new[]
            {
                ("Spine Front-Back", 0.15f), ("Head Nod Down-Up", -0.2f),
                ("Right Arm Down-Up", -0.25f), ("Right Arm Front-Back", 0.25f), ("Right Forearm Stretch", 0.3f),
                ("Left Arm Down-Up", 0.15f),
            },
        };
        // How far the hips sink in the crouch, so the bent legs keep the feet on the ground.
        private const float CrouchDrop = 0.07f;

        private static Dictionary<string, int> _muscles;
        private static Animator _animator;
        private static HumanPoseHandler _handler;
        private static HumanPose _pose;
        private static float[] _base, _wrote;
        private static Vector3 _baseBody, _wroteBody;
        private static int _shown, _written = -1;
        private static float _weight;

        // How much of a pose is on right now (0 = none), for the diagnostics.
        internal static float Weight => _weight;

        // Owner: the pose his current move warns with.
        internal static void Set(int pose)
        {
            ZNetView view = AlfrikBoss.View;
            if (pose == _written || view == null || !view.IsValid() || !view.IsOwner())
                return;
            _written = pose;
            view.GetZDO().Set(PoseKey, pose);
        }

        internal static void Update()
        {
            Humanoid body = AlfrikBoss.Char;
            ZNetView view = AlfrikBoss.View;
            if (body == null || view == null || !view.IsValid())
            {
                _weight = 0f;
                _written = -1;
                _wrote = null;
                return;
            }
            int pose = Plugin.TellPoses.Value && !body.IsDead() ? view.GetZDO().GetInt(PoseKey, None) : None;
            if (Pushes.ContainsKey(pose))
                _shown = pose;
            float target = Pushes.ContainsKey(pose) ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, target, Time.deltaTime / (target > 0f ? 0.12f : 0.18f));
            if (_weight <= 0f || !Pushes.ContainsKey(_shown))
            {
                _wrote = null;
                return;
            }

            Animator animator = AnimatorOf(body);
            if (animator == null || !animator.isHuman || animator.avatar == null)
                return;
            if (animator != _animator || _handler == null)
            {
                _handler?.Dispose();
                _animator = animator;
                _handler = new HumanPoseHandler(animator.avatar, animator.transform);
                _wrote = null;
            }
            if (_muscles == null)
            {
                _muscles = new Dictionary<string, int>();
                for (int i = 0; i < HumanTrait.MuscleName.Length; i++)
                    _muscles[HumanTrait.MuscleName[i]] = i;
            }
            _handler.GetHumanPose(ref _pose);
            float[] muscles = _pose.muscles;
            if (muscles == null || muscles.Length == 0)
                return;

            // The animation's own pose this frame. On a frame the animator did not run, the bones still hold what was
            // written last frame: the push then goes on last frame's animation pose, never on top of itself.
            if (_base == null || _base.Length != muscles.Length)
            {
                _base = new float[muscles.Length];
                _wrote = null;
            }
            if (_wrote != null && _wrote.Length == muscles.Length && Same(muscles, _wrote) && _pose.bodyPosition == _wroteBody)
            {
                System.Array.Copy(_base, muscles, muscles.Length);
                _pose.bodyPosition = _baseBody;
            }
            else
            {
                System.Array.Copy(muscles, _base, muscles.Length);
                _baseBody = _pose.bodyPosition;
            }

            foreach (var (muscle, push) in Pushes[_shown])
                if (_muscles.TryGetValue(muscle, out int i) && i < muscles.Length)
                    muscles[i] = Mathf.Clamp(muscles[i] + push * _weight, -1f, 1f);
            if (_shown == Crouch || _shown == Plant)
                _pose.bodyPosition.y -= CrouchDrop * _weight;
            _handler.SetHumanPose(ref _pose);

            // What the bones hold now, read back the way the next frame will read it.
            _handler.GetHumanPose(ref _pose);
            if (_wrote == null || _wrote.Length != _pose.muscles.Length)
                _wrote = new float[_pose.muscles.Length];
            System.Array.Copy(_pose.muscles, _wrote, _wrote.Length);
            _wroteBody = _pose.bodyPosition;
        }

        private static bool Same(float[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i] - b[i]) > 0.0005f)
                    return false;
            return true;
        }
    }
}
