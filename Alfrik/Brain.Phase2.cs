using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Alfrik
{
    // Phase 2, The Unbroken: Skeggöld in both hands. A wall that answers: he does not roll, he blocks, parries and
    // counterattacks, and his blows cover ground (every swing of Skeggöld is 180 degrees wide).
    //  Iron Wall        - against a swing or a shot coming at him, often after a burst: the great axe raised as a guard
    //                     (the game's block) for 2.4 s. Hits from the front are blocked; a blunt or heavy (secondary)
    //                     hit breaks it (he is never staggered in this phase); the third hit blocked is answered at once
    //                     with a heavy chop. Hits from the side or behind land. cd 14 s.
    //  Nothing of his can be blocked or parried in this phase: a guard raised against him breaks and staggers.
    //  Punishing Parry  - only against a swing coming at him: a gold glint on the axe, then for 1.1 s a melee hit is parried: the attacker staggers and
    //                     loses stamina, and his heavy chop that follows cannot be blocked. cd 13 s.
    //  Iron Wall, Returned - a shot caught on the front of the Iron Wall is flung back at whoever loosed it (ReturnedShot).
    //  Reaping Sweep (Twin Harvest) - twists his body back, then the wide sweep and the reverse sweep straight after it
    //                     (the second x1.2). cd 14 s.
    //  Odin's Fall      - against a target 8-20 m off: a crouch, then he leaps in a high arc and comes down just short of
    //                     it, a blast 5 m around that cannot be blocked. Only where there is open sky and ground to land on.
    //                     cd 20 s.
    //  Earthsplitter    - against a target 7-25 m off: the axe raised, then driven into the ground, and a rupture races
    //                     26 m along the ground at the target (GroundWave). cd 16 s.
    //  War Stomp        - someone beside or behind him within 4 m (or two enemies that close) for a moment: the knee
    //                     drawn up, then the foot comes down and the ground heaves 4 m around, throwing them back. cd 18 s.
    //  Executioner      - the axe raised high, a pause, a red flash, then an overhead chop that cannot be blocked. cd 18 s.
    //  Shoulder Breaker - phase 1's bash from a shorter step, against a raised guard. cd 14 s.
    //  FURY: Odin's Judgment - plants the axe, roars and his runes flare, then three chops, each heavier than the last
    //                     (x1, x1.3, x1.6). No shockwave.
    //                     First 12 s into the phase, then every 45 s.
    internal static partial class Brain
    {
        private static readonly AccessTools.FieldRef<Humanoid, bool> SecondaryOf = AccessTools.FieldRefAccess<Humanoid, bool>("m_currentAttackIsSecondary");

        // His damage on the swing under way (FURY's chops grow); read by AlfrikBoss.DamageDealt.
        internal static float SwingBoost = 1f;

        private static float _heavyUntil, _nextWall, _nextParry2, _nextSweep, _nextHook, _nextExec, _nextFury, _waveAt;
        private static int _wallBlocks;
        private static float _nextFall, _nextSplit, _nextStomp, _splitAt, _behindSince = -1f;
        private static Vector3 _leapFrom, _leapTo;
        private static readonly AccessTools.FieldRef<Character, float> AirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static int _leapMask, _arcMask;
        private const float FallRadius = 5f, StompRadius = 4f, HornRadius = 15f;
        private static float _nextRetrib, _retribUntil, _nextRetaliate, _nextRing, _nextWrath, _nextHorn, _nextAdvance,
            _nextExecStomp, _strikeAt;
        private static int _strikes;
        private static Character _retaliate;
        private static readonly List<Act> _ready = new List<Act>();

        // Retribution is on: every melee hit he takes is answered with a sweep.
        private static bool Retribution => Time.time < _retribUntil;

        private static bool Phase2 => AlfrikBoss.State == 1 && AlfrikBoss.PhaseNumber == 2;

        // In the air in Odin's Fall (his body is moved along the arc, so it is not smoothed by physics meanwhile).
        internal static bool Leaping => _act == Act.Fall;

        private static int PoseOf(Act act)
        {
            switch (act)
            {
                case Act.TwinTell: return Poses.Crouch;
                case Act.Guard: return Poses.Cross;
                case Act.RavensTell: return Poses.ArmsBack;
                case Act.SweepTell: return Poses.Twist;
                case Act.HookTell: return Poses.Reach;
                case Act.ExecTell: return Poses.Raise;
                case Act.FuryTell: return Poses.Plant;
                case Act.FallTell: return Poses.Crouch;
                case Act.Fall: return Poses.Dive;
                case Act.FallLand: return Poses.Plant;
                case Act.SplitTell: return Poses.Raise;
                case Act.StompTell: return Poses.Stomp;
                case Act.RetribTell: return Poses.Cross;
                case Act.RingTell: return Poses.Raise;
                case Act.WrathTell: return Poses.Raise;
                case Act.Wrath: return Poses.Raise;
                case Act.HornTell: return Poses.ArmsBack;
                default: return Poses.None;
            }
        }

        private static void NewPhase2()
        {
            _nextFury = Time.time + 12f;
            _nextWall = _nextParry2 = _nextSweep = _nextHook = _nextExec = _nextStomp = 0f;
            _nextFall = Time.time + 6f;
            _nextSplit = Time.time + 4f;
            // The new ones come in over the first half minute, not all at once.
            _nextRetrib = Time.time + 15f;
            _nextRing = Time.time + 8f;
            _nextWrath = Time.time + 10f;
            _nextHorn = Time.time + 20f;
            _nextAdvance = Time.time + 12f;
            _nextExecStomp = 0f;
            _retribUntil = 0f;
            _retaliate = null;
            _waveAt = _splitAt = 0f;
            _behindSince = -1f;
            SwingBoost = 1f;
        }

        // A swing about to land or a shot in the air: Punishing Parry when close, otherwise often the Iron Wall.
        private static void Defend2(Humanoid c)
        {
            float now = Time.time;
            if (c.InAttack() || _act != Act.None || _incomingAttack == null || now < _nextDodgeScan)
                return;
            _nextDodgeScan = now + 0.05f;
            if (now >= _nextWall && _incomingProjectile?.Invoke(AlfrikBoss.Npc, null) is Character shooter)
            {
                StartWall(c, shooter);
                return;
            }
            if (!(_incomingAttack.Invoke(AlfrikBoss.Npc, null) is Character threat) || threat.IsStaggering())
            {
                _seenSwing = null;
                return;
            }
            Attack swing = threat is Humanoid swinger ? CurrentAttack(swinger) : null;
            if (swing == null || swing != _seenSwing)
            {
                _seenSwing = swing;
                _swingLands = now + HitDelay(threat, swing) * UnityEngine.Random.Range(0.85f, 1.05f);
                _swingAnswered = false;
            }
            if (_swingAnswered || now < _swingLands - 0.35f)
                return;
            _swingAnswered = true;
            float roll = UnityEngine.Random.value;
            bool close = Flat(threat.transform.position - c.transform.position).magnitude <= 3.8f;
            if (close && now >= _nextParry2 && roll < 0.45f)
                StartParry(c, threat);
            else if (now >= _nextWall && roll < 0.8f)
                StartWall(c, threat);
        }

        private static void StartWall(Humanoid c, Character threat)
        {
            Start(Act.Wall, 2.4f);
            _wallBlocks = 0;
            _target = threat;
            _nextWall = Time.time + 14f;
            Face(c, threat);
            Fx.Send(Fx.WallUp);
            Plugin.Log.LogInfo("Alfrik raises the Iron Wall against " + threat.GetHoverName());
        }

        private static void StartParry(Humanoid c, Character threat)
        {
            Start(Act.ParryStance, 1.1f);
            _target = threat;
            _nextParry2 = Time.time + 13f;
            Face(c, threat);
            Fx.Send(Fx.Glint);
            Plugin.Log.LogInfo("Alfrik readies a Punishing Parry against " + threat.GetHoverName());
        }

        private static bool Choose2(Humanoid c, float dist)
        {
            float now = Time.time;
            // FURY first when it is ready; otherwise one of the abilities ready and in range, at random, so they do not
            // always come in the same order.
            Act pick = Act.None;
            Vector3 spot = Vector3.zero;
            if (now >= _nextFury && dist <= 6f)
                pick = Act.FuryTell;
            else
            {
                _ready.Clear();
                if (dist <= 3.2f && now >= _nextExec)
                    _ready.Add(Act.ExecTell);
                if (dist <= 3.6f && now >= _nextSweep)
                    _ready.Add(Act.SweepTell);
                if (dist >= 8f && dist <= 20f && now >= _nextFall && LeapSpot(c, _target, out spot))
                    _ready.Add(Act.FallTell);
                if (dist >= 7f && dist <= 25f && now >= _nextSplit)
                    _ready.Add(Act.SplitTell);
                if (dist <= 4f && now >= _nextRetrib)
                    _ready.Add(Act.RetribTell);
                if (dist <= 6f && now >= _nextRing)
                    _ready.Add(Act.RingTell);
                if (dist >= 5f && dist <= 30f && now >= _nextWrath)
                    _ready.Add(Act.WrathTell);
                if (dist <= 12f && now >= _nextHorn)
                    _ready.Add(Act.HornTell);
                if (dist >= 6f && dist <= 16f && now >= _nextAdvance)
                    _ready.Add(Act.Advance);
                if (_ready.Count == 0)
                    return false;
                pick = _ready[UnityEngine.Random.Range(0, _ready.Count)];
            }
            switch (pick)
            {
                case Act.FuryTell:
                    _nextFury = now + 45f;
                    Start(Act.FuryTell, Tell(1.2f));
                    Fx.Send(Fx.Fury);
                    Plugin.Log.LogInfo("Alfrik: FURY - Odin's Judgment");
                    break;
                case Act.ExecTell:
                    _nextExec = now + 18f;
                    Start(Act.ExecTell, Tell(0.8f));
                    Plugin.Log.LogInfo("Alfrik: Executioner");
                    break;
                case Act.SweepTell:
                    _nextSweep = now + 14f;
                    Start(Act.SweepTell, Tell(0.5f));
                    Sounds.PlayAt("growl", c.GetCenterPoint(), 0.8f);
                    Plugin.Log.LogInfo("Alfrik: Reaping Sweep");
                    break;
                case Act.FallTell:
                    _nextFall = now + 20f;
                    _leapTo = spot;
                    Start(Act.FallTell, Tell(0.6f));
                    Face(c, _target);
                    Sounds.PlayAt("p2growl", c.GetCenterPoint(), 0.8f);
                    Plugin.Log.LogInfo("Alfrik: Odin's Fall at " + dist.ToString("0.0") + " m");
                    break;
                case Act.SplitTell:
                    _nextSplit = now + 16f;
                    Start(Act.SplitTell, Tell(0.8f));
                    Plugin.Log.LogInfo("Alfrik: Earthsplitter at " + dist.ToString("0.0") + " m");
                    break;
                case Act.RetribTell:
                    _nextRetrib = now + 30f;
                    Start(Act.RetribTell, Tell(0.6f));
                    Sounds.PlayAt("p2growl", c.GetCenterPoint(), 0.8f);
                    Plugin.Log.LogInfo("Alfrik: Retribution");
                    break;
                case Act.RingTell:
                    _nextRing = now + 22f;
                    Start(Act.RingTell, Tell(0.7f));
                    Plugin.Log.LogInfo("Alfrik: Grave Ring");
                    break;
                case Act.WrathTell:
                    _nextWrath = now + 25f;
                    Start(Act.WrathTell, Tell(1f));
                    Sounds.PlayAt("thunder", c.GetCenterPoint(), 0.5f, 80f);
                    Plugin.Log.LogInfo("Alfrik: Odin's Wrath at " + dist.ToString("0.0") + " m");
                    break;
                case Act.HornTell:
                    _nextHorn = now + 28f;
                    Start(Act.HornTell, Tell(0.5f));
                    Plugin.Log.LogInfo("Alfrik: War Horn");
                    break;
                case Act.Advance:
                    _nextAdvance = now + 24f;
                    Start(Act.Advance, 4f);
                    _wallBlocks = 0;
                    Face(c, _target);
                    Fx.Send(Fx.WallUp);
                    Plugin.Log.LogInfo("Alfrik: Iron Advance at " + dist.ToString("0.0") + " m");
                    break;
            }
            // No snap: during the warning he turns to the target the usual way. Between his abilities there is room for
            // his ordinary swings, so they do not run into each other.
            _nextAbility = now + UnityEngine.Random.Range(1.5f, 2.5f);
            return true;
        }

        // Retribution, when he is free: the last one who struck him close by takes a sweep straight back (once every
        // 0.6 s at most).
        private static bool Retaliate(Humanoid c, bool attacking)
        {
            if (_retaliate == null)
                return false;
            if (!Retribution || _retaliate.IsDead())
            {
                _retaliate = null;
                return false;
            }
            if (attacking || Time.time < _nextRetaliate)
                return false;
            Character foe = _retaliate;
            _retaliate = null;
            if (Flat(foe.transform.position - c.transform.position).magnitude > 4.5f)
                return false;
            _target = foe;
            if (!Swing(c, false))
                return false;
            _nextRetaliate = Time.time + 0.6f;
            Fx.SendAt(Fx.Block, c.GetCenterPoint() + c.transform.forward * 0.6f);
            Plugin.Log.LogInfo("Alfrik's Retribution answers " + foe.GetHoverName());
            return true;
        }

        // War Stomp, checked every frame he is free: someone at his side or back within 4 m, or two enemies that close,
        // for 0.4 s.
        private static void Stomp(Humanoid c)
        {
            float now = Time.time;
            if (_act != Act.None || c.InAttack() || now < _nextStomp)
            {
                _behindSince = -1f;
                return;
            }
            List<Character> near = Enemies(c, StompRadius);
            bool flanked = false;
            foreach (Character enemy in near)
                if (Vector3.Angle(Flat(c.transform.forward), Flat(enemy.transform.position - c.transform.position)) > 75f)
                    flanked = true;
            if (!flanked && near.Count < 2)
            {
                _behindSince = -1f;
                return;
            }
            if (_behindSince < 0f)
                _behindSince = now;
            if (now - _behindSince < 0.4f)
                return;
            _behindSince = -1f;
            _nextStomp = now + 18f;
            Start(Act.StompTell, Tell(0.45f));
            Sounds.PlayAt("p2growl", c.GetCenterPoint(), 0.7f);
            Plugin.Log.LogInfo("Alfrik: War Stomp (" + near.Count + " near" + (flanked ? ", one at his side or back)" : ")"));
        }

        private static void StompHit(Humanoid c)
        {
            Fx.Send(Fx.StompHit);
            int thrown = 0;
            foreach (Character enemy in Enemies(c, StompRadius + 0.5f))
            {
                if (Mathf.Abs(enemy.transform.position.y - c.transform.position.y) > 2f)
                    continue;
                Vector3 away = Flat(enemy.transform.position - c.transform.position);
                Blast(c, enemy, away, 0.6f, 110f, 2.5f);
                thrown++;
            }
            Plugin.Log.LogInfo("Alfrik's War Stomp throws back " + thrown);
        }

        // A blow from the ground under them (Odin's Fall, War Stomp): Skeggöld's own damage scaled, thrown away from him,
        // never blocked.
        private static void Blast(Humanoid c, Character enemy, Vector3 away, float share, float push, float stagger)
        {
            ItemDrop.ItemData weapon = c.GetCurrentWeapon();
            var hit = new HitData();
            if (weapon != null)
                hit.m_damage = weapon.GetDamage();
            else
                hit.m_damage.m_blunt = 80f;
            hit.ApplyModifier(share * c.GetRandomSkillFactor(Skills.SkillType.Axes));
            hit.m_point = enemy.GetCenterPoint();
            hit.m_dir = away.sqrMagnitude > 0.0001f ? away.normalized : c.transform.forward;
            hit.m_pushForce = push;
            hit.m_staggerMultiplier = stagger;
            hit.m_blockable = false;
            hit.m_dodgeable = true;
            hit.m_skill = Skills.SkillType.Axes;
            hit.SetAttacker(c);
            enemy.Damage(hit);
            Fx.SendAt(Fx.Hit, enemy.GetCenterPoint());
        }

        // ---- Odin's Fall ----

        // Where he comes down: 1.6 m short of the target, on solid ground near its height (not on a roof above it), with
        // room to stand, and nothing - a roof, a wall, a hill - in the way of the arc.
        private static bool LeapSpot(Humanoid c, Character target, out Vector3 spot)
        {
            spot = Vector3.zero;
            if (target == null || ZoneSystem.instance == null)
                return false;
            if (_leapMask == 0)
            {
                _leapMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");
                _arcMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            }
            Vector3 from = c.transform.position;
            Vector3 toTarget = Flat(target.transform.position - from);
            spot = target.transform.position - (toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : c.transform.forward) * 1.6f;
            float ground = ZoneSystem.instance.GetSolidHeight(spot);
            if (Mathf.Abs(ground - target.transform.position.y) > 2f)
                return false;
            spot.y = ground;
            if (Physics.CheckCapsule(spot + Vector3.up * 0.8f, spot + Vector3.up * 1.7f, 0.4f, _leapMask))
                return false;
            for (int i = 1; i <= 3; i++)
                if (Physics.CheckSphere(ArcPoint(from, spot, i * 0.25f) + Vector3.up, 0.5f, _arcMask))
                    return false;
            return true;
        }

        private static Vector3 ArcPoint(Vector3 from, Vector3 to, float t)
        {
            float height = 3f + 0.12f * Flat(to - from).magnitude;
            return Vector3.Lerp(from, to, t) + Vector3.up * (4f * height * t * (1f - t));
        }

        // Off the ground: his body kinematic (as in his arrival), so physics and fall damage stay out of it.
        private static void StartLeap(Humanoid c)
        {
            _leapFrom = c.transform.position;
            float distance = Flat(_leapTo - _leapFrom).magnitude;
            Start(Act.Fall, Mathf.Lerp(0.7f, 1.05f, Mathf.InverseLerp(8f, 20f, distance)));
            Rigidbody body = c.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.interpolation = RigidbodyInterpolation.None;
                body.isKinematic = true;
            }
            Face(c, _target);
            Fx.Send(Fx.Leap);
        }

        private static void Leap(Humanoid c, float now)
        {
            c.SetMoveDir(Vector3.zero);
            float t = Mathf.Clamp01((now - _actStart) / Mathf.Max(_actEnd - _actStart, 0.01f));
            Place(c, ArcPoint(_leapFrom, _leapTo, t));
            if (t < 1f)
                return;
            EndLeap(c);
            Fx.SendAt(Fx.FallLand, c.transform.position + Vector3.up * 0.01f);
            int hit = 0;
            foreach (Character enemy in Enemies(c, FallRadius + 0.5f))
            {
                if (Mathf.Abs(enemy.transform.position.y - c.transform.position.y) > 2.5f)
                    continue;
                Blast(c, enemy, Flat(enemy.transform.position - c.transform.position), 1.1f, 90f, 2f);
                hit++;
            }
            Plugin.Log.LogInfo("Alfrik lands Odin's Fall " + Flat(c.transform.position - _leapFrom).magnitude.ToString("0.0") +
                " m on, " + hit + " caught");
            Start(Act.FallLand, 0.7f);
        }

        // Back on his feet: physics on again, and the fall forgotten (no fall damage from the leap's height).
        private static void EndLeap(Humanoid c)
        {
            Rigidbody body = c.GetComponent<Rigidbody>();
            if (body != null && body.isKinematic)
                body.isKinematic = false;
            AirAltitude(c) = c.transform.position.y;
        }

        private static void Place(Humanoid c, Vector3 position)
        {
            c.transform.position = position;
            Rigidbody body = c.GetComponent<Rigidbody>();
            if (body != null)
                body.position = position;
        }

        // Iron Wall and Punishing Parry answer an attack (Defend2), never come on their own.
        private static void AfterBurst2(Humanoid c)
        {
            if (UnityEngine.Random.value < 0.45f)
                Break(BreakMode.Hold, UnityEngine.Random.Range(0.6f, 1f));
            else
                Break(BreakMode.Circle, UnityEngine.Random.Range(0.8f, 1.3f));
        }

        private static void RunPhase2Act(Humanoid c, float dist, Vector3 toTarget)
        {
            float now = Time.time;
            switch (_act)
            {
                case Act.Wall:
                case Act.ParryStance:
                    Drive(c, Vector3.zero, toTarget, false);
                    Blocking(c) = true;
                    if (now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.SweepTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                        Start(Act.SweepChain, 4f);
                    break;
                case Act.SweepChain:
                    // Twin Harvest: the sweep, then the reverse sweep at once (the game carries the combo on).
                    Drive(c, Vector3.zero, toTarget, false);
                    if (!c.InAttack() && Time.time >= _recoverUntil && _swings < 2 && _target != null)
                    {
                        SwingBoost = _swings == 0 ? 1f : 1.2f;
                        if (Swing(c, false))
                        {
                            Fx.Send(Fx.Sweep);
                            _swings++;
                        }
                    }
                    if ((_swings >= 2 && !c.InAttack()) || now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.FallTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                    {
                        // Aimed where the target is now, if there is room to land there; otherwise where it was.
                        if (_target != null && LeapSpot(c, _target, out Vector3 spot))
                            _leapTo = spot;
                        StartLeap(c);
                    }
                    break;
                case Act.Fall:
                    Leap(c, now);
                    break;
                case Act.FallLand:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.SplitTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (_swings == 0 && now >= _actStart + (_actEnd - _actStart) * 0.6f)
                    {
                        _swings = 1;
                        Fx.Send(Fx.ExecFlash);
                    }
                    if (now >= _actEnd)
                    {
                        _heavyUntil = now + 0.3f;
                        Swing(c, true);
                        Start(Act.Split, 2.5f);
                        _splitAt = now + 0.45f;
                    }
                    break;
                case Act.Split:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (_splitAt > 0f && now >= _splitAt)
                    {
                        _splitAt = 0f;
                        GroundWave.SendSplit(c, toTarget);
                    }
                    if ((_splitAt <= 0f && !c.InAttack()) || now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.StompTell:
                    // He keeps facing the way he was: the stomp is for whoever is at his side or back.
                    Drive(c, Vector3.zero, c.transform.forward, false);
                    if (now >= _actEnd)
                    {
                        StompHit(c);
                        EndAct(c);
                        _recoverUntil = now + 0.5f;
                    }
                    break;
                case Act.HookTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                    {
                        Hook(c);
                        EndAct(c);
                    }
                    break;
                case Act.ExecTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (_swings == 0 && now >= _actStart + (_actEnd - _actStart) * 0.7f)
                    {
                        _swings = 1;
                        Fx.Send(Fx.ExecFlash);
                    }
                    if (now >= _actEnd)
                    {
                        Unblockable(1.3f);
                        _heavyUntil = now + 0.3f;
                        Swing(c, true);
                        // Executioner's Stomp: now and then the chop flows straight into a War Stomp.
                        if (now >= _nextExecStomp && UnityEngine.Random.value < 0.5f)
                        {
                            _nextExecStomp = now + 20f;
                            Start(Act.ExecFollow, 3f);
                            Plugin.Log.LogInfo("Alfrik: Executioner's Stomp");
                        }
                        else
                            EndAct(c);
                    }
                    break;
                case Act.ExecFollow:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actStart + 0.2f && !c.InAttack() && Time.time >= _recoverUntil)
                        Start(Act.StompTell, Tell(0.35f));
                    else if (now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.RetribTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                    {
                        _retribUntil = now + 5f;
                        _retaliate = null;
                        Fx.Send(Fx.IronSkin);
                        EndAct(c);
                    }
                    break;
                case Act.RingTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                    {
                        _heavyUntil = now + 0.3f;
                        Swing(c, true);
                        _strikeAt = now + 0.4f;
                        Start(Act.Ring, 2.5f);
                    }
                    break;
                case Act.Ring:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (_strikeAt > 0f && now >= _strikeAt)
                    {
                        _strikeAt = 0f;
                        Strikes.Send(c, c.transform.position, Strikes.GraveRing);
                    }
                    if ((_strikeAt <= 0f && !c.InAttack()) || now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.WrathTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                    {
                        _strikes = 0;
                        _strikeAt = now;
                        Start(Act.Wrath, 1.8f);
                    }
                    break;
                case Act.Wrath:
                    // Three bolts, 0.6 s apart, each called down where its mark is when it is called: the target, the
                    // second enemy near him if there is one, the target again.
                    Drive(c, Vector3.zero, toTarget, false);
                    if (_strikes < 3 && now >= _strikeAt)
                    {
                        Character second = _strikes == 1 ? SecondEnemy(c) : null;
                        Character mark = second != null ? second : _target;
                        if (mark != null && !mark.IsDead())
                            Strikes.Send(c, mark.transform.position, Strikes.Lightning);
                        _strikes++;
                        _strikeAt = now + 0.6f;
                    }
                    if (_strikes >= 3 && now >= _strikeAt)
                        EndAct(c);
                    break;
                case Act.HornTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                    {
                        Trigger("emote_roar");
                        Fx.Send(Fx.Horn);
                        int slowed = 0;
                        foreach (Character enemy in Enemies(c, HornRadius))
                        {
                            Slow.Apply(c, enemy);
                            slowed++;
                        }
                        Plugin.Log.LogInfo("Alfrik's War Horn slows " + slowed);
                        Start(Act.Horn, 1.8f);
                    }
                    break;
                case Act.Horn:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.Advance:
                    // Iron Advance: walking in behind the raised Iron Wall (WallHit takes the blows on it), then the bash.
                    Blocking(c) = true;
                    Drive(c, toTarget, toTarget, false);
                    if (dist <= 2.4f)
                    {
                        Blocking(c) = false;
                        _nextShoulder = Time.time + 14f;
                        _shoulderHit = false;
                        _shoulderAt = 0f;
                        Start(Act.Shoulder, Tell(0.3f));
                        Face(c, _target);
                        Plugin.Log.LogInfo("Alfrik's Iron Advance ends in a bash");
                    }
                    else if (now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.FuryTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                        Start(Act.FuryChain, 7f);
                    break;
                case Act.FuryChain:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (!c.InAttack() && Time.time >= _recoverUntil && _swings < 3 && _target != null)
                    {
                        bool last = _swings == 2;
                        SwingBoost = 1f + 0.3f * _swings;
                        if (last)
                            _heavyUntil = now + 0.3f;
                        if (Swing(c, last))
                        {
                            Fx.Send(Fx.FurySlash);
                            _swings++;
                        }
                    }
                    if ((_swings >= 3 && !c.InAttack()) || now >= _actEnd)
                        EndAct(c);
                    break;
            }
        }

        // The beard of the axe drags whoever stands in front of him to his feet; the next chop follows at once.
        private static void Hook(Humanoid c)
        {
            foreach (Character enemy in InFront(c, 5.5f, 60f))
            {
                Vector3 toHim = Flat(c.transform.position - enemy.transform.position);
                var hit = new HitData();
                hit.m_damage.m_pierce = 25f;
                hit.ApplyModifier(c.GetRandomSkillFactor(Skills.SkillType.Axes));
                hit.m_point = enemy.GetCenterPoint();
                hit.m_dir = toHim.sqrMagnitude > 0.0001f ? toHim.normalized : -c.transform.forward;
                // The pull itself is the Hooked status effect on their own game (SE_AlfrikHook), like the harpoon's.
                hit.m_pushForce = 0f;
                hit.m_statusEffectHash = SE_AlfrikHook.Hash;
                hit.m_staggerMultiplier = 0.5f;
                hit.m_blockable = true;
                hit.m_dodgeable = true;
                hit.m_skill = Skills.SkillType.Axes;
                hit.SetAttacker(c);
                enemy.Damage(hit);
                Fx.SendAt(Fx.BeardHook, enemy.GetCenterPoint());
            }
            _burstLeft = 1;
            _nextPressure = Time.time + 0.35f;
        }

        // Character.RPC_Damage (through OnHit) while the Iron Wall is up.
        private static bool WallHit(HitData hit)
        {
            Humanoid c = AlfrikBoss.Char;
            ZNetView view = AlfrikBoss.View;
            if (c == null || view == null || !view.IsOwner())
                return true;
            Vector3 dir = Flat(hit.m_dir);
            if (dir.sqrMagnitude < 0.0001f || Vector3.Dot(dir.normalized, Flat(c.transform.forward).normalized) > -0.25f)
                return true;
            Character attacker = hit.GetAttacker();
            float physical = hit.m_damage.GetTotalPhysicalDamage();
            bool heavy = (hit.m_damage.m_blunt > 0f && hit.m_damage.m_blunt >= physical * 0.5f) ||
                (attacker is Humanoid swinger && swinger.InAttack() && SecondaryOf(swinger));
            if (heavy)
            {
                EndAct(c);
                Fx.SendAt(Fx.GuardBreak, hit.m_point);
                Plugin.Log.LogInfo("Alfrik's Iron Wall is broken by " + (attacker != null ? attacker.GetHoverName() : "a heavy blow"));
                return true;
            }
            Fx.SendAt(Fx.Block, hit.m_point);
            _wallBlocks++;
            // A shot is flung back at whoever loosed it.
            if (hit.m_ranged && attacker != null && attacker != c)
            {
                ReturnedShot.Send(c, hit.m_point, attacker, hit.GetTotalDamage());
                Fx.SendAt(Fx.Returned, hit.m_point);
                Plugin.Log.LogInfo("Alfrik's Iron Wall returns " + attacker.GetHoverName() + "'s shot (" + hit.GetTotalDamage().ToString("0") + ")");
            }
            if (_wallBlocks >= 3 && attacker != null && !hit.m_ranged &&
                Flat(attacker.transform.position - c.transform.position).magnitude <= 3.8f)
            {
                EndAct(c);
                _target = attacker;
                Unblockable(1.2f);
                _heavyUntil = Time.time + 0.3f;
                Swing(c, true);
                Fx.Send(Fx.FurySlash);
                Plugin.Log.LogInfo("Alfrik answers the third blow on his Iron Wall");
            }
            return false;
        }
    }
}
