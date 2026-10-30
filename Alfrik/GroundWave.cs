using System.Collections.Generic;
using UnityEngine;

namespace Alfrik
{
    // A shockwave racing along the ground in a straight line from his weapon, hugging the terrain: Earthsplitter's
    // rupture, 26 m long and 7 m wide (three rows of eruptions). (FURY's old 20 m wave is no longer sent.) Every game draws it from one routed call; only the game that owns him deals the damage, once to each
    // enemy it passes (it cannot be blocked).
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
        private readonly HashSet<Character> _hit = new HashSet<Character>();

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
                    Plugin.Log.LogInfo("Alfrik's " + _name + " catches " + _hit.Count);
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
}
