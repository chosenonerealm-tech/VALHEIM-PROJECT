using HarmonyLib;
using UnityEngine;

namespace Alfrik
{
    // His arrival, on the game that owns him: thunder and a flash in the sky, then he dives from high above at a slant
    // and lands like a hammer blow, down on one knee with a fist to the ground (the game's kneel), the ground shaking
    // around him; he rises with a roar and the fight begins. Until then nothing can hurt him and his AI waits. His body
    // is kinematic in the dive so the game's physics and fall damage stay out of it.
    internal static class Entrance
    {
        private enum Stage { None, Dive, Kneel, Rise, Roar }

        private const float Height = 40f, Back = 14f, Speed = 34f, KneelTime = 1.6f, RiseTime = 0.5f, RoarTime = 2f, ShockRadius = 6f;

        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Character, Rigidbody> BodyOf = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");

        private static Stage _stage;
        private static Humanoid _c;
        private static Vector3 _ground;
        private static float _stageEnd;

        internal static bool Active => _stage != Stage.None && _c != null && _c == AlfrikBoss.Char;

        // His dive from the sky is under way (on this game).
        internal static bool Diving => _stage == Stage.Dive && _c != null;

        internal static int Pose => !Active ? Poses.None : _stage == Stage.Dive ? Poses.Dive : _stage == Stage.Kneel ? Poses.Landing : Poses.None;

        // He comes down on 'ground', diving in from the side 'from' points to.
        internal static void Begin(Humanoid c, Vector3 ground, Vector3 from)
        {
            if (c == null)
                return;
            if (ZoneSystem.instance != null)
                ground.y = ZoneSystem.instance.GetSolidHeight(ground);
            from.y = 0f;
            from = from.sqrMagnitude > 0.0001f ? from.normalized : -c.transform.forward;
            _c = c;
            _ground = ground;
            Rigidbody body = BodyOf(c);
            if (body != null)
            {
                body.interpolation = RigidbodyInterpolation.None;
                body.isKinematic = true;
            }
            Place(ground + Vector3.up * Height + from * Back);
            Face(-from);
            _stage = Stage.Dive;
            _stageEnd = Time.time + Height / Speed * 2f + 1f;
            Fx.Send(Fx.Descend);
        }

        internal static void Update()
        {
            if (_stage == Stage.None)
                return;
            ZNetView view = _c == null ? null : _c.GetComponent<ZNetView>();
            if (_c == null || _c.IsDead() || view == null || !view.IsValid() || !view.IsOwner() || _c != AlfrikBoss.Char)
            {
                Finish();
                return;
            }
            float now = Time.time;
            _c.SetMoveDir(Vector3.zero);
            switch (_stage)
            {
                case Stage.Dive:
                    Vector3 at = Vector3.MoveTowards(_c.transform.position, _ground, Speed * Time.deltaTime);
                    Place(at);
                    if ((at - _ground).sqrMagnitude < 0.0025f || now >= _stageEnd)
                        Land();
                    break;
                case Stage.Kneel:
                    // The kneel holds until the game is told to stop it (for players moving does that): he gets up.
                    if (now >= _stageEnd)
                    {
                        _stage = Stage.Rise;
                        _stageEnd = now + RiseTime;
                        Brain.Trigger("emote_stop");
                    }
                    break;
                case Stage.Rise:
                    if (now >= _stageEnd)
                    {
                        _stage = Stage.Roar;
                        _stageEnd = now + RoarTime;
                        Brain.Trigger("emote_roar");
                        Fx.Send(Fx.Roar);
                    }
                    break;
                case Stage.Roar:
                    if (now >= _stageEnd)
                        Finish();
                    break;
            }
        }

        private static void Land()
        {
            Place(_ground);
            Rigidbody body = BodyOf(_c);
            if (body != null)
                body.isKinematic = false;
            MaxAirAltitude(_c) = _ground.y;
            Brain.Trigger("emote_kneel");
            Fx.SendAt(Fx.Land, _ground);
            Shockwave(_c, _ground, ShockRadius);
            _stage = Stage.Kneel;
            _stageEnd = Time.time + KneelTime;
        }

        // A blast that throws everyone near him back (his landing, his roar at a new phase); it does no damage.
        internal static void Shockwave(Humanoid c, Vector3 at, float radius)
        {
            if (c == null)
                return;
            foreach (Character other in Character.GetAllCharacters())
            {
                if (other == null || other == c || other.IsDead() || !BaseAI.IsEnemy(c, other))
                    continue;
                Vector3 away = other.transform.position - at;
                away.y = 0f;
                if (away.magnitude > radius)
                    continue;
                var hit = new HitData
                {
                    m_point = other.GetCenterPoint(),
                    m_dir = away.sqrMagnitude > 0.0001f ? away.normalized : other.transform.forward * -1f,
                    m_pushForce = 90f,
                    m_staggerMultiplier = 0f,
                    m_blockable = false,
                    m_dodgeable = false,
                };
                hit.SetAttacker(c);
                other.Damage(hit);
            }
        }

        // Whatever cut the arrival short, he is left standing: no emote still holding him (an emote blocks his attacks).
        private static void Finish()
        {
            if (_c != null)
            {
                Brain.Trigger("emote_stop");
                Rigidbody body = BodyOf(_c);
                if (body != null && body.isKinematic)
                    body.isKinematic = false;
                MaxAirAltitude(_c) = _c.transform.position.y;
            }
            _stage = Stage.None;
            _c = null;
        }

        private static void Place(Vector3 position)
        {
            _c.transform.position = position;
            Rigidbody body = BodyOf(_c);
            if (body != null)
                body.position = position;
        }

        private static void Face(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                return;
            Quaternion facing = Quaternion.LookRotation(dir.normalized);
            _c.transform.rotation = facing;
            Rigidbody body = BodyOf(_c);
            if (body != null)
                body.rotation = facing;
            _c.SetLookDir(dir.normalized);
        }
    }
}
