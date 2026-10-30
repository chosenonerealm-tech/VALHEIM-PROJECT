using UnityEngine;

namespace Alfrik
{
    // His moments, seen and heard by everyone: the game that owns him sends one routed call, every game plays the sound
    // (Sounds, Audio/) and builds the effect here from particle textures (Textures, Kenney Particle Pack) and lights.
    internal static class Fx
    {
        internal const int Rune = 1, Parry = 2, Roar = 3, Dodge = 4, Slash = 5, Hit = 6, Yield = 7, Charge = 8,
            Ravens = 9, Punch = 10, Hook = 11, Catch = 12, Cleave = 13, WarCry = 14, Shoulder = 15, Bloodscent = 16,
            Descend = 17, Land = 18, Swap = 19, WallUp = 20, Block = 21, GuardBreak = 22, Glint = 23, Sweep = 24, BeardHook = 25,
            ExecFlash = 26, Fury = 27, FurySlash = 28, Swing2 = 29, Hit2 = 30, Stunned = 31, Leap = 32, FallLand = 33,
            StompHit = 34, Returned = 35, IronSkin = 36, Horn = 37, StoneBreak = 38;
        private static readonly Color Storm = new Color(0.75f, 0.85f, 1f, 1f);
        private const string Rpc = "Alfrik_Fx";

        private static readonly Color Red = new Color(1f, 0.14f, 0.05f, 1f);
        private static readonly Color Ember = new Color(1f, 0.45f, 0.1f, 1f);
        private static readonly Color Gold = new Color(1f, 0.85f, 0.45f, 1f);
        private static readonly Color Steel = new Color(0.85f, 0.9f, 1f, 1f);
        private static readonly Color Dust = new Color(0.55f, 0.5f, 0.42f, 0.6f);
        private static readonly Color Smoke = new Color(0.35f, 0.33f, 0.32f, 0.55f);
        private static readonly Color Raven = new Color(0.25f, 0.3f, 0.6f, 1f);

        internal static void Register() => ZRoutedRpc.instance?.Register<ZDOID, int, Vector3>(Rpc, OnFx);

        internal static void Send(int kind) => SendAt(kind, Vector3.zero);

        // At a point (an axe or a fist striking); Vector3.zero means on him.
        internal static void SendAt(int kind, Vector3 point)
        {
            ZNetView view = AlfrikBoss.View;
            if (view != null && view.IsValid() && ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, view.GetZDO().m_uid, kind, point);
        }

