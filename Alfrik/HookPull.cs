using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Alfrik
{
    // Bearded Hook's pull, the way the game's harpoon pulls (SE_Harpooned): a status effect the hit puts on whoever it
    // catches, running on that character's own game, drawing them in smoothly with Utils.Pull every frame - no shove
    // from his side of the network, so no rubber-banding. Unlike the harpoon (which holds its catch at the distance it
    // was hooked) the line shortens: from where they stood to about 2 m in front of him over about half a second, then
    // it lets go. It also lets go if they somehow end up far beyond the line.
    internal class SE_AlfrikHook : StatusEffect
    {
        internal const string Name = "Alfrik_Hook";
        internal static readonly int Hash = Name.GetStableHashCode();
        private const float StopAt = 2f, ReelTime = 0.55f, Speed = 11f, Smooth = 1.2f, Break = 9f;

        private Character _holder;
        private float _from, _held;
        private bool _done;

        internal static void Register()
        {
            var effect = CreateInstance<SE_AlfrikHook>();
            effect.name = Name;
            effect.m_name = "Hooked";
            effect.m_ttl = 1.1f;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false));
        }

        public override void SetAttacker(Character attacker)
        {
            _holder = attacker;
            _held = 0f;
            _done = attacker == null;
            if (!_done)
            {
                _from = Vector3.Distance(attacker.transform.position, m_character.transform.position);
                _lastDistance = _from;
                Plugin.Log.LogInfo("Hooked by " + attacker.GetHoverName() + " from " + _from.ToString("0.0") + " m");
            }
        }

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);
            if (_done || _holder == null || m_character == null)
                return;
            Rigidbody body = m_character.GetComponent<Rigidbody>();
            if (body == null)
                return;
            _held += dt;
            float distance = Vector3.Distance(_holder.transform.position, m_character.transform.position);
            if (distance - _from > Break)
            {
                _done = true;
                return;
            }
            float line = Mathf.Lerp(_from, StopAt, Mathf.Clamp01(_held / ReelTime));
            if (m_character.GetStandingOnShip() == null && !m_character.IsAttached())
            {
                Utils.Pull(body, _holder.transform.position, line, Speed, 1f, Smooth, true, false, 1f);
                // Their own movement can win against the pull (it sets their speed every physics step): then the chain
                // drags them along the line itself, on their own game all the same.
                if (_held > 0.12f && distance > line + 0.3f && distance > _lastDistance - 0.05f)
                {
                    Vector3 toward = _holder.transform.position - body.position;
                    toward.y = 0f;
                    if (toward.sqrMagnitude > 0.0001f)
                        body.MovePosition(body.position + toward.normalized * Mathf.Min(Speed * dt, distance - line));
                }
            }
            _lastDistance = distance;
            if (_held >= ReelTime && distance <= StopAt + 0.4f)
                _done = true;
            if (_done || _held >= ReelTime + 0.5f)
                Report(distance);
        }

        private float _lastDistance;
        private bool _reported;

        private void Report(float distance)
        {
            if (_reported)
                return;
            _reported = true;
            Plugin.Log.LogInfo("Hook let go: pulled from " + _from.ToString("0.0") + " m to " + distance.ToString("0.0") + " m");
        }

        public override bool IsDone() => base.IsDone() || _done || _holder == null;
    }
}
