using UnityEngine;

namespace Alfrik
{
    // Iron Wall, Returned: a shot (arrow, bolt, spear, spell) caught on the front of his Iron Wall is flung straight back
    // at whoever loosed it, as a gold-hot streak. Every game draws it from one routed call; only the game that owns him
    // deals its damage (as much as the shot carried, as piercing), to the first enemy it reaches. It flies in a straight
    // line toward where the shooter stood, and breaks on the ground.
    internal static class ReturnedShot
    {
        private const string Rpc = "Alfrik_Return";

        internal static void Register() => ZRoutedRpc.instance?.Register<ZDOID, Vector3, Vector3, float>(Rpc, OnReturn);

        // Owner: from where the shot struck the wall, toward the shooter's chest.
        internal static void Send(Humanoid c, Vector3 from, Character shooter, float damage)
        {
            ZNetView view = c == null ? null : c.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || ZRoutedRpc.instance == null || shooter == null)
                return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, view.GetZDO().m_uid, from, shooter.GetCenterPoint(), damage);
        }

        private static void OnReturn(long sender, ZDOID id, Vector3 from, Vector3 to, float damage)
        {
            GameObject instance = ZNetScene.instance == null ? null : ZNetScene.instance.FindInstance(id);
            Humanoid thrower = instance == null ? null : instance.GetComponent<Humanoid>();
            if (thrower == null)
                return;
            new GameObject("Alfrik_Returned").AddComponent<ReturnedShotRun>().Begin(thrower, from, to, damage);
        }
    }

    internal class ReturnedShotRun : MonoBehaviour
    {
        private const float Speed = 32f, MaxRange = 45f, Radius = 0.9f, Spark = 0.04f;

        private Humanoid _thrower;
        private bool _owner;
        private Vector3 _dir;
        private float _damage, _travelled, _nextSpark;

        internal void Begin(Humanoid thrower, Vector3 from, Vector3 to, float damage)
        {
            _thrower = thrower;
            _damage = damage;
            Vector3 line = to - from;
            _dir = line.sqrMagnitude > 0.0001f ? line.normalized : thrower.transform.forward;
            transform.position = from + _dir * 0.5f;
            ZNetView view = thrower.GetComponent<ZNetView>();
            _owner = view != null && view.IsValid() && view.IsOwner();
            float life = MaxRange / Speed;
            Fx.ShotGlow(transform, life);
            Destroy(gameObject, life + 0.5f);
        }

        private void Update()
        {
            if (_thrower == null || _travelled >= MaxRange)
            {
                Destroy(gameObject);
                return;
            }
            float move = Speed * Time.deltaTime;
            _travelled += move;
            Vector3 at = transform.position + _dir * move;
            transform.position = at;
            if (Time.time >= _nextSpark)
            {
                _nextSpark = Time.time + Spark;
                Fx.ShotSpark(at);
            }
            if (ZoneSystem.instance != null && at.y < ZoneSystem.instance.GetSolidHeight(at) - 0.1f)
            {
                Fx.WaveStepSmall(at);
                Destroy(gameObject);
                return;
            }
            if (_owner && Strike(at))
                Destroy(gameObject);
        }

        private bool Strike(Vector3 at)
        {
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character == _thrower || character.IsDead() || !BaseAI.IsEnemy(_thrower, character))
                    continue;
                if (Vector3.Distance(character.GetCenterPoint(), at) > Radius + character.GetRadius())
                    continue;
                var hit = new HitData();
                hit.m_damage.m_pierce = _damage;
                hit.m_point = at;
                hit.m_dir = _dir;
                hit.m_pushForce = 30f;
                hit.m_staggerMultiplier = 1f;
                hit.m_blockable = false;
                hit.m_dodgeable = true;
                hit.m_ranged = true;
                hit.m_skill = Skills.SkillType.Axes;
                hit.SetAttacker(_thrower);
                character.Damage(hit);
                Fx.SendAt(Fx.Hit, at);
                return true;
            }
            return false;
        }
    }
}