        private static void OnFx(long sender, ZDOID id, int kind, Vector3 point)
        {
            GameObject instance = ZNetScene.instance == null ? null : ZNetScene.instance.FindInstance(id);
            Character body = instance == null ? null : instance.GetComponent<Character>();
            if (body == null)
                return;
            Vector3 centre = body.GetCenterPoint(), feet = body.transform.position, forward = body.transform.forward;
            Vector3 at = point == Vector3.zero ? centre + forward * 0.6f : point;
            switch (kind)
            {
                // ---- Phase 1, The Berserker ----
                case Rune:
                    // A warning: fire climbs a ring around his feet, red runes flare.
                    Sounds.PlayAt("growl", centre);
                    Sounds.PlayAt("rune", centre, 0.8f);
                    Anim(feet + Vector3.up * 0.4f, body.transform, "p1_firecrown", new Color(1f, 1f, 1f, 0.55f), 1.6f, 0.8f);
                    Ring(feet + Vector3.up * 0.2f, Textures.Runes, new Color(1f, 0.25f, 0.1f, 1f), 1.35f, 10, 0f, 1.1f, 0.75f);
                    Flash(centre, body.transform, Red, 4f, 5f);
                    break;
                case Parry:
                    Sounds.PlayAt("parry", at);
                    Burst(at, null, "p1_spark", Gold, 18, 3f, 8f, 0.3f, 0.3f, 0.2f, 1f);
                    Burst(at, null, "p1_star", Steel, 3, 0.2f, 0.6f, 0.9f, 0.25f, 0.05f, 0f);
                    Flash(at, null, Gold, 5f, 6f);
                    break;
                case Roar:
                case WarCry:
                    // A ring of fire and a wall of smoke burst out from him.
                    Sounds.PlayAt("roar", centre, 1f, 70f);
                    Anim(feet + Vector3.up * 0.3f, null, "p1_firering", Color.white, 7f, 1.1f);
                    Anim(feet + Vector3.up * 0.2f, null, "p1_smokeblast", Smoke, 6f, 1.3f);
                    Burst(centre, null, "p1_fire", Ember, 10, 0.5f, 2.5f, 0.45f, 1f, 0.6f, -0.25f);
                    Flash(centre, body.transform, Red, 7f, 8f);
                    break;
                case Dodge:
                    Sounds.PlayAt("dodge", feet, 0.9f, 30f);
                    Burst(feet + Vector3.up * 0.15f, null, "p1_dust", Dust, 8, 0.8f, 2f, 0.7f, 0.8f, 0.4f, 0.4f, grow: true);
                    break;
                case Slash:
                    // The red slash is the trail on his axes (HeldAxes); this is its sound.
                    Sounds.PlayAt("swing", centre);
                    break;
                case Hit:
                    Sounds.PlayAt("hit", at, 1f, 35f);
                    Burst(at, null, "p1_spark", Ember, 12, 3f, 7f, 0.25f, 0.25f, 0.15f, 1f);
                    Flash(at, null, Ember, 3f, 4f);
                    break;
                case Yield:
                    Sounds.PlayAt("yield", centre);
                    Burst(feet + Vector3.up * 0.1f, null, "p1_puff", Dust, 10, 0.6f, 1.6f, 0.8f, 1.2f, 0.6f, -0.03f, grow: true);
                    break;
                case Charge:
                    Sounds.PlayAt("growl", centre, 1f, 45f);
                    Sounds.PlayAt("shoulder_whoosh", feet, 0.8f, 30f);
                    Trail(body.transform, "p1_dust", 1.6f);
                    break;
                case Ravens:
                    // Raven-blue smoke rolls off him as both axes leave his hands.
                    Sounds.PlayAt("raven", centre, 1f, 60f);
                    Sounds.PlayAt("rune", centre, 0.7f);
                    Anim(centre, body.transform, "p1_ravenring", Color.white, 3.2f, 0.9f);
                    Burst(centre, body.transform, "p1_flare", Raven, 6, 0.5f, 1.5f, 0.5f, 0.5f, 0.3f, -0.2f);
                    Flash(centre, body.transform, Raven, 5f, 5f);
                    break;
                case Punch:
                    Sounds.PlayAt("punch", at, 1f, 30f);
                    Burst(at, null, "p1_star", Gold, 2, 0.2f, 0.6f, 0.6f, 0.2f, 0.05f, 0f);
                    break;
                case Hook:
                    Sounds.PlayAt("hook", at, 1f, 40f);
                    Burst(at, null, "p1_star", Red, 3, 0.3f, 1f, 0.9f, 0.3f, 0.1f, 0f);
                    Burst(at, null, "p1_spark", Ember, 10, 3f, 6f, 0.25f, 0.25f, 0.1f, 1f);
                    Flash(at, null, Red, 3f, 5f);
                    break;
                case Catch:
                    Sounds.PlayAt("catch", at, 1f, 30f);
                    Burst(at, null, "p1_spark", Steel, 8, 1.5f, 4f, 0.2f, 0.25f, 0.1f, 0.5f);
                    break;
                case Cleave:
                    Sounds.PlayAt("cleave", centre);
                    Sounds.PlayAt("swing", centre, 0.8f);
                    break;
                case Shoulder:
                    Sounds.PlayAt("shoulder", at, 1f, 40f);
                    Burst(at, null, "p1_dust", Dust, 10, 1.5f, 4f, 0.7f, 0.6f, 0.3f, 0.3f, grow: true);
                    Burst(at, null, "p1_smoke", Smoke, 6, 0.5f, 1.5f, 0.9f, 0.8f, 0.3f, -0.05f, grow: true);
                    Flash(at, null, Ember, 3f, 3f);
                    break;
                case Bloodscent:
                    // The blood rage: a burning ring round his chest and fire off his body.
                    Sounds.PlayAt("growl", centre, 1f, 50f);
                    Anim(centre, body.transform, "p1_bloodring", Color.white, 2.4f, 1.2f);
                    Burst(centre, body.transform, "p1_fire", Red, 14, 0.8f, 2.5f, 0.5f, 1f, 0.5f, -0.3f);
                    Flash(centre, body.transform, Red, 6f, 7f);
                    break;
                case Stunned:
                    // Staggered: a ring of dazed sparks over his head.
                    Sounds.PlayAt("hit", centre, 0.8f, 35f);
                    Burst(body.GetHeadPoint() + Vector3.up * 0.3f, body.transform, "p1_star", Gold, 6, 0.4f, 1.2f, 0.35f, 0.9f, 0.25f, -0.1f);
                    Burst(centre, null, "p1_spark", Gold, 14, 2f, 5f, 0.25f, 0.3f, 0.2f, 1f);
                    Flash(centre, body.transform, Gold, 4f, 5f);
                    break;
                // ---- Phase 2, The Unbroken: its own sounds (p2*) and effects (p2_* sheets) ----
                case Swap:
                    // Hugr & Hamr gone in a burst of fire and runes; Skeggöld in his hands.
                    Sounds.PlayAt("p2roar", centre, 1f, 90f);
                    Sounds.PlayAt("p2boom", centre, 0.8f, 70f);
                    Anim(centre, body.transform, "p2_explosion", Color.white, 3.2f, 0.9f);
                    Burst(centre, body.transform, Textures.Runes, Red, 18, 1f, 3f, 0.4f, 0.9f, 0.4f, -0.2f);
                    Flash(centre, body.transform, Red, 10f, 9f);
                    GameCamera.instance?.AddShake(feet, 30f, 1.5f, false);
                    break;
                case WallUp:
                    Sounds.PlayAt("p2clash", centre, 0.6f, 35f);
                    Burst(centre + forward * 0.5f, body.transform, "p1_spark", Steel, 8, 0.5f, 1.5f, 0.15f, 0.25f, 0.2f, 0f);
                    break;
                case Block:
                    Sounds.PlayAt("p2clash", at, 1f, 40f);
                    Burst(at, null, "p1_spark", Steel, 22, 2f, 7f, 0.2f, 0.25f, 0.15f, 1f);
                    Flash(at, null, Steel, 3f, 4f);
                    break;
                case GuardBreak:
                    // A guard smashed: steel on steel, then the blow.
                    Sounds.PlayAt("p2clash", at, 1f, 50f);
                    Sounds.PlayAt("p2hit", at, 0.9f, 50f);
                    Anim(at, null, "p2_fireball", Gold, 1.6f, 0.45f);
                    Burst(at, null, "p1_spark", Gold, 30, 3f, 9f, 0.25f, 0.35f, 0.2f, 1f);
                    Flash(at, null, Gold, 6f, 7f);
                    break;
                case Glint:
                    // A gold glint running along the axe's edge, with the ring of steel.
                    Vector3 edge = (body is Humanoid holder ? Hatchet.HandPosition(holder) : centre) + forward * 0.6f + Vector3.up * 0.35f;
                    Sounds.PlayAt("p2glint", edge, 1f, 35f);
                    Sprite(edge, forward, "p1_star", Gold, 1.2f, 0.55f);
                    Flash(edge, body.transform, Gold, 3f, 5f);
                    break;
                case Sweep:
                    Sounds.PlayAt("p2swing", centre, 1f, 45f);
                    Sounds.PlayAt("p2growl", centre, 0.5f, 40f);
                    break;
                case BeardHook:
                    HookChain.Show(body as Humanoid, at);
                    Sounds.PlayAt("p2chain", at, 1f, 40f);
                    Sounds.PlayAt("p2hit", at, 0.7f, 40f);
                    Sprite(at, forward, "p2_blood", Color.white, 1.4f, 0.4f);
                    Burst(at, null, "p1_dust", Dust, 8, 1f, 3f, 0.5f, 0.6f, 0.3f, 0.4f, grow: true);
                    break;
                case ExecFlash:
                    Sounds.PlayAt("p2growl", centre, 1f, 50f);
                    Flash(centre + Vector3.up * 1.4f, body.transform, Red, 9f, 9f);
                    Anim(centre + Vector3.up * 1.5f, body.transform, "p2_fireball", Color.white, 1.4f, 0.6f);
                    break;
                case Fury:
                    // Planted, roaring: runes flare around him, fire bursts off his body and the ground shakes.
                    Sounds.PlayAt("p2roar", centre, 1f, 90f);
                    Sounds.PlayAt("p2rumble", feet, 1f, 70f);
                    Ring(feet + Vector3.up * 0.05f, Textures.Runes, Red, 2.2f, 12, 0f, 1.4f, 0.6f);
                    Anim(feet + Vector3.up * 0.3f, null, "p1_firering", Color.white, 6f, 1f);
                    Anim(centre, body.transform, "p2_explosion", Color.white, 2.6f, 1f);
                    Burst(centre, body.transform, "p1_fire", Red, 24, 0.8f, 3f, 0.35f, 1.1f, 0.6f, -0.4f);
                    Flash(centre, body.transform, Red, 10f, 9f);
                    GameCamera.instance?.AddShake(feet, 25f, 1.2f, false);
                    break;
                case FurySlash:
                    Sounds.PlayAt("p2swing", centre, 1f, 50f);
                    Sounds.PlayAt("p2clash", centre, 0.5f, 50f);
                    Flash(centre + forward, null, Red, 5f, 5f);
                    break;
                case Swing2:
                    Sounds.PlayAt("p2swing", centre, 0.9f, 35f);
                    break;
                case Leap:
                    // Odin's Fall: the ground bursts under his feet and he trails dark smoke and embers up into the air.
                    Sounds.PlayAt("p2growl", centre, 1f, 60f);
                    Sounds.PlayAt("shoulder_whoosh", centre, 1f, 50f);
                    Burst(feet + Vector3.up * 0.2f, null, "p1_dust", Dust, 18, 2f, 6f, 0.7f, 0.9f, 0.5f, 0.8f, grow: true);
                    Trail(body.transform, "p1_puff", 1.2f, new Color(0.35f, 0.08f, 0.06f, 0.7f), 0.9f, 40f);
                    Trail(body.transform, "p1_fire", 1.2f, Ember, 0.3f, 30f);
                    break;
                case FallLand:
                    // He comes down like Odin's hammer: a blast, a wall of smoke and dust racing out, the ground shaking.
                    Sounds.PlayAt("p2boom", at, 1f, 90f);
                    Sounds.PlayAt("p2rumble", at, 1f, 70f);
                    Anim(at + Vector3.up * 0.5f, null, "p2_explosion", Color.white, 4.5f, 0.8f);
                    Anim(at + Vector3.up * 0.2f, null, "p1_smokeblast", Smoke, 10f, 1.2f);
                    Ring(at + Vector3.up * 0.2f, "p1_puff", Smoke, 0.8f, 30, 6f, 1f, 1.2f);
                    Burst(at + Vector3.up * 0.2f, null, "p1_dust", Dust, 24, 3f, 8f, 0.7f, 1f, 0.6f, 1.2f, grow: true);
                    Ring(at + Vector3.up * 0.05f, Textures.Runes, Red, 2.4f, 10, 0f, 1.3f, 0.6f);
                    Flash(at + Vector3.up, null, Red, 10f, 8f);
                    GameCamera.instance?.AddShake(at, 35f, 2f, false);
                    break;
                case StompHit:
                    // War Stomp: the foot comes down and the ground heaves outward around him.
                    Sounds.PlayAt("p2boom", feet, 0.9f, 70f);
                    Sounds.PlayAt("p2rumble", feet, 0.8f, 50f);
                    Anim(feet + Vector3.up * 0.3f, null, "p2_explosion", Color.white, 3.2f, 0.6f);
                    Ring(feet + Vector3.up * 0.15f, "p1_puff", Dust, 0.6f, 36, 6f, 0.8f, 1.4f);
                    Ring(feet + Vector3.up * 0.1f, "p1_firering", Color.white, 0f, 1, 0f, 0.8f, 1f, 9f);
                    Burst(feet + Vector3.up * 0.2f, null, "p1_dust", Dust, 20, 2f, 6f, 0.8f, 0.8f, 0.5f, 1f, grow: true);
                    Flash(feet + Vector3.up * 0.5f, null, Ember, 6f, 5f);
                    GameCamera.instance?.AddShake(feet, 20f, 1.2f, false);
                    break;
                case StoneBreak:
                    // The Challenge Stone, spent: it bursts apart in grit and dust, its runes scattering as red sparks.
                    Sounds.PlayAt("p2boom", at, 0.9f, 60f);
                    Sounds.PlayAt("rune", at, 1f, 50f);
                    Burst(at, null, "p1_dust", Dust, 30, 2f, 7f, 0.8f, 1.2f, 0.6f, 0.8f, grow: true);
                    Burst(at, null, "p1_smoke", Smoke, 14, 0.5f, 2f, 1.2f, 1.6f, 0.5f, -0.05f, grow: true);
                    Burst(at, null, Textures.Runes, Red, 10, 1f, 4f, 0.5f, 1.2f, 0.4f, 0.3f);
                    Burst(at, null, "p1_spark", Ember, 24, 3f, 8f, 0.25f, 0.5f, 0.4f, 1f);
                    Flash(at, null, Red, 8f, 6f);
                    break;
                case IronSkin:
                    // Retribution: his skin goes to dark iron - steel rings out, and iron sparks shed off him while it lasts.
                    Sounds.PlayAt("p2clash", centre, 1f, 50f);
                    Sounds.PlayAt("p2growl", centre, 0.7f, 50f);
                    Burst(centre, body.transform, "p1_spark", Steel, 40, 2f, 6f, 0.35f, 0.5f, 0.4f, 0.5f);
                    Ring(feet + Vector3.up * 0.1f, Textures.Runes, Steel, 1.4f, 12, 0f, 1.5f, 0.9f);
                    Trail(body.transform, "p1_spark", 5f, Steel, 0.25f, 40f);
                    Trail(body.transform, "p1_smoke", 5f, new Color(0.2f, 0.22f, 0.26f, 0.6f), 0.6f, 12f);
                    Flash(centre, body.transform, Steel, 6f, 7f);
                    break;
                case Horn:
                    // War Horn: a roar like a horn blast, a wall of air and smoke rolling out, the ground trembling.
                    Sounds.PlayAt("p2roar", centre, 1f, 100f);
                    Sounds.PlayAt("p2rumble", feet, 0.8f, 70f);
                    Ring(feet + Vector3.up * 0.3f, "p1_puff", Smoke, 0.8f, 48, 12f, 1.2f, 1.8f);
                    Anim(feet + Vector3.up * 0.3f, null, "p1_smokeblast", Smoke, 12f, 1.2f);
                    Ring(feet + Vector3.up * 0.1f, "p1_firering", Color.white, 0f, 1, 0f, 1.2f, 1f, 30f);
                    Flash(centre, body.transform, Red, 8f, 6f);
                    GameCamera.instance?.AddShake(feet, 40f, 1.5f, false);
                    break;
                case Returned:
                    // A shot caught on the Iron Wall, flung back: steel ringing and a burst of gold.
                    Sounds.PlayAt("p2clash", at, 1f, 45f);
                    Sounds.PlayAt("p2glint", at, 0.8f, 40f);
                    Burst(at, null, "p1_spark", Gold, 24, 3f, 8f, 0.2f, 0.3f, 0.15f, 1f);
                    Flash(at, null, Gold, 4f, 6f);
                    break;
                case Hit2:
                    // Fjallbrjótr crashing into someone: the crunch, a burst of dust and grit, and stars - stunned.
                    Sounds.PlayAt("p2hit", at, 1f, 45f);
                    Sounds.PlayAt("p2clash", at, 0.5f, 35f);
                    Burst(at, null, "p1_dust", Dust, 12, 2f, 6f, 0.6f, 0.6f, 0.3f, 0.4f, grow: true);
                    Burst(at, null, "p1_spark", Ember, 14, 3f, 8f, 0.25f, 0.3f, 0.15f, 1f);
                    Burst(at + Vector3.up * 0.7f, null, "p1_star", Gold, 5, 0.4f, 1.2f, 0.4f, 0.9f, 0.3f, -0.1f);
                    Flash(at, null, Ember, 4f, 5f, 0.3f);
                    break;
                case Descend:
                    // Thunder and a flash in the sky, then he streaks down trailing red steam and embers.
                    Sounds.PlayAt("thunder", centre, 1f, 150f);
                    Sounds.PlayAt("shoulder_whoosh", centre, 1f, 60f);
                    Flash(centre + Vector3.up * 6f, null, Storm, 120f, 6f);
                    Trail(body.transform, "p1_puff", 2f, new Color(0.6f, 0.12f, 0.08f, 0.7f), 1.1f, 60f);
                    Trail(body.transform, "p1_fire", 2f, Ember, 0.35f, 40f);
                    break;
                case Land:
                    // The ground splits: a ring of light racing out, a wall of dust and smoke, red runes around him,
                    // and every camera near shakes.
                    Sounds.PlayAt("land", at, 1f, 90f);
                    Anim(at + Vector3.up * 0.3f, null, "p1_firering", Color.white, 12f, 1f);
                    Anim(at + Vector3.up * 0.2f, null, "p1_smokeblast", Smoke, 14f, 1.4f);
                    Ring(at + Vector3.up * 0.2f, "p1_puff", Smoke, 0.8f, 36, 7f, 1.2f, 1.3f);
                    Burst(at + Vector3.up * 0.2f, null, "p1_dust", Dust, 30, 3f, 9f, 0.7f, 1.1f, 0.6f, 1.2f, grow: true);
                    Burst(at + Vector3.up * 0.3f, null, "p1_spark", Ember, 26, 4f, 10f, 0.2f, 0.5f, 0.4f, 1f);
                    Ring(at + Vector3.up * 0.05f, Textures.Runes, Red, 2.4f, 10, 0f, 1.6f, 0.6f);
                    Flash(at + Vector3.up, null, Ember, 14f, 9f);
                    GameCamera.instance?.AddShake(at, 40f, 3f, false);
                    break;
            }
        }

