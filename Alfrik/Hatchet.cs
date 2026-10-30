using System.Collections.Generic;
using UnityEngine;

namespace Alfrik
{
    // A thrown axe (Flying Hatchet: Hamr from the left hand; Ravens' Flight: Hugr from the right and Hamr from the left):
    // it spins out to where the target stood and curves back to the hand that threw it, hitting every enemy it passes,
    // once on the way out and once on the way back. Every game draws the flight from one routed call; only the game that
    // owns him deals the damage and hears when it is caught. Recall turns every axe of his in the air back at once.
    internal static class Hatchet
    {
        internal const int Right = 0, Left = 1;
        private const string Rpc = "Alfrik_Hatchet", RecallRpc = "Alfrik_Recall", PickupRpc = "Alfrik_Pickup";

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZDOID, Vector3, Vector3, int>(Rpc, OnThrow);
            ZRoutedRpc.instance?.Register<ZDOID>(RecallRpc, OnRecall);
            ZRoutedRpc.instance?.Register<ZDOID, int>(PickupRpc, OnPickup);
        }

        // Owner: he pulls the axe of that hand out of the ground (every game removes it there).
        internal static void Pickup(Humanoid thrower, int hand)
        {
            ZNetView view = thrower == null ? null : thrower.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, PickupRpc, view.GetZDO().m_uid, hand);
        }

        private static void OnPickup(long sender, ZDOID id, int hand) => HatchetFlight.PickUp(Find(id), hand);

        // Owner: is an axe of his in the air?
        internal static bool Flying => HatchetFlight.InFlight(AlfrikBoss.Char);

        // hover: seconds the axe circles at the far end before it turns back, 0 for none; stick: it stays in the ground
        // where it lands until he picks it up (Ravens' Flight). Both travel in the call with the hand
        // (hand + 10 x tenths of a second + 1000 if it sticks).
        internal static void Throw(Humanoid thrower, Character target, int hand = Left, float hover = 0f, bool stick = false)
        {
            ZNetView view = thrower == null ? null : thrower.GetComponent<ZNetView>();
            if (target == null || view == null || !view.IsValid() || ZRoutedRpc.instance == null)
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, view.GetZDO().m_uid, HandPosition(thrower, hand),
                target.GetCenterPoint(), hand + 10 * Mathf.Clamp(Mathf.RoundToInt(hover * 10f), 0, 99) + (stick ? 1000 : 0));
        }

        internal static void Recall(Humanoid thrower)
        {
            ZNetView view = thrower == null ? null : thrower.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RecallRpc, view.GetZDO().m_uid);
        }

        private static Humanoid Find(ZDOID id)
        {
            GameObject instance = ZNetScene.instance == null ? null : ZNetScene.instance.FindInstance(id);
            return instance == null ? null : instance.GetComponent<Humanoid>();
        }

        private static void OnThrow(long sender, ZDOID id, Vector3 from, Vector3 to, int code)
        {
            bool stick = code >= 1000;
            code %= 1000;
            int hand = code % 10;
            float hover = code / 10 / 10f;
            Humanoid thrower = Find(id);
            if (thrower == null || Weapons.AxeMesh == null)
                return;
            bool hugr = hand == Right && Weapons.HugrMesh != null;
            var axe = new GameObject(hugr ? "Alfrik_FlyingHugr" : "Alfrik_FlyingHamr");
            axe.transform.position = from;
            axe.AddComponent<MeshFilter>().sharedMesh = hugr ? Weapons.HugrMesh : Weapons.AxeMesh;
            axe.AddComponent<MeshRenderer>().sharedMaterial = hugr ? Weapons.HugrMaterial : Weapons.AxeMaterial;
            axe.AddComponent<HatchetFlight>().Begin(thrower, to, hand, hover, stick);
            Sounds.PlayAt("swing", from);
        }

        private static void OnRecall(long sender, ZDOID id) => HatchetFlight.RecallAll(Find(id));

        internal static Vector3 HandPosition(Humanoid humanoid, int hand = Right)
        {
            VisEquipment visual = humanoid.GetComponent<VisEquipment>();
            Transform joint = visual == null ? null : hand == Left ? visual.m_leftHand : visual.m_rightHand;
            return joint != null ? joint.position : humanoid.transform.position + Vector3.up * 1.4f;
        }
    }

    internal class HatchetFlight : MonoBehaviour
    {
        private static readonly HashSet<HatchetFlight> Active = new HashSet<HatchetFlight>();

        // Every game: is an axe of his in the air (from that hand, or from either with hand -1)? That hand is then empty.
        internal static bool InFlight(Character thrower, int hand = -1)
        {
            foreach (HatchetFlight flight in Active)
                if (flight != null && flight._thrower == thrower && (hand < 0 || flight._hand == hand))
                    return true;
            return false;
        }

        internal static void RecallAll(Character thrower)
        {
            foreach (HatchetFlight flight in Active)
                if (flight != null && flight._thrower == thrower && !flight._returning)
                    flight.TurnBack();
        }

        // Is the axe of that hand stuck in the ground, and where?
        internal static bool Stuck(Character thrower, int hand, out Vector3 at)
        {
            foreach (HatchetFlight flight in Active)
                if (flight != null && flight._thrower == thrower && flight._hand == hand && flight._stuck)
                {
                    at = flight.transform.position;
                    return true;
                }
            at = Vector3.zero;
            return false;
        }

        internal static void PickUp(Character thrower, int hand)
        {
            foreach (HatchetFlight flight in new List<HatchetFlight>(Active))
                if (flight != null && flight._thrower == thrower && flight._hand == hand && flight._stuck)
                {
                    flight._caught = true;
                    flight.gameObject.SetActive(false);
                    Destroy(flight.gameObject);
                }
        }

        private const float Speed = 15f, Reach = 22f, HitRadius = 1f, Spin = 900f, AboveGround = 0.7f, Whoosh = 0.35f;

        private Humanoid _thrower;
        private int _hand;
        private bool _owner, _returning, _caught, _hovering, _stick, _stuck;
        private TrailRenderer _trail;
        private float _hover, _hoverEnd, _orbit;
        private Vector3 _to, _turn, _curve, _side = Vector3.right;
        private float _t, _returnLength, _angle, _nextWhoosh;
        private readonly HashSet<Character> _hit = new HashSet<Character>();

        // Out past where the target stood by 2 m on the level, never into the ground, so it stays in sight.
        internal void Begin(Humanoid thrower, Vector3 to, int hand, float hover = 0f, bool stick = false)
        {
            _thrower = thrower;
            _hand = hand;
            _hover = hover;
            _stick = stick;
            _orbit = hand == Hatchet.Right ? 0f : 180f;
            Active.Add(this);
            ZNetView view = thrower.GetComponent<ZNetView>();
            _owner = view != null && view.IsValid() && view.IsOwner();
            Vector3 dir = to - transform.position;
            if (dir.magnitude > Reach)
                to = transform.position + dir.normalized * Reach;
            Vector3 level = new Vector3(dir.x, 0f, dir.z);
            _to = to + (level.sqrMagnitude > 0.0001f ? level.normalized * 2f : Vector3.zero);
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(_to, out float ground))
                _to.y = Mathf.Max(_to.y, ground + AboveGround);

            TrailRenderer trail = gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = Textures.Get("p1_puff");
            trail.textureMode = LineTextureMode.Stretch;
            trail.time = 0.3f;
            trail.startWidth = 0.4f;
            trail.endWidth = 0f;
            trail.startColor = hand == Hatchet.Right ? new Color(0.45f, 0.55f, 1f, 0.8f) : new Color(1f, 0.3f, 0.15f, 0.8f);
            trail.endColor = new Color(trail.startColor.r, trail.startColor.g, trail.startColor.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail = trail;
            // A stuck axe stays until he picks it up (or calls it back).
            if (!stick)
                Destroy(gameObject, 6f + hover);
        }

        private void Update()
        {
            if (_thrower == null || _thrower.IsDead())
            {
                Destroy(gameObject);
                return;
            }
            if (_stuck)
                return;
            float dt = Time.deltaTime;
            Vector3 before = transform.position;
            Fly(dt);
            if (_stuck)
                return;
            if (this == null || !gameObject.activeSelf)
                return;
            // End over end around the flat of the axe, which is turned across the flight.
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            if (moved.sqrMagnitude > 0.000001f)
                _side = Vector3.Cross(Vector3.up, moved.normalized);
            _angle += Spin * dt;
            transform.rotation = Quaternion.AngleAxis(_angle, _side) * Quaternion.LookRotation(_side, Vector3.up);
            if (Time.time >= _nextWhoosh)
            {
                _nextWhoosh = Time.time + Whoosh;
                Sounds.PlayAt("spin", transform.position, 0.7f, 30f);
            }
            if (_owner)
                Strike();
        }

        private void Fly(float dt)
        {
            if (_hovering)
            {
                // Circling like a raven around where it flew, then back.
                _orbit += 300f * dt;
                float a = _orbit * Mathf.Deg2Rad;
                transform.position = _to + new Vector3(Mathf.Cos(a) * 1.6f, 0.25f * Mathf.Sin(a * 2f), Mathf.Sin(a) * 1.6f);
                if (Time.time >= _hoverEnd)
                    TurnBack();
            }
            else if (!_returning)
            {
                transform.position = Vector3.MoveTowards(transform.position, _to, Speed * dt);
                if ((transform.position - _to).sqrMagnitude < 0.04f)
                {
                    if (_stick)
                        Stick();
                    else if (_hover > 0f)
                    {
                        _hovering = true;
                        _hoverEnd = Time.time + _hover;
                    }
                    else
                        TurnBack();
                }
            }
            else
            {
                _t += Speed * dt / _returnLength;
                transform.position = Bezier(_turn, _curve, Hatchet.HandPosition(_thrower, _hand), Mathf.Min(_t, 1f));
                if (_t >= 1f)
                {
                    _caught = true;
                    gameObject.SetActive(false);
                    Destroy(gameObject);
                }
            }
        }

        // Back along a sideways curve.
        // Down into the ground, head first, the haft standing up at a slant along the way it flew.
        private void Stick()
        {
            _stuck = true;
            Vector3 flight = new Vector3(-_side.z, 0f, _side.x);
            if (flight.sqrMagnitude < 0.0001f)
                flight = Vector3.forward;
            flight.Normalize();
            Vector3 ground = transform.position;
            if (ZoneSystem.instance != null)
                ground.y = ZoneSystem.instance.GetSolidHeight(ground);
            Vector3 down = (Vector3.down * 0.85f + flight * 0.5f).normalized;
            Vector3 blade = Vector3.ProjectOnPlane(flight, down).normalized;
            transform.rotation = Quaternion.LookRotation(Vector3.Cross(down, blade), blade);
            transform.position = ground - down * 0.15f;
            if (_trail != null)
                _trail.emitting = false;
            Sounds.PlayAt("catch", ground, 0.8f, 30f);
            Fx.WaveStepSmall(ground);
        }

        private void TurnBack()
        {
            if (_stuck)
            {
                _stuck = false;
                if (_trail != null)
                    _trail.emitting = true;
                Destroy(gameObject, 6f);
            }
            _hovering = false;
            _returning = true;
            _turn = transform.position;
            _hit.Clear();
            Vector3 back = Hatchet.HandPosition(_thrower, _hand) - _turn;
            float side = _hand == Hatchet.Right ? 1f : -1f;
            _curve = _turn + back * 0.5f + Vector3.Cross(Vector3.up, back.normalized) * 4f * side + Vector3.up;
            _returnLength = Mathf.Max(back.magnitude * 1.2f, 1f);
            _t = 0f;
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) =>
            Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

        private void Strike()
        {
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character == _thrower || character.IsDead() || _hit.Contains(character) ||
                    (character.GetCenterPoint() - transform.position).sqrMagnitude > HitRadius * HitRadius ||
                    !BaseAI.IsEnemy(_thrower, character))
                    continue;
                _hit.Add(character);
                character.Damage(MakeHit(character));
                Fx.SendAt(Fx.Hit, character.GetCenterPoint());
            }
        }

        // Most of an axe swing, scaled like his swings are (Vikings for Hire puts his damage into GetRandomSkillFactor).
        private HitData MakeHit(Character target)
        {
            ItemDrop.ItemData weapon = _thrower.GetCurrentWeapon();
            var hit = new HitData();
            if (weapon != null && weapon.m_shared.m_skillType == Skills.SkillType.Axes)
                hit.m_damage = weapon.GetDamage();
            else
                hit.m_damage.m_slash = 60f;
            hit.ApplyModifier(0.6f * _thrower.GetRandomSkillFactor(Skills.SkillType.Axes));
            hit.m_point = target.GetCenterPoint();
            hit.m_dir = (target.GetCenterPoint() - transform.position).normalized;
            hit.m_pushForce = 30f;
            hit.m_staggerMultiplier = 1.5f;
            hit.m_ranged = true;
            hit.m_blockable = true;
            hit.m_dodgeable = true;
            hit.m_skill = Skills.SkillType.Axes;
            hit.SetAttacker(_thrower);
            return hit;
        }

        private void OnDestroy()
        {
            Active.Remove(this);
            if (_owner && _thrower != null)
                Brain.Caught(_hand, transform.position, _caught);
        }
    }
}
