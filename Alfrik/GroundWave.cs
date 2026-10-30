using System.Collections.Generic;
using UnityEngine;

namespace Alfrik
{
    // A shockwave racing along the ground in a straight line from his weapon, hugging the terrain: Earthsplitter's
    // rupture, 26 m long and 7 m wide (three rows of eruptions). (FURY's old 20 m wave is no longer sent.) Every game draws it from one routed call; only the game that owns him deals the damage, once to each
    // enemy it passes on the ground (it cannot be blocked; jumped, it passes under).
    internal static class GroundWave
    {
        private const string Rpc = "Alfrik_Wave", SplitRpc = "Alfrik_Split";
        // Earthsplitter's rupture: longer and faster than FURY's wave, sent at whoever stands far from him.
        internal const float SplitLength = 26f, SplitSpeed = 22f, SplitRadius = 3.5f;

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZDOID, Vector3, Vector3>(Rpc, OnWave);
            ZRoutedRpc.instance?.Register<ZDOID, Vector3, Vector3>(SplitRpc, OnSplit);
        }

        // Off the ground (jumping) as a wave along the ground reaches them: it passes under. Their own game knows it exactly
        // (IsOnGround); for a character run by another game, its synced height above the ground is used.
        internal static bool Airborne(Character character)
        {
            ZNetView view = character.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner())
                return !character.IsOnGround();
            Vector3 at = character.transform.position;
            return ZoneSystem.instance != null && at.y - ZoneSystem.instance.GetSolidHeight(at) > 0.5f;
        }

        internal static void Send(Humanoid c, Vector3 dir) => Send(c, dir, Rpc);

        internal static void SendSplit(Humanoid c, Vector3 dir) => Send(c, dir, SplitRpc);

        private static void Send(Humanoid c, Vector3 dir, string rpc)
        {
            ZNetView view = c == null ? null : c.GetComponent<ZNetView>();
            dir.y = 0f;
            if (view == null || !view.IsValid() || ZRoutedRpc.instance == null || dir.sqrMagnitude < 0.0001f)
                return;
            dir.Normalize();
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, rpc, view.GetZDO().m_uid, c.transform.position + dir * 1.2f, dir);
        }

        private static void OnWave(long sender, ZDOID id, Vector3 from, Vector3 dir) => Run(id, from, dir, 20f, 16f, 1.4f, "FURY wave");

        private static void OnSplit(long sender, ZDOID id, Vector3 from, Vector3 dir) => Run(id, from, dir, SplitLength, SplitSpeed, SplitRadius, "Earthsplitter");

        private static void Run(ZDOID id, Vector3 from, Vector3 dir, float length, float speed, float radius, string name)
        {
            GameObject instance = ZNetScene.instance == null ? null : ZNetScene.instance.FindInstance(id);
            Humanoid thrower = instance == null ? null : instance.GetComponent<Humanoid>();
            if (thrower == null)
                return;
            new GameObject("Alfrik_Wave").AddComponent<GroundWaveRun>().Begin(thrower, from, dir, length, speed, radius, name);
            Sounds.PlayAt("p2boom", from, 1f, 80f);
            Sounds.PlayAt("p2rumble", from, 0.9f, 60f);
            GameCamera.instance?.AddShake(from, 30f, 1.5f, false);
        }
    }

    internal class GroundWaveRun : MonoBehaviour
    {
        private const float Step = 0.07f;

        private Humanoid _thrower;
        private bool _owner;
        private Vector3 _dir, _at;
        private float _travelled, _nextStep, _length, _speed, _radius;
        private string _name;
        private readonly HashSet<Character> _hit = new HashSet<Character>(), _jumped = new HashSet<Character>();

        internal void Begin(Humanoid thrower, Vector3 from, Vector3 dir, float length, float speed, float radius, string name)
        {
            _name = name;
            _radius = radius;
            _thrower = thrower;
            _dir = dir;
            _at = from;
            _length = length;
            _speed = speed;
            ZNetView view = thrower.GetComponent<ZNetView>();
            _owner = view != null && view.IsValid() && view.IsOwner();
            Destroy(gameObject, length / speed + 1f);
        }

        private void Update()
        {
            if (_thrower == null || _travelled >= _length)
            {
                // The end of the run, on the game that owns him: how many it caught.
                if (_owner && _thrower != null)
                    Plugin.Log.LogInfo("Alfrik's " + _name + " catches " + _hit.Count + " (" + QuakeRun.JumpedOnly(_jumped, _hit) + " jumped it)");
                Destroy(gameObject);
                return;
            }
            float move = _speed * Time.deltaTime;
            _travelled += move;
            _at += _dir * move;
            if (ZoneSystem.instance != null)
                _at.y = ZoneSystem.instance.GetSolidHeight(_at);
            transform.position = _at;
            if (Time.time >= _nextStep)
            {
                _nextStep = Time.time + Step;
                if (_radius > 2f)
                {
                    // A wide rupture: a big row of eruptions down the middle and one along each side.
                    Fx.WaveStep(_at, 1.4f);
                    Vector3 side = Vector3.Cross(Vector3.up, _dir) * (_radius * 0.65f);
                    foreach (Vector3 edge in new[] { _at + side, _at - side })
                    {
                        Vector3 p = edge;
                        if (ZoneSystem.instance != null)
                            p.y = ZoneSystem.instance.GetSolidHeight(p);
                        Fx.WaveStep(p);
                    }
                }
                else
                    Fx.WaveStep(_at);
            }
            if (_owner)
                Strike();
        }

        private void Strike()
        {
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character == _thrower || character.IsDead() || _hit.Contains(character) ||
                    !BaseAI.IsEnemy(_thrower, character))
                    continue;
                Vector3 to = character.transform.position - _at;
                to.y = 0f;
                if (to.magnitude > _radius + character.GetRadius() || Mathf.Abs(character.transform.position.y - _at.y) > 2.5f)
                    continue;
                // In the air as it passes under them: not caught (they may still land in it).
                if (GroundWave.Airborne(character))
                {
                    _jumped.Add(character);
                    continue;
                }
                _hit.Add(character);
                ItemDrop.ItemData weapon = _thrower.GetCurrentWeapon();
                var hit = new HitData();
                if (weapon != null)
                    hit.m_damage = weapon.GetDamage();
                else
                    hit.m_damage.m_blunt = 80f;
                hit.ApplyModifier(0.9f * _thrower.GetRandomSkillFactor(Skills.SkillType.Axes));
                hit.m_point = character.GetCenterPoint();
                hit.m_dir = _dir;
                hit.m_pushForce = 90f;
                hit.m_staggerMultiplier = 2f;
                hit.m_blockable = false;
                hit.m_dodgeable = true;
                hit.m_skill = Skills.SkillType.Axes;
                hit.SetAttacker(_thrower);
                character.Damage(hit);
                Fx.SendAt(Fx.Hit, character.GetCenterPoint());
            }
        }
    }

    // Rings bursting outward from a point, one step after another: Grave Ring (around him, as his hammer strikes), War
    // Stomp (from his foot) and the landing of Odin's Fall. Each step erupts in a circle further out and catches whoever is on
    // the ground in that band; in the air (jumping) the ring passes under them. Every game draws it from one routed call;
    // only the game that owns him deals the damage, once to each enemy per move (it cannot be blocked).
    internal static class Quake
    {
        internal const int GraveRing = 0, Stomp = 1, FallLanding = 2, ChargeImpact = 3;
        private const string Rpc = "Alfrik_Quake";

        internal sealed class Kind
        {
            internal string Name;
            internal float First, Step, Interval, Band, Share, Push, Stagger;
            internal int Steps;
        }

        // First ring, the step out to each next one, how many, the time between them, half the band's width; the blow
        // (Fjallbrjótr's damage share, throw-back, stagger).
        internal static readonly Kind[] Kinds =
        {
            new Kind { Name = "Grave Ring", First = 2f, Step = 2f, Steps = 4, Interval = 0.3f, Band = 1.1f, Share = 0.9f, Push = 60f, Stagger = 2f },
            new Kind { Name = "War Stomp", First = 1.5f, Step = 1.5f, Steps = 3, Interval = 0.2f, Band = 0.85f, Share = 0.6f, Push = 110f, Stagger = 2.5f },
            new Kind { Name = "Odin's Fall landing", First = 1.5f, Step = 1.5f, Steps = 4, Interval = 0.2f, Band = 0.85f, Share = 1.1f, Push = 90f, Stagger = 2f },
            new Kind { Name = "Odin's Charge impact", First = 1.5f, Step = 1.5f, Steps = 3, Interval = 0.15f, Band = 0.85f, Share = 0.8f, Push = 120f, Stagger = 3f },
        };

        internal static void Register() => ZRoutedRpc.instance?.Register<ZDOID, Vector3, int>(Rpc, OnQuake);

        internal static void Send(Humanoid c, Vector3 at, int kind)
        {
            ZNetView view = c == null ? null : c.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || ZRoutedRpc.instance == null)
                return;
            if (ZoneSystem.instance != null)
                at.y = ZoneSystem.instance.GetSolidHeight(at);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, view.GetZDO().m_uid, at, kind);
        }

        private static void OnQuake(long sender, ZDOID id, Vector3 at, int kind)
        {
            GameObject instance = ZNetScene.instance == null ? null : ZNetScene.instance.FindInstance(id);
            Humanoid thrower = instance == null ? null : instance.GetComponent<Humanoid>();
            if (thrower == null || kind < 0 || kind >= Kinds.Length)
                return;
            new GameObject("Alfrik_Quake").AddComponent<QuakeRun>().Begin(thrower, at, Kinds[kind]);
        }
    }

    internal class QuakeRun : MonoBehaviour
    {
        private Humanoid _thrower;
        private bool _owner;
        private Vector3 _at;
        private Quake.Kind _kind;
        private int _step;
        private float _next;
        private readonly HashSet<Character> _hit = new HashSet<Character>(), _jumped = new HashSet<Character>();

        internal void Begin(Humanoid thrower, Vector3 at, Quake.Kind kind)
        {
            _thrower = thrower;
            _at = at;
            _kind = kind;
            _next = Time.time;
            ZNetView view = thrower.GetComponent<ZNetView>();
            _owner = view != null && view.IsValid() && view.IsOwner();
            Destroy(gameObject, kind.Steps * kind.Interval + 2f);
        }

        // Those who jumped a ring of it and were never caught.
        internal static int JumpedOnly(HashSet<Character> jumped, HashSet<Character> hit)
        {
            int count = 0;
            foreach (Character character in jumped)
                if (!hit.Contains(character))
                    count++;
            return count;
        }

        private void Update()
        {
            if (_thrower == null)
            {
                Destroy(gameObject);
                return;
            }
            while (_step < _kind.Steps && Time.time >= _next)
            {
                float radius = _kind.First + _kind.Step * _step;
                Fx.QuakeStep(_at, radius, _step == 0);
                if (_owner)
                    Strike(radius, _step == 0);
                _step++;
                _next += _kind.Interval;
            }
            if (_step < _kind.Steps)
                return;
            if (_owner)
                Plugin.Log.LogInfo("Alfrik's " + _kind.Name + " catches " + _hit.Count + " (" + JumpedOnly(_jumped, _hit) + " jumped it)");
            Destroy(gameObject);
        }

        // The ring at this radius: whoever stands in its band (the first ring reaches in to the middle), on the ground.
        private void Strike(float radius, bool first)
        {
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character == _thrower || character.IsDead() || _hit.Contains(character) ||
                    !BaseAI.IsEnemy(_thrower, character))
                    continue;
                Vector3 to = character.transform.position - _at;
                float height = Mathf.Abs(to.y);
                to.y = 0f;
                float distance = to.magnitude;
                if (height > 2.5f || distance > radius + _kind.Band + character.GetRadius() || (!first && distance < radius - _kind.Band))
                    continue;
                if (GroundWave.Airborne(character))
                {
                    _jumped.Add(character);
                    continue;
                }
                _hit.Add(character);
                Brain.Blast(_thrower, character, to, _kind.Share, _kind.Push, _kind.Stagger);
            }
        }
    }
}