        // One step of the FURY shockwave racing along the ground (GroundWave), on every game.
        internal static void WaveStep(Vector3 at, float scale = 1f)
        {
            Anim(at + Vector3.up * 0.9f * scale, null, "p2_eruption", new Color(0.8f, 0.5f, 0.35f, 1f), 2.6f * scale, 0.6f, upright: true);
            Anim(at + Vector3.up * 0.4f * scale, null, "p2_explosion", Color.white, 1.6f * scale, 0.5f);
        }

        // A shot flung back off the Iron Wall (ReturnedShot): a gold-hot glow riding on it, embers shed behind.
        internal static void ShotGlow(Transform shot, float life) =>
            Anim(shot.position, shot, "p2_fireball", Gold, 1.5f, life);

        internal static void ShotSpark(Vector3 at) =>
            Burst(at, null, "p1_spark", Ember, 4, 0.3f, 1.2f, 0.25f, 0.3f, 0.05f, 0.3f);

        // Where a Strike will fall: red runes in a ring (Grave Ring) or pale runes and a glow (Odin's Wrath).
        internal static void StrikeMark(Vector3 at, int kind, float life)
        {
            // Big and bright enough to read at a glance: large runes, and a ring of fire swelling flat on the ground.
            if (kind == Strikes.GraveRing)
            {
                Sounds.PlayAt("rune", at, 1f, 50f);
                Ring(at + Vector3.up * 0.1f, Textures.Runes, Red, Strikes.RingOuter - 0.4f, 36, 0f, life, 1.2f);
                Ring(at + Vector3.up * 0.1f, Textures.Runes, Red, Strikes.RingInner + 0.4f, 18, 0f, life, 0.9f);
                Ring(at + Vector3.up * 0.05f, "p1_firering", Color.white, 0f, 1, 0f, life, 1f, Strikes.RingOuter * 2f);
                Flash(at + Vector3.up, null, Red, 9f, 4f, life);
            }
            else
            {
                Sounds.PlayAt("p2glint", at, 0.7f, 50f);
                Ring(at + Vector3.up * 0.1f, Textures.Runes, Storm, Strikes.BoltRadius - 0.2f, 12, 0f, life, 0.9f);
                Ring(at + Vector3.up * 0.05f, "p1_firering", new Color(0.7f, 0.85f, 1f, 1f), 0f, 1, 0f, life, 1f, Strikes.BoltRadius * 2f);
            }
        }

