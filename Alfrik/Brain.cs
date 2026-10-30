using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Alfrik
{
    // His abilities, run each frame on the game that owns him, as a boss and as a companion. Phase 1, The Berserker, a
    // duelist who keeps up the pressure:
    //  Pressure       - in reach he attacks in bursts of 1-4 chops instead of waiting on the AI's attack timer; after a
    //                   burst he often rolls at once, otherwise a steady beat: holding his ground or circling.
    //                   An enemy he has staggered gets swung at, never guarded against.
    //  Wolf Step      - when an enemy starts a swing at him he may turn and roll out of it, mostly to the side, sometimes
    //                   back, untouchable for a moment, facing the way he rolls like a player does. As a hired companion
    //                   Vikings for Hire decides when (its React); this decides how.
    //  Hound's Chase  - sprints after a target more than 10 m away, until it is within 6 m.
    //  Charge         - sprints at a target 4.5-9 m away, bending toward it while it is ahead, and chops twice as he
    //                   arrives (no swings if he ran past). Cannot be blocked while it runs.
    //  As a boss every perfect parry cuts his combo and staggers him, a full stagger bar too (once per 8 s);
    //  a plain block costs the blocker 10 more stamina; his hits fill the stagger bar of who he hits by [Boss]
    //  StaggerOnYou. Only the Charge, the Twin Reaper cross, his counter and the Shoulder Charge cannot be blocked.
    //  Twin Reaper    - now and then, never on a fixed beat, one warning in five a feint: plants his feet with a rune flash, then four quick chops,
    //                   the last, the red cross-slash, cannot be blocked: block three, dodge the cross.
    //  Blade Catch    - only against a swing coming at him: raises the axes in a guard with a rune flash. A melee hit in that guard is parried: no damage,
    //                   the attacker staggers and loses stamina, and his counter (the leap) cannot be blocked.
    //  The leap (the axes' heavy attack) is only ever a punish on a staggered enemy in reach (NoBlindLeap).
    //                   Thrown and shot things still hit.
    //  Flying Hatchet - cocks his arm back with a rune flash and throws Hamr, which comes back to him (Hatchet).
    //  Ravens' Flight - both arms back with two raven-blue flashes: Hugr at his target, Hamr at a second enemy (or at
    //                   the same one a moment later); both come back. While his right hand is empty he fights with
    //  The Left Fist  - punches and every third a heavy hook; their perfect-parry window is tight, and a parried punch
    //                   makes him flinch and call both axes back at once.
    //  Catch & Cleave - an axe caught with an enemy within 3 m turns straight into a swing at it.
    //  War Cry        - both axes back in his hands after the fists: a roar and 6 s of +25% speed and damage.
    //  Shoulder Charge- in reach, mostly against a raised guard: a short dash and a bash that cannot be blocked,
    //                   knocking the target back and staggering it.
    //  Huginn's Mark  - in a group: a raven sigil over whoever hurt him most lately; he goes after them for 8 s.
    //  Bloodscent     - once a phase, below half health: his warnings get shorter for the rest of the phase.
    // He turns to face his target the moment he swings, so a leap goes where he means it. While a move or a roll runs his
    // AI is paused and the move steers him, so the two never pull against each other; the AI picks up after.
    internal static partial class Brain
    {
        private enum Act { None, Charge, TwinTell, TwinChain, Guard, Throw, Break, RavensTell, Fists, Shoulder,
            Wall, ParryStance, SweepTell, ExecTell, FuryTell, FuryChain,
            SweepChain, FallTell, Fall, FallLand, SplitTell, Split, StompTell,
            ExecFollow, RetribTell, RingTell, Ring, WrathTell, Wrath, HornTell, Horn, Advance }
        private enum BreakMode { Hold, Circle, Back }

        private const float Reach = 2.6f, ParryStaminaBreak = 40f;

        private static MethodInfo _incomingAttack, _incomingProjectile;
        private static FieldInfo _dodgeUntil, _invulnerableUntil;
        private static readonly AccessTools.FieldRef<Character, bool> Blocking = AccessTools.FieldRefAccess<Character, bool>("m_blocking");
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Zanim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");

        private static Act _act;
        private static float _actStart, _actEnd;
        private static int _swings;
        private static Character _target;
        private static MonsterAI _ai, _paused;
        private static bool _chasing;
        private static Vector3 _rollDir;
        private static float _rollUntil;
        private static float _nextAbility, _nextCharge, _nextTwin, _nextGuard, _nextThrow, _nextDodgeScan, _nextPressure, _nextClose;
        private static string _throwTrigger;
        private static int _burstLeft, _rolls;
        private static bool _afterBurst, _rollCheck;
        private static BreakMode _breakMode;
        private static float _side = 1f, _rollWindow, _noRollUntil;
        private static Vector3 _rollFrom, _chargeDir, _chargeFrom;
        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        private static Attack _seenSwing;
        private static float _swingLands, _nextShotDodge, _flinchUntil;
        private static bool _parryStagger;
        private static bool _swingAnswered;
        private static int _rollMask;
        private static int _equips, _equipTraces, _swingFrames, _turnFlips, _stepFlips, _speedChanges, _staggers;
        private static float _nextReport, _watchYaw = float.NaN, _watchTurn, _watchSpeed = 1f, _nextStagger;
        private static Vector3 _watchPos, _watchStep;
        private static Character _staggerVictim, _lastFoe, _perfectParrier;
        private static readonly AccessTools.FieldRef<MonsterAI, float> SinceAttacking = AccessTools.FieldRefAccess<MonsterAI, float>("m_timeSinceAttacking");
        private static readonly AccessTools.FieldRef<MonsterAI, float> Unreachable = AccessTools.FieldRefAccess<MonsterAI, float>("m_unableToAttackTargetTimer");
        private static readonly AccessTools.FieldRef<Character, Vector3> MoveDirOf = AccessTools.FieldRefAccess<Character, Vector3>("m_moveDir");
        private static float _traceUntil, _nextTrace;
        private static int _traces;
        private static bool _wasSwinging;
        private static Vector3 _tracePos;
        private static float _traceYaw;

        private static readonly int FistKey = "alfrik_fist_until".GetStableHashCode();
        internal static readonly int BloodKey = "alfrik_bloodscent".GetStableHashCode();
        private static readonly AccessTools.FieldRef<MonsterAI, Character> TargetOf = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
        private static readonly Dictionary<Character, float> _hurt = new Dictionary<Character, float>();
        private static float _nextRavens, _nextShoulder, _nextMark, _nextPunch, _warCryUntil, _markUntil, _nextMarkAim,
            _secondThrowAt, _hurtSince;
        private static int _punches;
        private static bool _hook, _bloodUsed, _shoulderHit;
        private static Character _secondTarget, _marked;

        // Owner: War Cry is up (more speed here, more damage in AlfrikBoss.DamageDealt).
        internal static bool WarCry => Time.time < _warCryUntil;

        private static bool Blood => AlfrikBoss.View != null && AlfrikBoss.View.IsValid() && AlfrikBoss.View.GetZDO().GetBool(BloodKey, false);

        // Under Bloodscent his warnings are shorter.
        private static float Tell(float seconds) => Blood ? seconds * 0.6f : seconds;

        private static readonly int UnblockableKey = "alfrik_unblockable_until".GetStableHashCode();
        private static readonly AccessTools.FieldRef<Humanoid, float> BlockTimer = AccessTools.FieldRefAccess<Humanoid, float>("m_blockTimer");

        // Owner: his hits cannot be blocked until this game time (ticks), synced so the game of whoever blocks knows.
        // Phase 1 (The Berserker): everything of his can be blocked and parried, so no window is set.
        private static void Unblockable(float seconds)
        {
            if (!Phase2)
                return;
            ZNetView view = AlfrikBoss.View;
            if (view != null && view.IsValid() && ZNet.instance != null)
                view.GetZDO().Set(UnblockableKey, ZNet.instance.GetTime().AddSeconds(seconds).Ticks);
        }

        internal static void Init(Harmony harmony, Type viking)
        {
            _incomingAttack = AccessTools.Method(viking, "IncomingAttack");
            _incomingProjectile = AccessTools.Method(viking, "IncomingProjectile");
            _dodgeUntil = AccessTools.Field(viking, "m_dodgeUntil");
            _invulnerableUntil = AccessTools.Field(viking, "m_invulnerableUntil");
            MethodInfo react = AccessTools.Method(viking, "React");
            if (react != null)
                harmony.Patch(react, prefix: new HarmonyMethod(typeof(Brain), nameof(React)));
            harmony.Patch(AccessTools.Method(typeof(Humanoid), "BlockAttack"),
                prefix: new HarmonyMethod(typeof(Brain), nameof(BlockingHim)), postfix: new HarmonyMethod(typeof(Brain), nameof(BlockedHim)));
            harmony.Patch(AccessTools.Method(typeof(Character), "RPC_Stagger"), prefix: new HarmonyMethod(typeof(Brain), nameof(NoStagger)));
            harmony.Patch(AccessTools.Method(typeof(Character), "RPC_Damage"), prefix: new HarmonyMethod(typeof(Brain), nameof(HitBy)),
                postfix: new HarmonyMethod(typeof(Brain), nameof(HitDone)));
            harmony.Patch(AccessTools.Method(typeof(Character), "AddStaggerDamage"), prefix: new HarmonyMethod(typeof(Brain), nameof(LessStagger)));
            harmony.Patch(AccessTools.Method(typeof(Humanoid), nameof(Humanoid.EquipItem)), postfix: new HarmonyMethod(typeof(Brain), nameof(Equipped)));
            harmony.Patch(AccessTools.Method(typeof(Humanoid), nameof(Humanoid.StartAttack)), prefix: new HarmonyMethod(typeof(Brain), nameof(NoBlindLeap)),
                postfix: new HarmonyMethod(typeof(Brain), nameof(Swung)));
        }

        internal static void Trigger(string name)
        {
            Humanoid c = AlfrikBoss.Char;
            if (c != null && !string.IsNullOrEmpty(name))
                Zanim(c)?.SetTrigger(name);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        internal static void Update()
        {
            if (Entrance.Active)
            {
                // His arrival runs him; the AI waits until he has roared.
                _act = Act.None;
                _ai = AlfrikBoss.Char.GetBaseAI() as MonsterAI;
                Pause();
                Poses.Set(Entrance.Pose);
                return;
            }
            Think();
            if (AlfrikBoss.View != null && AlfrikBoss.View.IsValid() && AlfrikBoss.View.IsOwner() && AlfrikBoss.State == 1)
                Watch(AlfrikBoss.Char);
            if (AlfrikBoss.InTransition)
            {
                Poses.Set(AlfrikBoss.TransitionPose);
                return;
            }
            // A warning pose fades out just before the strike it announces, so pose and swing never overlap.
            int pose = PoseOf(_act);
            if (pose != Poses.None && _act != Act.Guard && Time.time > _actEnd - 0.15f)
                pose = Poses.None;
            bool reel = Time.time < _stunUntil && AlfrikBoss.Char != null && !AlfrikBoss.Char.IsStaggering();
            Poses.Set(reel ? Poses.Reel : Time.time < _stunUntil ? Poses.None : pose);
        }

        private static void Think()
        {
            Humanoid c = AlfrikBoss.Char;
            ZNetView view = AlfrikBoss.View;
            if (c == null || view == null || !view.IsValid() || !view.IsOwner())
            {
                _act = Act.None;
                Resume();
                return;
            }
            Report();
            if (Time.time < _stunUntil)
            {
                _ai = c.GetBaseAI() as MonsterAI;
                Pause();
                c.SetMoveDir(Vector3.zero);
                c.SetRun(false);
                return;
            }
            Rearm(c);
            int state = AlfrikBoss.State;
            bool boss = state == 1 && !AlfrikBoss.InTransition;
            if (state == 1 && AlfrikBoss.InTransition && !c.IsDead())
            {
                Stop();
                _ai = c.GetBaseAI() as MonsterAI;
                Character foe = _ai == null ? null : _ai.GetTargetCreature();
                Vector3 to = foe != null ? Flat(foe.transform.position - c.transform.position) : Vector3.zero;
                Drive(c, Vector3.zero, to.sqrMagnitude > 0.0001f ? to.normalized : c.transform.forward, false);
                return;
            }
            if ((!boss && state != 2) || c.IsDead() || AlfrikBoss.Helpless)
            {
                Stop();
                return;
            }
            _ai = c.GetBaseAI() as MonsterAI;
            _target = _ai == null ? null : _ai.GetTargetCreature();
            KeepAggro(c);
            float dist = _target == null ? float.MaxValue : Flat(_target.transform.position - c.transform.position).magnitude;

            if (Time.time < _rollUntil)
            {
                Drive(c, _rollDir, _rollDir, true);
                return;
            }
            if (_rollCheck)
            {
                // A roll that went nowhere hit something: no rolling for a moment, so he does not roll into a wall again.
                _rollCheck = false;
                if (Flat(c.transform.position - _rollFrom).magnitude < 0.6f)
                    _noRollUntil = Time.time + 3f;
            }
            // The move sequencer: when an attack's animation ends he takes a breath before his next move, so no
            // animation is cut short by the next one (combos, abilities and swings all wait for it).
            bool attacking = c.InAttack();
            if (_wasAttacking && !attacking)
                _recoverUntil = Time.time + (_burstLeft > 0 || _act == Act.TwinChain || _act == Act.FuryChain || _act == Act.Charge ||
                    _act == Act.SweepChain || _act == Act.ExecFollow ? 0.1f : Phase2 ? 0.5f : 0.35f);
            _wasAttacking = attacking;
            bool recovering = Time.time < _recoverUntil;
            // In his own swings the AI is held, so it cannot walk him in and stop him again under the swing; he only
            // keeps turning to the target.
            bool swinging = _act == Act.None && attacking && _target != null;
            // Within 7 m of his target the AI stays paused and his own footwork moves him (Footwork); the AI only chases.
            bool close = boss && _target != null && dist <= 7f;
            if (_act == Act.None && !swinging && !close)
                Resume();
            Speed(boss, dist);
            if (c.IsStaggering())
            {
                EndAct(c);
                return;
            }
            if (boss)
            {
                if (Phase2)
                {
                    Defend2(c);
                    Stomp(c);
                }
                else
                    Defend(c, dist);
                Bloodscent(c);
            }
            KeepMark();
            if (_secondThrowAt > 0f && Time.time >= _secondThrowAt)
            {
                _secondThrowAt = 0f;
                Hatchet.Throw(c, _secondTarget != null && !_secondTarget.IsDead() ? _secondTarget : _target, Hatchet.Left, 0f, true);
            }
            if (_act == Act.None && HatchetFlight.InFlight(c, Hatchet.Right))
                StartFists();
            if (_act != Act.None)
            {
                RunAct(c, dist);
                return;
            }
            if (Retaliate(c, attacking))
                return;
            if (Time.time < _flinchUntil)
            {
                Drive(c, Vector3.zero, Flat(_target != null ? _target.transform.position - c.transform.position : c.transform.forward), false);
                return;
            }
            if (swinging)
            {
                Drive(c, Vector3.zero, Flat(_target.transform.position - c.transform.position), false);
                return;
            }
            if (_target == null || attacking)
                return;
            if (recovering)
            {
                if (close)
                    Footwork(c, dist);
                return;
            }
            if (_afterBurst)
            {
                _afterBurst = false;
                AfterBurst(c);
                return;
            }
            if (Time.time >= _nextAbility && (Phase2 ? Choose2(c, dist) : Choose(c, dist)))
                return;
            if (dist <= 2.4f && Time.time >= _nextShoulder && (_target.IsBlocking() || UnityEngine.Random.value < 0.004f))
            {
                // Phase 2's Shoulder Breaker is the same bash from a shorter step.
                _nextShoulder = Time.time + (Phase2 ? 14f : 10f);
                _shoulderHit = false;
                _shoulderAt = 0f;
                Start(Act.Shoulder, Tell(Phase2 ? 0.3f : 0.45f));
                Plugin.Log.LogInfo(Phase2 ? "Alfrik: Shoulder Breaker" : "Alfrik: Shoulder Charge");
                Face(c, _target);
                return;
            }
            if (dist <= Reach && Time.time >= _nextPressure)
            {
                if (_burstLeft <= 0)
                    _burstLeft = Phase2 ? UnityEngine.Random.Range(1, 3) : UnityEngine.Random.Range(1, 5);
                _nextPressure = Time.time + 0.1f;
                if (Swing(c, false) && --_burstLeft == 0)
                    _afterBurst = true;
            }
            if (close && _act == Act.None && !c.InAttack())
                Footwork(c, dist);
        }

        private static bool _wasAttacking;
        private static float _recoverUntil, _nextFlank, _flankAngle;

        // Footwork near his target: he comes in on a curve to its side or back - often in phase 1, now and then in phase
        // 2 - facing it once close, rather than always straight at its front; a new angle every few seconds. A wall in
        // the way and he comes straight in.
        private static void Footwork(Humanoid c, float dist)
        {
            Vector3 toTarget = Flat(_target.transform.position - c.transform.position);
            Vector3 look = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : c.transform.forward;
            if (Time.time >= _nextFlank)
            {
                _nextFlank = Time.time + UnityEngine.Random.Range(3f, 5f);
                bool flank = UnityEngine.Random.value < (Phase2 ? 0.35f : 0.7f);
                float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                _flankAngle = !flank ? 0f : side * (Phase2 ? UnityEngine.Random.Range(40f, 70f) : UnityEngine.Random.Range(60f, 150f));
            }
            Vector3 front = Flat(_target.transform.forward);
            if (front.sqrMagnitude < 0.0001f)
                front = -look;
            Vector3 spot = _target.transform.position + Quaternion.Euler(0f, _flankAngle, 0f) * front.normalized * (Reach - 0.5f);
            Vector3 toSpot = Flat(spot - c.transform.position);
            if (_flankAngle == 0f || toSpot.magnitude < (_footArrived ? 1.1f : 0.6f))
            {
                _footArrived = _flankAngle != 0f;
                // In step: move in once past his reach, stop only well inside it.
                if (dist > Reach)
                    _footStep = true;
                else if (dist < Reach - 0.7f)
                    _footStep = false;
                Steer(c, _footStep ? look : Vector3.zero, look, false);
                return;
            }
            _footArrived = false;
            // Round the target, not through it: close in, he steps around its side.
            Vector3 around = Vector3.Cross(Vector3.up, look) * -Mathf.Sign(_flankAngle);
            Vector3 move = (toSpot.normalized + (dist < 3.5f ? around * 0.8f : Vector3.zero)).normalized;
            if (!Clear(c, move))
                move = look;
            bool near = dist < 4f;
            Steer(c, move, near ? look : move, !Phase2 && dist > 3f);
        }

        private static bool _footStep, _footArrived;
        private static Vector3 _footDir;

        // Drive, with the move eased so a new direction (or stopping) does not flip his legs from one frame to the next.
        private static void Steer(Humanoid c, Vector3 move, Vector3 look, bool run)
        {
            _footDir = move == Vector3.zero ? Vector3.zero : Vector3.Slerp(_footDir.sqrMagnitude > 0.01f ? _footDir : move, move, Time.deltaTime * 8f);
            Drive(c, _footDir, look, run);
        }

        // The end of a burst of chops is where he could be punished, so he does not stand there: often he rolls or
        // backs off at once, otherwise a short random beat (holding, circling the target or stepping back).
        // Steady beats only (a quick step back and in again reads as a shake): holding his ground or circling.
        private static void AfterBurst(Humanoid c)
        {
            if (Phase2)
            {
                AfterBurst2(c);
                return;
            }
            if (UnityEngine.Random.value < 0.35f && _target != null && Roll(c, _target))
                return;
            float pick = UnityEngine.Random.value;
            if (pick < 0.3f)
                Break(BreakMode.Back, UnityEngine.Random.Range(1f, 1.5f));
            else if (pick < 0.55f)
                Break(BreakMode.Hold, UnityEngine.Random.Range(0.5f, 0.9f));
            else
                Break(BreakMode.Circle, UnityEngine.Random.Range(0.8f, 1.4f));
        }

        // The game's monster AI gives up a target it has not attacked for 60 s (and after 15 s calls it unreachable),
        // counting only its own attacks; his are this file's, so its clock is kept at zero while he fights. A target it
        // dropped anyway is taken back while it is alive and within 40 m.
        private static void KeepAggro(Humanoid c)
        {
            if (_ai == null || AlfrikBoss.State != 1)
                return;
            if (_target == null && _lastFoe != null && !_lastFoe.IsDead() && BaseAI.IsEnemy(c, _lastFoe) &&
                Flat(_lastFoe.transform.position - c.transform.position).magnitude <= 40f)
            {
                TargetOf(_ai) = _lastFoe;
                _target = _lastFoe;
                Plugin.Log.LogInfo("Alfrik's AI dropped " + _lastFoe.GetHoverName() + "; target taken back");
            }
            if (_target == null)
                return;
            _lastFoe = _target;
            SinceAttacking(_ai) = 0f;
            Unreachable(_ai) = 0f;
        }

        private static void Break(BreakMode mode, float seconds)
        {
            _breakMode = mode;
            _side = UnityEngine.Random.value < 0.5f ? 1f : -1f;
            Start(Act.Break, seconds);
        }

        // The phase speed all the time (a steady speed, so it never flickers), the sprint only for a real chase.
        private static void Speed(bool boss, float dist)
        {
            if (_target == null || dist < 6f)
                _chasing = false;
            else if (dist > 10f)
                _chasing = true;
            float speed = boss ? AlfrikBoss.CurrentPhase.Speed : 1f;
            if (_act == Act.Charge)
                speed = 2.2f;
            else if (_act == Act.Shoulder)
                speed = 2.6f;
            else if ((_act == Act.None || _act == Act.Fists) && _chasing)
                speed *= 1.6f;
            if (WarCry)
                speed *= 1.25f;
            AlfrikBoss.SetSpeed(speed);
        }

        private static void Pause()
        {
            if (_paused != null || _ai == null)
                return;
            _paused = _ai;
            _paused.enabled = false;
        }

        private static void Resume()
        {
            if (_paused == null)
                return;
            _paused.enabled = true;
            _paused = null;
        }

        private static void Drive(Humanoid c, Vector3 move, Vector3 look, bool run)
        {
            Pause();
            c.SetMoveDir(move);
            c.SetRun(run);
            if (look.sqrMagnitude > 0.0001f)
                c.SetLookDir(look);
        }

        // Turned to the target at once (turning normally takes a moment, and a leap goes where he faces). Already
        // nearly facing it he just turns the usual way, so he does not snap again and again.
        private static void Face(Humanoid c, Character target)
        {
            if (target == null)
                return;
            Vector3 dir = Flat(target.transform.position - c.transform.position);
            if (dir.sqrMagnitude < 0.0001f)
                return;
            if (Vector3.Angle(Flat(c.transform.forward), dir) < 45f)
            {
                c.SetLookDir(dir.normalized);
                return;
            }
            Quaternion facing = Quaternion.LookRotation(dir.normalized);
            c.transform.rotation = facing;
            Rigidbody body = c.GetComponent<Rigidbody>();
            if (body != null)
                body.rotation = facing;
            c.SetLookDir(dir.normalized);
        }

        private static bool Swing(Humanoid c, bool secondary)
        {
            Face(c, _target);
            // Fjallbrjótr (a sledge) has no heavy attack of its own: its slam is the heavy blow.
            ItemDrop.ItemData weapon = c.GetCurrentWeapon();
            if (secondary && weapon != null && !weapon.HaveSecondaryAttack())
                secondary = false;
            return c.StartAttack(_target, secondary);
        }

        // A swing coming at him (not from a staggered enemy, which he punishes instead). He answers it just before it
        // lands, worked out from the weapon swung, so a slow sledge does not bait him into rolling early: Blade Catch
        // (if it is ready and he is in reach), Wolf Step, or he takes it. Shots (bows, crossbows, staffs, thrown spears)
        // are answered in flight: about half the time he rolls sideways out of their line.
        private static void Defend(Humanoid c, float dist)
        {
            float now = Time.time;
            if (c.InAttack() || _act == Act.Guard || _act == Act.Throw || _act == Act.RavensTell ||
                _act == Act.Shoulder || _incomingAttack == null)
                return;
            if (now >= _nextShotDodge && _incomingProjectile?.Invoke(AlfrikBoss.Npc, null) is Character shooter)
            {
                _nextShotDodge = now + 1.2f;
                if (UnityEngine.Random.value < 0.55f)
                    Roll(c, shooter, true);
                return;
            }
            if (now < _nextDodgeScan)
                return;
            _nextDodgeScan = now + 0.05f;
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
            if (_swingAnswered || now < _swingLands - 0.18f)
                return;
            _swingAnswered = true;
            _nextDodgeScan = now + 0.5f;
            float roll = UnityEngine.Random.value;
            if (_act != Act.Fists && Time.time >= _nextGuard && Flat(threat.transform.position - c.transform.position).magnitude <= 3.6f && roll < 0.6f)
            {
                EndAct(c);
                _target = threat;
                Start(Act.Guard, 1.2f);
                _nextGuard = Time.time + 7f;
                Face(c, threat);
                Fx.Send(Fx.Rune);
                Plugin.Log.LogInfo("Alfrik raises Blade Catch against " + threat.GetHoverName());
            }
            else if (roll < 0.8f)
                Roll(c, threat);
            _nextDodgeScan = Time.time + 1.2f;
        }

        // Vikings for Hire's React, for him: the same timing, but his own roll.
        private static bool React(object __instance, Character threat)
        {
            if (__instance == null || __instance != (object)AlfrikBoss.Npc || AlfrikBoss.State == 0)
                return true;
            Humanoid c = AlfrikBoss.Char;
            if (c != null && threat != null && !c.InAttack() && !c.IsStaggering())
                Roll(c, threat);
            return false;
        }

        // A roll goes to the side or back, whichever way is clear of walls, rocks and buildings for 2.5 m; with no way
        // clear, or after two rolls in 4 s, or just after a roll that got stuck, he does not roll.
        private static bool Roll(Humanoid c, Character threat, bool sideways = false)
        {
            float now = Time.time;
            // The wall of phase 2 does not roll.
            if (Phase2)
                return false;
            if (now < _noRollUntil)
                return false;
            if (now - _rollWindow > 4f)
            {
                _rollWindow = now;
                _rolls = 0;
            }
            if (_rolls >= 2)
                return false;
            Vector3 away = Flat(c.transform.position - threat.transform.position);
            away = away.sqrMagnitude > 0.0001f ? away.normalized : -c.transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, away) * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
            Vector3[] choices = sideways ? new[] { side, -side, (side + away * 0.2f).normalized, (-side + away * 0.2f).normalized }
                : UnityEngine.Random.value < 0.7f
                ? new[] { (side + away * 0.35f).normalized, (-side + away * 0.35f).normalized, away, side, -side }
                : new[] { away, (side + away * 0.35f).normalized, (-side + away * 0.35f).normalized, side, -side };
            Vector3 dir = Vector3.zero;
            foreach (Vector3 choice in choices)
                if (Clear(c, choice))
                {
                    dir = choice;
                    break;
                }
            if (dir == Vector3.zero)
                return false;
            EndAct(c);
            _rolls++;
            _rollFrom = c.transform.position;
            _rollCheck = true;
            _rollDir = dir;
            _rollUntil = now + 0.45f;
            _dodgeUntil?.SetValue(AlfrikBoss.Npc, 0f);
            _invulnerableUntil?.SetValue(AlfrikBoss.Npc, now + 0.35f);
            Quaternion facing = Quaternion.LookRotation(dir);
            c.transform.rotation = facing;
            Rigidbody body = c.GetComponent<Rigidbody>();
            if (body != null)
                body.rotation = facing;
            Drive(c, dir, dir, true);
            Zanim(c)?.SetTrigger("dodge");
            Plugin.Log.LogInfo("Alfrik rolls");
            Fx.Send(Fx.Dodge);
            return true;
        }

        private static bool Clear(Humanoid c, Vector3 dir)
        {
            if (_rollMask == 0)
                _rollMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            Vector3 from = c.transform.position + Vector3.up * 0.6f;
            if (Physics.SphereCast(from, 0.3f, dir, out RaycastHit _, 2.5f, _rollMask, QueryTriggerInteraction.Ignore))
                return false;
            // Ground to land on (no rolling off a ledge).
            return Physics.Raycast(from + dir * 2.4f, Vector3.down, 2.5f, _rollMask, QueryTriggerInteraction.Ignore);
        }

        private static bool Choose(Humanoid c, float dist)
        {
            float now = Time.time;
            if (now >= _nextMark && Enemies(c, 25f).Count >= 2)
            {
                _nextMark = now + 25f;
                Mark(c);
                return true;
            }
            if (now >= _nextRavens && !Hatchet.Flying && Weapons.AxeMesh != null && dist <= 20f)
            {
                _secondTarget = SecondEnemy(c);
                if (dist >= 6f || _secondTarget != null)
                {
                    _nextRavens = now + 35f;
                    _nextThrow = Mathf.Max(_nextThrow, now + 6f);
                    Start(Act.RavensTell, Tell(0.6f));
                    Plugin.Log.LogInfo("Alfrik: Ravens' Flight");
                    Face(c, _target);
                    Trigger(ThrowTrigger());
                    Fx.Send(Fx.Ravens);
                    _nextAbility = now + 0.5f;
                    return true;
                }
            }
            if (dist >= 4.5f && dist <= 9f && now >= _nextCharge)
            {
                Start(Act.Charge, 1.8f);
                Plugin.Log.LogInfo("Alfrik: Charge");
                _nextCharge = now + 8f;
                _chargeDir = Flat(_target.transform.position - c.transform.position).normalized;
                _chargeFrom = c.transform.position;
                Face(c, _target);
                Unblockable(3.5f);
                Fx.Send(Fx.Charge);
            }
            else if (dist >= 7f && dist <= 20f && now >= _nextThrow && !Hatchet.Flying && Weapons.AxeMesh != null)
            {
                Start(Act.Throw, Tell(0.45f));
                Plugin.Log.LogInfo("Alfrik: Flying Hatchet");
                _nextThrow = now + 12f;
                Face(c, _target);
                Trigger(ThrowTrigger());
                Fx.Send(Fx.Rune);
            }
            else if (dist <= 3.6f && now >= _nextTwin && now >= _nextClose && !_target.IsStaggering())
            {
                // Not on a fixed beat: a few seconds of plain swings between chances.
                _nextClose = now + UnityEngine.Random.Range(2.5f, 4.5f);
                Fx.Send(Fx.Rune);
                if (UnityEngine.Random.value < 0.2f)
                {
                    // A feint: the warning, then he circles instead.
                    Break(BreakMode.Circle, 0.8f);
                    _nextTwin = now + 4f;
                    Plugin.Log.LogInfo("Alfrik: Twin Reaper (feint)");
                }
                else
                {
                    Start(Act.TwinTell, Tell(0.4f));
                    _nextTwin = now + 9f;
                    Plugin.Log.LogInfo("Alfrik: Twin Reaper");
                }
            }
            else
                return false;
            _nextAbility = now + 0.5f;
            return true;
        }

        private static void Start(Act act, float duration)
        {
            _act = act;
            _actStart = Time.time;
            _actEnd = Time.time + duration;
            _swings = 0;
            Pause();
        }

        private static void RunAct(Humanoid c, float dist)
        {
            float now = Time.time;
            Vector3 toTarget = _target != null ? Flat(_target.transform.position - c.transform.position).normalized : c.transform.forward;
            switch (_act)
            {
                case Act.Charge:
                    if (_target == null)
                    {
                        EndAct(c);
                        break;
                    }
                    // He bends his run toward the target (90 degrees a second, and only while it is ahead of him), so
                    // he runs it down instead of blindly down the first line.
                    if (_swings == 0 && dist > 2.4f && now < _actEnd && Flat(c.transform.position - _chargeFrom).magnitude < 11f)
                    {
                        if (Vector3.Angle(_chargeDir, toTarget) < 70f)
                            _chargeDir = Vector3.RotateTowards(_chargeDir, toTarget, 90f * Mathf.Deg2Rad * Time.deltaTime, 0f);
                        Drive(c, _chargeDir, _chargeDir, true);
                        break;
                    }
                    // Run past: no swings at the air.
                    if (_swings == 0 && dist > 3.5f)
                    {
                        EndAct(c);
                        break;
                    }
                    Drive(c, Vector3.zero, toTarget, false);
                    if (!c.InAttack() && Time.time >= _recoverUntil && _swings < 2 && Swing(c, false))
                        _swings++;
                    if ((_swings >= 2 && !c.InAttack()) || now >= _actEnd + 1.5f)
                        EndAct(c);
                    break;
                case Act.TwinTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                        Start(Act.TwinChain, 4f);
                    break;
                case Act.TwinChain:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (!c.InAttack() && Time.time >= _recoverUntil && _target != null && _swings < 4)
                    {
                        if (_swings == 3)
                            Unblockable(1.2f);
                        if (Swing(c, false))
                        {
                            if (_swings == 3)
                                Fx.Send(Fx.Slash);
                            _swings++;
                        }
                    }
                    if ((_swings >= 4 && !c.InAttack()) || now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.Guard:
                    Drive(c, Vector3.zero, toTarget, false);
                    Blocking(c) = true;
                    bool theirs = _target is Humanoid swinger && swinger.InAttack();
                    if ((now >= _actEnd && !theirs) || now >= _actEnd + 1f)
                    {
                        EndAct(c);
                        _recoverUntil = now + 0.3f;
                    }
                    break;
                case Act.Break:
                    Vector3 around = Vector3.Cross(Vector3.up, toTarget) * _side;
                    Vector3 step = _breakMode == BreakMode.Circle ? (around + toTarget * 0.15f).normalized
                        : _breakMode == BreakMode.Back ? -toTarget : Vector3.zero;
                    Drive(c, step, toTarget, _breakMode == BreakMode.Circle && !Phase2);
                    if (now >= _actEnd)
                        EndAct(c);
                    break;
                case Act.Throw:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                    {
                        Hatchet.Throw(c, _target);
                        EndAct(c);
                    }
                    break;
                case Act.RavensTell:
                    Drive(c, Vector3.zero, toTarget, false);
                    if (now >= _actEnd)
                    {
                        Hatchet.Throw(c, _target, Hatchet.Right, 0f, true);
                        if (_secondTarget != null && !_secondTarget.IsDead())
                            Hatchet.Throw(c, _secondTarget, Hatchet.Left, 0f, true);
                        else
                        {
                            _secondTarget = _target;
                            _secondThrowAt = now + 0.3f;
                        }
                        StartFists();
                    }
                    break;
                case Act.Fists:
                    Fists(c, dist, toTarget);
                    break;
                case Act.Shoulder:
                    if (now < _actStart + 0.15f)
                    {
                        Drive(c, Vector3.zero, toTarget, false);
                        break;
                    }
                    if (!_shoulderHit && _shoulderAt <= 0f)
                    {
                        Drive(c, toTarget, toTarget, true);
                        if (dist <= 1.9f || now >= _actEnd)
                        {
                            // A real swing, so the bash has an animation; the shove lands with it.
                            Drive(c, Vector3.zero, toTarget, false);
                            Swing(c, false);
                            _shoulderAt = now + 0.3f;
                        }
                    }
                    else if (!_shoulderHit)
                    {
                        Drive(c, Vector3.zero, toTarget, false);
                        if (now >= _shoulderAt)
                            ShoulderHit(c);
                    }
                    else if (!c.InAttack())
                        EndAct(c);
                    break;
                default:
                    RunPhase2Act(c, dist, toTarget);
                    break;
            }
        }

        private static void EndAct(Humanoid c)
        {
            SwingBoost = 1f;
            if (c != null)
            {
                if (_act == Act.Guard || _act == Act.Wall || _act == Act.ParryStance || _act == Act.Advance)
                    Blocking(c) = false;
                // The War Horn's roar is an emote, which holds until it is stopped.
                if (_act == Act.Horn)
                    Trigger("emote_stop");
                // The Charge cannot be blocked only while it lasts; the swings after it can.
                if (_act == Act.Charge)
                    Unblockable(0f);
                // Cut short in the air (his death, the phase change): back on his feet where he is.
                if (_act == Act.Fall)
                    EndLeap(c);
                if (_act != Act.None)
                    c.SetMoveDir(Vector3.zero);
            }
            _act = Act.None;
            if (Time.time >= _rollUntil)
                Resume();
        }

        internal static void Stop()
        {
            _rollUntil = 0f;
            EndAct(AlfrikBoss.Char);
            Resume();
        }

        // The spear throw wind-up from a vanilla spear's secondary attack; a pointing emote if the game renamed it.
        private static string ThrowTrigger()
        {
            if (_throwTrigger == null && ObjectDB.instance != null)
            {
                GameObject spear = ObjectDB.instance.GetItemPrefab("SpearFlint") ?? ObjectDB.instance.GetItemPrefab("SpearBronze");
                string trigger = spear == null ? null : spear.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_secondaryAttack?.m_attackAnimation;
                _throwTrigger = string.IsNullOrEmpty(trigger) ? "emote_point" : trigger;
            }
            return _throwTrigger;
        }

        // Character.RPC_Damage prefix, on the game that owns him: Blade Catch parries a melee hit taken in the guard.
        // The attacker staggers and loses stamina, and his counter cannot be blocked.
        internal static bool OnHit(Character __instance, HitData hit)
        {
            if (hit != null && __instance == AlfrikBoss.Char && hit.GetAttacker() is Character hurter)
            {
                if (Time.time - _hurtSince > 12f)
                {
                    _hurt.Clear();
                    _hurtSince = Time.time;
                }
                _hurt.TryGetValue(hurter, out float sum);
                _hurt[hurter] = sum + hit.GetTotalDamage();
            }
            if (hit == null || __instance != AlfrikBoss.Char)
                return true;
            // Retribution: a melee hit close by is answered with a sweep as soon as he is free (Think).
            if (Retribution && !hit.m_ranged && hit.GetAttacker() is Character striker &&
                Flat(striker.transform.position - __instance.transform.position).magnitude <= 4.5f)
                _retaliate = striker;
            if (_act == Act.Wall || _act == Act.Advance)
                return WallHit(hit);
            if ((_act != Act.Guard && _act != Act.ParryStance) || hit.m_ranged)
                return true;
            ZNetView view = AlfrikBoss.View;
            Character attacker = hit.GetAttacker();
            if (view == null || !view.IsOwner() || attacker == null)
                return true;
            Humanoid c = AlfrikBoss.Char;
            bool punishing = _act == Act.ParryStance;
            EndAct(c);
            Fx.Send(Fx.Parry);
            Plugin.Log.LogInfo((punishing ? "Alfrik's Punishing Parry caught " : "Alfrik's Blade Catch parried ") + attacker.GetHoverName());
            attacker.Stagger(Flat(attacker.transform.position - c.transform.position).normalized);
            if (attacker is Player player)
                player.UseStamina(ParryStaminaBreak);
            Unblockable(1.5f);
            _target = attacker;
            _heavyUntil = Time.time + 0.3f;
            Swing(c, true);
            _nextAbility = Time.time + 0.5f;
            return false;
        }

        private static readonly int StateKey = "sc_alfrik".GetStableHashCode();
        private static readonly int PhaseKey = "sc_alfrik_phase".GetStableHashCode();
        private static Character _crushVictim, _fistVictim;
        private static Vector3 _crushDir;
        private static float _healthBefore;

        private static ZDO BossZdo(Character attacker)
        {
            ZNetView view = attacker == null ? null : attacker.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && zdo.GetInt(StateKey, 0) == 1 ? zdo : null;
        }

        // Humanoid.BlockAttack prefix, on the game of whoever blocks him as a boss: inside an unblockable window (the
        // Charge, the Twin Reaper cross, his counter after Blade Catch) the block fails. Otherwise blocking and parrying
        // work as usual; whether it was a perfect parry is noted for the postfix.
        private static bool BlockingHim(Humanoid __instance, Character attacker, ref bool __result, out bool __state)
        {
            __state = false;
            ZDO zdo = BossZdo(attacker);
            if (zdo == null)
                return true;
            // Skeggöld (phase 2) cannot be blocked or parried: the guard breaks, the blocker staggers on the spot and
            // the hit lands in full.
            if (zdo.GetInt(PhaseKey, 1) == 2)
            {
                Vector3 away = Flat(__instance.transform.position - attacker.transform.position);
                __instance.Stagger(away.sqrMagnitude > 0.0001f ? away.normalized : -__instance.transform.forward);
                Fx.SendAt(Fx.GuardBreak, __instance.GetCenterPoint());
                __result = false;
                return false;
            }
            if (ZNet.instance != null && zdo.GetLong(UnblockableKey, 0L) > ZNet.instance.GetTime().Ticks)
            {
                __result = false;
                return false;
            }
            float timer = BlockTimer(__instance);
            bool fist = ZNet.instance != null && zdo.GetLong(FistKey, 0L) > ZNet.instance.GetTime().Ticks;
            __state = timer != -1f && timer < (fist ? 0.18f : 0.25f);
            // A punch has a tighter perfect-parry window: later than that it is a plain block.
            if (fist && !__state && timer != -1f && timer < 0.25f)
                BlockTimer(__instance) = 0.25f;
            // A perfect parry of his swing never staggers the one who parries (LessStagger), or the game would count
            // it as a broken guard: no parry, a stagger and the full hit.
            _perfectParrier = __state ? __instance : null;
            return true;
        }

        // A perfect parry interrupts his combo and staggers him briefly (see OnFlinch); a plain
        // block costs 10 more stamina, so holding block through his bursts breaks the guard.
        private static void BlockedHim(Humanoid __instance, Character attacker, bool __result, bool __state)
        {
            _perfectParrier = null;
            if (!__result || BossZdo(attacker) == null)
                return;
            Plugin.Log.LogInfo(__state ? "Alfrik's swing parried" : "Alfrik's swing blocked");
            if (__state)
                SendFlinch(attacker);
            else
                __instance.UseStamina(10f);
        }

        private const string FlinchRpc = "Alfrik_Flinch";

        internal static void Register()
        {
            ZRoutedRpc.instance?.Register<ZDOID>(FlinchRpc, OnFlinch);
            ZRoutedRpc.instance?.Register<ZDOID, float>(MarkRpc, OnMark);
        }

        // ---- The Left Fist ----

        // The Left Fist lasts until he has his axes back: after Ravens' Flight they are stuck in the ground where they
        // landed. He fights with his fists for 6-9 s (or less if the target gets away), then goes and pulls them out,
        // Hugr first unless Hamr is nearer; with Hugr in his hand again he stops punching. An axe he cannot reach flies
        // back to him after 40 s.
        private static float _fetchAt, _stunUntil, _stunLength = 1.3f, _shoulderAt;

        private static void StartFists()
        {
            Start(Act.Fists, 40f);
            _fetchAt = Time.time + UnityEngine.Random.Range(6f, 9f);
            _punches = 0;
            _nextPunch = Time.time + 0.3f;
        }

        private static void Fists(Humanoid c, float dist, Vector3 toTarget)
        {
            float now = Time.time;
            if (!HatchetFlight.InFlight(c) && _secondThrowAt <= 0f)
            {
                EndAct(c);
                return;
            }
            if (now >= _actEnd)
            {
                // Once: the axes he never reached fly back.
                Hatchet.Recall(c);
                _actEnd = now + 30f;
            }
            bool unarmed = HatchetFlight.InFlight(c, Hatchet.Right);
            bool rightStuck = HatchetFlight.Stuck(c, Hatchet.Right, out Vector3 rightAt);
            bool leftStuck = HatchetFlight.Stuck(c, Hatchet.Left, out Vector3 leftAt);
            // Going for his axes: after his time with the fists, when the target is far or gone, or once Hugr is back.
            if ((rightStuck || leftStuck) && (now >= _fetchAt || _target == null || dist > 9f || !unarmed))
            {
                int hand = rightStuck && (!leftStuck ||
                    Flat(rightAt - c.transform.position).sqrMagnitude <= Flat(leftAt - c.transform.position).sqrMagnitude)
                    ? Hatchet.Right : Hatchet.Left;
                Vector3 to = Flat((hand == Hatchet.Right ? rightAt : leftAt) - c.transform.position);
                if (to.magnitude <= 1.4f)
                {
                    Hatchet.Pickup(c, hand);
                    Plugin.Log.LogInfo("Alfrik pulls " + (hand == Hatchet.Right ? "Hugr" : "Hamr") + " out of the ground");
                    Drive(c, Vector3.zero, to.sqrMagnitude > 0.0001f ? to.normalized : c.transform.forward, false);
                    return;
                }
                Drive(c, to.normalized, to.normalized, true);
                if (unarmed)
                    Punch(c, dist, now);
                return;
            }
            if (_target == null)
            {
                Drive(c, Vector3.zero, c.transform.forward, false);
                return;
            }
            Drive(c, dist > 1.5f ? toTarget : Vector3.zero, toTarget, dist > 1.5f);
            if (unarmed)
                Punch(c, dist, now);
        }

        // The Left Fist: his axe is put away, so the game's own unarmed punch (and, every third, the kick) plays and
        // lands with its animation; a punch or kick hits harder than a bare fist would (SwingBoost).
        private static void Punch(Humanoid c, float dist, float now)
        {
            if (_target == null || c.InAttack() || dist > 2.2f || now < _nextPunch)
                return;
            Unarm(c);
            _hook = (_punches + 1) % 3 == 0;
            Face(c, _target);
            SwingBoost = _hook ? 4.5f : 3f;
            if (_hook)
                _heavyUntil = now + 0.3f;
            FistWindow(1f);
            if (!c.StartAttack(_target, _hook))
                return;
            _punches++;
            _nextPunch = now + (_hook ? Tell(1.2f) : Tell(0.7f));
            Plugin.Log.LogInfo("Alfrik: Left Fist " + (_hook ? "kick" : "punch") + " " + _punches);
        }

        // Hugr & Hamr put away while his hands are empty, taken up again once his fists are done (Rearm).
        private static ItemDrop.ItemData _stowed;

        private static void Unarm(Humanoid c)
        {
            ItemDrop.ItemData weapon = c.GetCurrentWeapon();
            if (weapon == null || weapon.m_dropPrefab == null || weapon.m_dropPrefab.name != Weapons.HugrHamr)
                return;
            _stowed = weapon;
            c.UnequipItem(weapon, false);
        }

        private static void Rearm(Humanoid c)
        {
            if (_stowed == null || _act == Act.Fists || HatchetFlight.InFlight(c, Hatchet.Right))
                return;
            if (!c.IsItemEquiped(_stowed) && c.GetInventory().ContainsItem(_stowed))
                c.EquipItem(_stowed, false);
            _stowed = null;
        }

        // Synced: while a punch is on its way, the blocker's game uses the tighter parry window.
        private static void FistWindow(float seconds)
        {
            ZNetView view = AlfrikBoss.View;
            if (view != null && view.IsValid() && ZNet.instance != null)
                view.GetZDO().Set(FistKey, ZNet.instance.GetTime().AddSeconds(seconds).Ticks);
        }

        // ---- Catch & Cleave, War Cry ----

        // An axe of his came back (HatchetFlight, on the game that owns him).
        internal static void Caught(int hand, Vector3 at, bool caught)
        {
            Humanoid c = AlfrikBoss.Char;
            ZNetView view = AlfrikBoss.View;
            if (c == null || view == null || !view.IsValid() || !view.IsOwner() || c.IsDead())
                return;
            if (caught)
            {
                Fx.SendAt(Fx.Catch, at);
                Character near = Nearest(c, 3f);
                if (near != null && !c.InAttack())
                {
                    _target = near;
                    Face(c, near);
                    if (c.StartAttack(near, false))
                        Fx.Send(Fx.Cleave);
                }
            }
            if (_act == Act.Fists && !HatchetFlight.InFlight(c) && _secondThrowAt <= 0f)
            {
                EndAct(c);
                _warCryUntil = Time.time + 6f;
                Plugin.Log.LogInfo("Alfrik: War Cry");
                Fx.Send(Fx.WarCry);
            }
        }

        // ---- Shoulder Charge ----

        private static void ShoulderHit(Humanoid c)
        {
            _shoulderHit = true;
            Unblockable(0.4f);
            foreach (Character enemy in InFront(c, 2.2f, 70f))
            {
                Vector3 dir = Flat(enemy.transform.position - c.transform.position).normalized;
                var hit = new HitData();
                hit.m_damage.m_blunt = 25f;
                hit.ApplyModifier(c.GetRandomSkillFactor(Skills.SkillType.Unarmed));
                hit.m_point = enemy.GetCenterPoint();
                hit.m_dir = dir;
                hit.m_pushForce = 120f;
                hit.m_staggerMultiplier = 3f;
                hit.m_blockable = Phase2;
                hit.m_dodgeable = true;
                hit.m_skill = Skills.SkillType.Unarmed;
                hit.SetAttacker(c);
                enemy.Damage(hit);
                enemy.Stagger(dir);
                Fx.SendAt(Fx.Shoulder, enemy.GetCenterPoint());
            }
        }

        // ---- Huginn's Mark ----

        private const string MarkRpc = "Alfrik_Mark";
        private const float MarkSeconds = 8f;

        private static void Mark(Humanoid c)
        {
            Plugin.Log.LogInfo("Alfrik: Huginn's Mark");
            Character mark = null;
            float most = 0f;
            foreach (KeyValuePair<Character, float> pair in _hurt)
                if (pair.Key != null && !pair.Key.IsDead() && pair.Value > most && Vector3.Distance(pair.Key.transform.position, c.transform.position) < 30f)
                {
                    most = pair.Value;
                    mark = pair.Key;
                }
            if (mark == null)
            {
                float far = 0f;
                foreach (Character enemy in Enemies(c, 25f))
                {
                    float d = Vector3.Distance(enemy.transform.position, c.transform.position);
                    if (d > far)
                    {
                        far = d;
                        mark = enemy;
                    }
                }
            }
            ZNetView view = mark == null ? null : mark.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || ZRoutedRpc.instance == null)
                return;
            _marked = mark;
            _markUntil = Time.time + MarkSeconds;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, MarkRpc, view.GetZDO().m_uid, MarkSeconds);
        }

        private static void KeepMark()
        {
            if (_marked == null)
                return;
            if (Time.time > _markUntil || _marked.IsDead())
            {
                _marked = null;
                return;
            }
            if (_ai != null && Time.time >= _nextMarkAim)
            {
                _nextMarkAim = Time.time + 0.5f;
                TargetOf(_ai) = _marked;
            }
        }

        private static void OnMark(long sender, ZDOID id, float seconds)
        {
            GameObject instance = ZNetScene.instance == null ? null : ZNetScene.instance.FindInstance(id);
            Character marked = instance == null ? null : instance.GetComponent<Character>();
            if (marked != null)
                MarkSigil.Show(marked, seconds);
        }

        // ---- Bloodscent ----

        private static void Bloodscent(Humanoid c)
        {
            if (_bloodUsed || c.GetHealthPercentage() >= 0.5f)
                return;
            _bloodUsed = true;
            AlfrikBoss.View.GetZDO().Set(BloodKey, true);
            Fx.Send(Fx.Bloodscent);
        }

        // A new phase (or the end of the fight): Bloodscent can come again.
        internal static void NewPhase()
        {
            // Ravens' Flight (and so the fists) not in the first 20 s of a phase.
            _nextRavens = Time.time + 20f;
            _bloodUsed = false;
            NewPhase2();
            // Axes left stuck in the ground come back to him.
            if (AlfrikBoss.Char != null)
                Hatchet.Recall(AlfrikBoss.Char);
            ZNetView view = AlfrikBoss.View;
            if (view != null && view.IsValid() && view.IsOwner())
                view.GetZDO().Set(BloodKey, false);
        }

        // ---- Who is around ----

        private static List<Character> Enemies(Humanoid c, float range)
        {
            var list = new List<Character>();
            foreach (Character other in Character.GetAllCharacters())
                if (other != null && other != c && !other.IsDead() && BaseAI.IsEnemy(c, other) &&
                    Vector3.Distance(other.transform.position, c.transform.position) <= range)
                    list.Add(other);
            return list;
        }

        // Another enemy for the second axe, the farthest one but his target (so someone hanging back gets it).
        private static Character SecondEnemy(Humanoid c)
        {
            Character best = null;
            float far = 0f;
            foreach (Character enemy in Enemies(c, 20f))
            {
                float d = Vector3.Distance(enemy.transform.position, c.transform.position);
                if (enemy != _target && d >= 3f && d > far)
                {
                    far = d;
                    best = enemy;
                }
            }
            return best;
        }

        private static Character Nearest(Humanoid c, float range)
        {
            Character best = null;
            float near = range;
            foreach (Character enemy in Enemies(c, range))
            {
                float d = Vector3.Distance(enemy.transform.position, c.transform.position);
                if (d <= near)
                {
                    near = d;
                    best = enemy;
                }
            }
            return best;
        }

        private static List<Character> InFront(Humanoid c, float range, float angle)
        {
            var list = new List<Character>();
            foreach (Character enemy in Enemies(c, range + 0.5f))
            {
                Vector3 to = Flat(enemy.transform.position - c.transform.position);
                if (to.magnitude <= range + enemy.GetRadius() && Vector3.Angle(c.transform.forward, to) <= angle)
                    list.Add(enemy);
            }
            return list;
        }

        private static void SendFlinch(Character boss)
        {
            ZNetView view = boss.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && ZRoutedRpc.instance != null)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, FlinchRpc, view.GetZDO().m_uid);
        }

        // On the game that owns him: the parried combo is cut off and he staggers (the game's own short stagger), on
        // every perfect parry.
        private static void OnFlinch(long sender, ZDOID id)
        {
            ZNetView view = AlfrikBoss.View;
            Humanoid c = AlfrikBoss.Char;
            if (view == null || !view.IsValid() || !view.IsOwner() || view.GetZDO().m_uid != id || c == null)
                return;
            EndAct(c);
            _burstLeft = 0;
            _afterBurst = false;
            _flinchUntil = Time.time + 0.5f;
            if (!c.IsStaggering())
            {
                _parryStagger = true;
                c.Stagger(-c.transform.forward);
                _parryStagger = false;
            }
        }

        // When a swing lands, by the weapon swung: quick blades soon, two-handed weapons and area slams late.
        private static float HitDelay(Character attacker, Attack swing)
        {
            ItemDrop.ItemData weapon = (attacker as Humanoid)?.GetCurrentWeapon();
            if (weapon == null)
                return 0.35f;
            float delay;
            switch (weapon.m_shared.m_skillType)
            {
                case Skills.SkillType.Knives: delay = 0.22f; break;
                case Skills.SkillType.Unarmed: delay = 0.25f; break;
                case Skills.SkillType.Swords: delay = 0.35f; break;
                case Skills.SkillType.Spears: delay = 0.35f; break;
                case Skills.SkillType.Axes: delay = 0.38f; break;
                case Skills.SkillType.Clubs: delay = 0.42f; break;
                case Skills.SkillType.Polearms: delay = 0.5f; break;
                case Skills.SkillType.Pickaxes: delay = 0.5f; break;
                default: delay = 0.4f; break;
            }
            ItemDrop.ItemData.ItemType type = weapon.m_shared.m_itemType;
            if (type == ItemDrop.ItemData.ItemType.TwoHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft)
                delay += 0.2f;
            if (swing != null && swing.m_attackType == Attack.AttackType.Area)
                delay += 0.1f;
            return delay;
        }

        // Character.RPC_Stagger prefix, on the game that owns him: as a boss a perfect parry staggers him, and so does a
        // full stagger bar (hits fill it at 40%, AlfrikBoss.StaggerBonus), but that at most once every 8 s.
        private static bool NoStagger(Character __instance)
        {
            if (__instance != AlfrikBoss.Char || AlfrikBoss.State != 1)
                return true;
            // Phase 2: nothing staggers him.
            if (Phase2)
                return false;
            if (!_parryStagger && Time.time < _nextStagger)
                return false;
            if (!__instance.IsStaggering())
            {
                _nextStagger = Time.time + 8f;
                _stunLength = _parryStagger ? 1.6f : 1.3f;
                _stunUntil = Time.time + _stunLength;
                EndAct(AlfrikBoss.Char);
                Fx.Send(Fx.Stunned);
                _staggers++;
                Plugin.Log.LogInfo(_parryStagger ? "Alfrik staggered by a parry" : "Alfrik staggered by his stagger bar");
            }
            return true;
        }

        // Character.RPC_Damage prefix/postfix, on the game of whoever he hits: his hits fill their stagger bar by
        // [Boss] StaggerOnYou (Character.AddStaggerDamage prefix), blocked or not.
        private static void HitBy(Character __instance, HitData hit)
        {
            ZDO boss = hit == null ? null : BossZdo(hit.GetAttacker());
            _staggerVictim = boss != null ? __instance : null;
            // Phase 2: any blow of his that carries no status effect of its own (War Horn's carries its slow).
            _crushVictim = boss != null && boss.GetInt(PhaseKey, 1) == 2 && hit.m_statusEffectHash == 0 ? __instance : null;
            _crushDir = hit != null ? hit.m_dir : Vector3.zero;
            _healthBefore = __instance != null ? __instance.GetHealth() : 0f;
            _fistVictim = boss != null && boss.GetInt(PhaseKey, 1) == 1 && hit.m_skill == Skills.SkillType.Unarmed ? __instance : null;
        }

        // Phase 2: a blow of his that hurt (the health went down) leaves the one hit stunned - staggered every time.
        private static void HitDone(Character __instance)
        {
            if (_crushVictim != null && __instance == _crushVictim && !__instance.IsDead() && __instance.GetHealth() < _healthBefore - 0.5f)
            {
                Fx.SendAt(Fx.Hit2, __instance.GetCenterPoint());
                Vector3 dir = Flat(_crushDir);
                __instance.Stagger(dir.sqrMagnitude > 0.0001f ? dir.normalized : -__instance.transform.forward);
            }
            // His fists land with their own sound.
            if (_fistVictim != null && __instance == _fistVictim && __instance.GetHealth() < _healthBefore - 0.5f)
                Fx.SendAt(Fx.Punch, __instance.GetCenterPoint());
            _staggerVictim = _crushVictim = _fistVictim = null;
        }

        private static void LessStagger(Character __instance, ref float damage)
        {
            if (__instance != null && __instance == _perfectParrier)
                damage = 0f;
            else if (__instance == _staggerVictim)
                damage *= Plugin.StaggerOnYou.Value;
        }

        // Humanoid.StartAttack prefix: the dual axes' heavy attack is a leap toward wherever he faces, which misses a
        // moving target, so he only leaps at an enemy that is staggered within 4.5 m (his punish). Any other heavy
        // attack, his own or the AI's, becomes a normal chop.
        private static void NoBlindLeap(Humanoid __instance, Character target, ref bool secondaryAttack)
        {
            if (!secondaryAttack || __instance == null || __instance != AlfrikBoss.Char)
                return;
            bool punish = target != null && target.IsStaggering() &&
                Flat(target.transform.position - __instance.transform.position).magnitude <= 4.5f;
            if (!punish && Time.time >= _heavyUntil)
                secondaryAttack = false;
        }

        // Humanoid.StartAttack postfix, on the game that owns him: each Skeggöld swing whooshes (its own sounds are gone).
        private static void Swung(Humanoid __instance, bool __result)
        {
            if (__result && __instance == AlfrikBoss.Char && Phase2 && AlfrikBoss.View != null && AlfrikBoss.View.IsOwner())
                Fx.Send(Fx.Swing2);
        }

        // Only real changes count (EquipItem on what he already holds does nothing); the first few say who did it.
        private static void Equipped(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (__instance != AlfrikBoss.Char || !__result)
                return;
            _equips++;
            if (_equipTraces++ < 6)
                Plugin.Log.LogInfo("Alfrik equipped " + item?.m_shared.m_name + " from: " + Callers());
        }

        private static string Callers()
        {
            var frames = new System.Diagnostics.StackTrace(2).GetFrames() ?? new System.Diagnostics.StackFrame[0];
            var names = new List<string>();
            foreach (var frame in frames)
            {
                MethodBase method = frame.GetMethod();
                if (method == null)
                    continue;
                names.Add((method.DeclaringType?.Name ?? "?") + "." + method.Name);
                if (names.Count == 5)
                    break;
            }
            return string.Join(" < ", names);
        }

        // Diagnostics, while he swings: how often his turning reverses, his body steps back and forth, or his animation
        // speed changes - the kinds of shaking there are - so the log says which one it is.
        private static void Watch(Humanoid c)
        {
            if (c == null)
                return;
            Trace(c);
            if (!c.InAttack())
            {
                _watchYaw = float.NaN;
                return;
            }
            float yaw = c.transform.eulerAngles.y;
            Vector3 pos = c.transform.position;
            Animator animator = Poses.AnimatorOf(c);
            float speed = animator != null ? animator.speed : 1f;
            if (!float.IsNaN(_watchYaw))
            {
                _swingFrames++;
                float turn = Mathf.DeltaAngle(_watchYaw, yaw);
                if (Mathf.Abs(turn) > 1f && Mathf.Abs(_watchTurn) > 1f && Mathf.Sign(turn) != Mathf.Sign(_watchTurn))
                    _turnFlips++;
                if (Mathf.Abs(turn) > 1f)
                    _watchTurn = turn;
                Vector3 step = Flat(pos - _watchPos);
                if (step.magnitude > 0.01f && _watchStep.magnitude > 0.01f && Vector3.Dot(step, _watchStep) < 0f)
                    _stepFlips++;
                if (step.magnitude > 0.01f)
                    _watchStep = step;
                if (Mathf.Abs(speed - _watchSpeed) > 0.01f)
                    _speedChanges++;
            }
            _watchYaw = yaw;
            _watchPos = pos;
            _watchSpeed = speed;
        }

        // Frame by frame for 0.7 s after a swing ends (a few times a session): what moves him there and how.
        private static void Trace(Humanoid c)
        {
            bool swinging = c.InAttack();
            float now = Time.time;
            if (_wasSwinging && !swinging && now >= _nextTrace && _traces < 4)
            {
                _traces++;
                _nextTrace = now + 20f;
                _traceUntil = now + 0.7f;
                _tracePos = c.transform.position;
                _traceYaw = c.transform.eulerAngles.y;
                Plugin.Log.LogInfo("Alfrik after-swing trace " + _traces + " (t, act, ai on, in attack, move dir fwd/side, " +
                    "moved fwd/side cm, turned deg, anim speed, anim state):");
            }
            _wasSwinging = swinging;
            if (now >= _traceUntil)
                return;
            Transform body = c.transform;
            Vector3 moved = Flat(body.position - _tracePos);
            Vector3 move = MoveDirOf(c);
            Animator animator = Poses.AnimatorOf(c);
            Animator vrm = VikingVrmAvatarCore.Avatar != null ? VikingVrmAvatarCore.Avatar.GetComponent<Animator>() : null;
            Transform hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            Transform vrmHips = vrm != null && vrm.isHuman ? vrm.GetBoneTransform(HumanBodyBones.Hips) : null;
            Vector3 hipsAt = hips != null ? body.InverseTransformPoint(hips.position) : Vector3.zero;
            Vector3 vrmHipsAt = vrmHips != null ? body.InverseTransformPoint(vrmHips.position) : Vector3.zero;
            Plugin.Log.LogInfo(string.Format("    rig: hips {0:0.000} {1:0.000} {2:0.000} | avatar hips {3:0.000} {4:0.000} {5:0.000} | pose {6:0.00}",
                hipsAt.x, hipsAt.y, hipsAt.z, vrmHipsAt.x, vrmHipsAt.y, vrmHipsAt.z, Poses.Weight));
            Plugin.Log.LogInfo(string.Format("  {0:0.00} {1} {2} {3} move {4:0.00}/{5:0.00} moved {6:0.0}/{7:0.0} turn {8:0.0} speed {9:0.00} state {10}",
                0.7f - (_traceUntil - now), _act, _ai != null && _ai.enabled, swinging,
                Vector3.Dot(move, body.forward), Vector3.Dot(move, body.right),
                Vector3.Dot(moved, body.forward) * 100f, Vector3.Dot(moved, body.right) * 100f,
                Mathf.DeltaAngle(_traceYaw, body.eulerAngles.y), animator != null ? animator.speed : 1f,
                animator != null ? animator.GetCurrentAnimatorStateInfo(0).shortNameHash : 0));
            _tracePos = body.position;
            _traceYaw = body.eulerAngles.y;
        }

        private static void Report()
        {
            if (Time.time < _nextReport)
                return;
            _nextReport = Time.time + 15f;
            if (_equips > 0)
                Plugin.Log.LogInfo("Alfrik re-equipped " + _equips + " times in 15 s");
            if (_swingFrames > 0)
                Plugin.Log.LogInfo("Alfrik swings, 15 s: " + _swingFrames + " frames, turn reversals " + _turnFlips + ", back-and-forth steps " +
                    _stepFlips + ", animation speed changes " + _speedChanges + ", stagger bar " + _staggers + " stagger(s)");
            _equips = _swingFrames = _turnFlips = _stepFlips = _speedChanges = _staggers = 0;
        }
    }
}
