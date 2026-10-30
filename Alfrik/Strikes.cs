using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Alfrik
{
    // Blows that fall a moment after they are marked on the ground, seen by everyone from one routed call; only the game
    // that owns him deals the damage (never blocked).
    //  Grave Ring  - red runes in a ring 3-7 m around where he struck, then the ground erupts along it (1.2 s).
    //  Lightning   - Odin's Wrath: pale runes where the bolt will fall, then lightning, 2.2 m around (0.9 s).
    internal static class Strikes
    {
        internal const int GraveRing = 0, Lightning = 1;
        internal const float RingInner = 2.5f, RingOuter = 7.5f, BoltRadius = 2.2f;
        private const string Rpc = "Alfrik_Strike";

        internal static void Register() => ZRoutedRpc.instance?.Register<ZDOID, Vector3, int>(Rpc, OnStrike);

        internal static void Send(Humanoid c, Vector3 at, int kind)
        {
            ZNetView view = c == null ? null : c.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || ZRoutedRpc.instance == null)
                return;
            if (ZoneSystem.instance != null)
                at.y = ZoneSystem.instance.GetSolidHeight(at);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, view.GetZDO().m_uid, at, kind);
        }

        private static void OnStrike(long sender, ZDOID id, Vector3 at, int kind)
        {
            GameObject instance = ZNetScene.instance == null ? null : ZNetScene.instance.FindInstance(id);
            Humanoid thrower = instance == null ? null : instance.GetComponent<Humanoid>();
            if (thrower == null)
                return;
            new GameObject("Alfrik_Strike").AddComponent<StrikeRun>().Begin(thrower, at, kind);
        }
    }

    internal class StrikeRun : MonoBehaviour
    {
        private Humanoid _thrower;
        private bool _owner;
        private Vector3 _at;
        private int _kind;
        private float _fallAt;

        internal void Begin(Humanoid thrower, Vector3 at, int kind)
        {
            _thrower = thrower;
            _at = at;
            _kind = kind;
            float delay = kind == Strikes.GraveRing ? 1.2f : 0.9f;
            _fallAt = Time.time + delay;
            ZNetView view = thrower.GetComponent<ZNetView>();
            _owner = view != null && view.IsValid() && view.IsOwner();
            Fx.StrikeMark(at, kind, delay);
            Destroy(gameObject, delay + 2f);
        }

        private void Update()
        {
            if (_thrower == null)
            {
                Destroy(gameObject);
                return;
            }
            if (Time.time < _fallAt)
                return;
            Fx.StrikeBurst(_at, _kind);
            if (_owner)
                Strike();
            Destroy(gameObject);
        }

        private void Strike()
        {
            int hit = 0;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || character == _thrower || character.IsDead() || !BaseAI.IsEnemy(_thrower, character))
                    continue;
                Vector3 to = character.transform.position - _at;
                float height = Mathf.Abs(to.y);
                to.y = 0f;
                float distance = to.magnitude;
                bool caught = _kind == Strikes.GraveRing
                    ? distance >= Strikes.RingInner && distance <= Strikes.RingOuter && height <= 2.5f
                    : distance <= Strikes.BoltRadius + character.GetRadius() && height <= 3f;
                if (!caught)
                    continue;
                ItemDrop.ItemData weapon = _thrower.GetCurrentWeapon();
                var blow = new HitData();
                if (_kind == Strikes.GraveRing)
                {
                    if (weapon != null)
                        blow.m_damage = weapon.GetDamage();
                    else
                        blow.m_damage.m_blunt = 80f;
                    blow.ApplyModifier(0.9f * _thrower.GetRandomSkillFactor(Skills.SkillType.Axes));
                    blow.m_pushForce = 60f;
                    blow.m_dir = distance > 0.01f ? to / distance : Vector3.up;
                }
                else
                {
                    blow.m_damage.m_lightning = (weapon != null ? weapon.GetDamage().GetTotalDamage() : 100f) * 0.7f *
                        _thrower.GetRandomSkillFactor(Skills.SkillType.Axes);
                    blow.m_pushForce = 20f;
                    blow.m_dir = Vector3.down;
                }
                blow.m_point = character.GetCenterPoint();
                blow.m_staggerMultiplier = 2f;
                blow.m_blockable = false;
                blow.m_dodgeable = true;
                blow.m_skill = Skills.SkillType.Axes;
                blow.SetAttacker(_thrower);
                character.Damage(blow);
                Fx.SendAt(Fx.Hit, character.GetCenterPoint());
                hit++;
            }
            if (hit > 0)
                Plugin.Log.LogInfo((_kind == Strikes.GraveRing ? "Alfrik's Grave Ring catches " : "Odin's Wrath strikes ") + hit);
        }
    }

    // War Horn's slow: the game's own timed status effect, carried by the horn's hit (so it runs on the game of whoever
    // is slowed): 35% slower for 4 s, with an icon of the horn's call.
    internal static class Slow
    {
        internal const string Name = "Alfrik_Horn";
        private static readonly int Hash = Name.GetStableHashCode();

        internal static void Register()
        {
            var effect = ScriptableObject.CreateInstance<SE_Stats>();
            effect.name = Name;
            effect.m_name = "War Horn";
            effect.m_tooltip = "Alfrik's war horn: your legs are heavy.";
            effect.m_icon = Icon();
            effect.m_ttl = 4f;
            effect.m_speedModifier = -0.35f;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false));
        }

        // Owner: a blow of no real weight that carries the slow.
        internal static void Apply(Humanoid c, Character enemy)
        {
            var hit = new HitData();
            hit.m_damage.m_blunt = 1f;
            hit.m_point = enemy.GetCenterPoint();
            hit.m_dir = (enemy.transform.position - c.transform.position).normalized;
            hit.m_pushForce = 0f;
            hit.m_staggerMultiplier = 0f;
            hit.m_blockable = false;
            hit.m_dodgeable = false;
            hit.m_statusEffectHash = Hash;
            hit.SetAttacker(c);
            enemy.Damage(hit);
        }

        // Three red arcs, a call spreading out, drawn here.
        private static Sprite Icon()
        {
            const int size = 64;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt((u + 0.75f) * (u + 0.75f) + v * v);
                    float angle = Mathf.Abs(Mathf.Atan2(v, u + 0.75f));
                    float a = 0f;
                    if (angle < 0.75f)
                        for (int arc = 1; arc <= 3; arc++)
                            a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - arc * 0.45f) / 0.09f));
                    a = Mathf.Max(a, Mathf.Clamp01(1f - r / 0.22f) * 2f);
                    pixels[y * size + x] = new Color(0.85f, 0.15f, 0.08f, Mathf.Clamp01(a));
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Alfrik_horn" };
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