        // The Strike falling: the ground erupting all along the ring, or a bolt of lightning.
        internal static void StrikeBurst(Vector3 at, int kind)
        {
            if (kind == Strikes.GraveRing)
            {
                Sounds.PlayAt("p2boom", at, 1f, 80f);
                Sounds.PlayAt("p2rumble", at, 0.9f, 60f);
                float radius = (Strikes.RingInner + Strikes.RingOuter) * 0.5f;
                for (int i = 0; i < 14; i++)
                {
                    float angle = i * Mathf.PI * 2f / 14f;
                    Vector3 p = at + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (ZoneSystem.instance != null)
                        p.y = ZoneSystem.instance.GetSolidHeight(p);
                    WaveStep(p);
                }
                GameCamera.instance?.AddShake(at, 30f, 1.5f, false);
                return;
            }
            // The bolt is a blink: the strike, its sparks and its light all gone within a quarter of a second.
            Sounds.PlayAt("thunder", at, 1f, 120f);
            Bolt(at);
            Burst(at + Vector3.up * 0.3f, null, "p1_spark", Storm, 30, 4f, 11f, 0.3f, 0.25f, 0.3f, 1f);
            Flash(at + Vector3.up * 2f, null, Storm, 16f, 10f, 0.2f);
            GameCamera.instance?.AddShake(at, 25f, 1f, false);
        }

        // A jagged bolt from high above down to the ground, gone in a blink.
        private static void Bolt(Vector3 ground)
        {
            var bolt = new GameObject("Alfrik_Bolt");
            LineRenderer line = bolt.AddComponent<LineRenderer>();
            line.sharedMaterial = Aura.Soft;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            const int points = 12;
            line.positionCount = points;
            Vector3 top = ground + Vector3.up * 30f + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
            for (int i = 0; i < points; i++)
            {
                float t = i / (points - 1f);
                Vector3 p = Vector3.Lerp(top, ground, t);
                if (i > 0 && i < points - 1)
                    p += new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f));
                line.SetPosition(i, p);
            }
            line.widthMultiplier = 0.6f;
            line.startColor = new Color(0.85f, 0.9f, 1f, 1f);
            line.endColor = Color.white;
            Object.Destroy(bolt, 0.12f);
        }

        // A thrown axe biting into the ground.
        internal static void WaveStepSmall(Vector3 at) =>
            Burst(at + Vector3.up * 0.1f, null, "p1_dust", Dust, 6, 0.8f, 2f, 0.4f, 0.6f, 0.15f, 0.5f, grow: true);

        // Sprite sheets (4 x 4 frames) played once over their life; upright ones stand on the ground.
        private static readonly System.Collections.Generic.Dictionary<string, int> Sheets = new System.Collections.Generic.Dictionary<string, int>
        {
            ["p2_explosion"] = 4, ["p2_fireball"] = 4, ["p2_eruption"] = 4,
            ["p1_firering"] = 8, ["p1_smokeblast"] = 8, ["p1_firecrown"] = 8, ["p1_bloodring"] = 8, ["p1_ravenring"] = 8,
        };

        private static void Anim(Vector3 position, Transform follow, string texture, Color colour, float size, float life, bool upright = false)
        {
            ParticleSystem system = System(position, follow, texture, colour, size, life, 1);
            ParticleSystem.MainModule main = system.main;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            if (upright)
            {
                main.startRotation = 0f;
                system.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.VerticalBillboard;
            }
            system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            system.Play();
        }

        private static ParticleSystem System(Vector3 position, Transform follow, string texture, Color colour, float size, float life, int max)
        {
            var host = new GameObject("Alfrik_fx");
            if (follow != null)
                host.transform.SetParent(follow, true);
            host.transform.position = position;
            ParticleSystem system = Aura.Particles(host, colour, size, life, max, true, Textures.Get(texture));
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            if (texture == Textures.Runes)
                Textures.RuneSheet(system);
            else if (Sheets.TryGetValue(texture, out int tiles))
            {
                ParticleSystem.TextureSheetAnimationModule sheet = system.textureSheetAnimation;
                sheet.enabled = true;
                sheet.numTilesX = tiles;
                sheet.numTilesY = tiles;
                sheet.animation = ParticleSystemAnimationType.WholeSheet;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                sheet.cycleCount = 1;
            }
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            // The sheets fade out themselves (their last frames); every other effect fades over its life.
            fade.enabled = !Sheets.ContainsKey(texture);
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            return system;
        }

        // A burst from a sphere; gravity < 0 rises, grow puffs the particles up (dust, smoke).
        private static void Burst(Vector3 position, Transform follow, string texture, Color colour, int count, float speedMin,
            float speedMax, float size, float life, float radius, float gravity, bool grow = false)
        {
            ParticleSystem system = System(position, follow, texture, colour, size, life, count);
            ParticleSystem.MainModule main = system.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.gravityModifier = gravity;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            if (grow)
                Grow(system, 0.6f, 1.6f);
            system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            system.Play();
        }

        // A flat ring on the ground: speed 0 stays (rising a little), speed > 0 spreads out as a shockwave. grow > 0 is one
        // flat particle that swells to that size (a ring of light).
        private static void Ring(Vector3 position, string texture, Color colour, float radius, int count, float speed, float life,
            float size, float grow = 0f)
        {
            ParticleSystem system = System(position, null, texture, colour, size, life, count);
            system.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ParticleSystem.MainModule main = system.main;
            main.startSpeed = speed;
            main.gravityModifier = speed > 0f ? 0f : -0.15f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            if (grow > 0f)
            {
                shape.enabled = false;
                main.startSize = 1f;
                Grow(system, 0.2f, grow);
                system.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            }
            system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            system.Play();
        }

        // One big sprite facing the way he strikes (a slash).
        private static void Sprite(Vector3 position, Vector3 forward, string texture, Color colour, float size, float life)
        {
            ParticleSystem system = System(position, null, texture, colour, size, life, 1);
            ParticleSystem.MainModule main = system.main;
            main.startRotation = Random.Range(-0.6f, 0.6f);
            Grow(system, 0.7f, 1.2f);
            system.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            system.Play();
        }

        private static void Grow(ParticleSystem system, float from, float to)
        {
            ParticleSystem.SizeOverLifetimeModule growth = system.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
        }

        // Left behind him while he moves: dust kicked up in a charge (the default), steam and embers in his dive.
        private static void Trail(Transform body, string texture, float seconds, Color? colour = null, float size = 0.55f, float rate = 30f)
        {
            ParticleSystem system = System(body.position + Vector3.up * 0.1f, body, texture, colour ?? Dust, size, 0.9f, Mathf.RoundToInt(rate * 2f));
            ParticleSystem.MainModule main = system.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.duration = seconds;
            main.gravityModifier = -0.03f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = rate;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            Grow(system, 0.6f, 1.6f);
            system.Play();
        }

        private static void Flash(Vector3 position, Transform follow, Color colour, float range, float intensity, float fade = 0.6f)
        {
            var flash = new GameObject("Alfrik_flash");
            if (follow != null)
                flash.transform.SetParent(follow, true);
            flash.transform.position = position;
            Light light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.color = colour;
            light.range = range;
            light.intensity = intensity;
            flash.AddComponent<FlashFade>().Duration = fade;
        }
    }

    // Huginn's Mark: a raven-blue rune over the marked enemy's head, seen by everyone, for as long as the mark lasts.
    internal class MarkSigil : MonoBehaviour
    {
        private Character _marked;
        private float _until;

        internal static void Show(Character marked, float seconds)
        {
            var sigil = new GameObject("Alfrik_Mark");
            sigil.AddComponent<MarkSigil>().Begin(marked, seconds);
        }

        private void Begin(Character marked, float seconds)
        {
            _marked = marked;
            _until = Time.time + seconds;
            transform.position = marked.GetHeadPoint() + Vector3.up * 0.7f;
            ParticleSystem system = Aura.Particles(gameObject, new Color(0.35f, 0.5f, 1f, 0.95f), 0.55f, 0.35f, 4, false, Textures.Get(Textures.Runes));
            Textures.RuneSheet(system, Textures.Ansuz);
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 8f;
            system.Play();
            Light light = gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.color = new Color(0.35f, 0.5f, 1f);
            light.range = 2.5f;
            light.intensity = 2f;
            Sounds.PlayAt("mark", transform.position, 1f, 50f);
            Sounds.PlayAt("raven", transform.position, 0.8f, 50f);
        }

        private void Update()
        {
            if (_marked == null || _marked.IsDead() || Time.time > _until)
            {
                Destroy(gameObject);
                return;
            }
            transform.position = _marked.GetHeadPoint() + Vector3.up * (0.7f + Mathf.Sin(Time.time * 3f) * 0.08f);
        }
    }

    internal class FlashFade : MonoBehaviour
    {
        internal float Duration = 0.6f;
        private Light _light;
        private float _start, _intensity;

        private void Start()
        {
            _light = GetComponent<Light>();
            _intensity = _light.intensity;
            _start = Time.time;
        }

        private void Update()
        {
            float t = (Time.time - _start) / Duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            _light.intensity = _intensity * (1f - t);
        }
    }
}
