using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Alfrik
{
    // The avatar's viking is a boss before he is a companion. ZDO "sc_alfrik" holds his state:
    //  1 boss: a Vikings for Hire outlaw (its own ConfigureRoadBandit) with the boss health bar, fought in phases. Every
    //    phase has its own full health bar; when it empties he is untouchable for a few seconds, then the next phase
    //    starts at full health with its own weapon, speed and abilities (Brain). He never dies and never rolls Vikings for
    //    Hire's random surrender; when the last phase's bar empties he yields.
    //  3 yielding: kneels (Vikings for Hire's Surrender), takes no damage, and after a few seconds joins the player who
    //    beat him (JoinPlayer, as when taking a surrendered outlaw, minus the outlaw betrayal roll).
    //  2 companion: a Berserker who holds the front line, taunting enemies near him onto himself, with a combat-only menu.
    // He never wears armour (his armour is hidden in his stats, so nothing clips through the avatar). Owner-side logic
    // runs on the game that owns his ZDO; the health bar, names, aura and messages on every game.
    internal static class AlfrikBoss
    {
        internal const string Title = "The Left Fist of Odin";

        // In the fight he goes by the name of the phase (The Berserker, The Unbroken); The Left Fist of Odin is the
        // title he carries as a companion.
        internal static string ShownTitle => _state == 1 && CurrentPhase != null ? CurrentPhase.Name : Title;
        internal const string Quote = "\"He's Big. He's Bad. He's Ugly. The Berserker. The Destroyer. The Left Fist of Odin. " +
            "Gods help us all.\"\n- Olaf the Storyteller";
        private const int Berserker = 6;
        private const float TransitionTime = 5.2f, RageAt = 0.5f, TauntRadius = 12f;

        internal sealed class BossPhase
        {
            internal string Name, Weapon;
            internal float Speed = 1f;
        }

        // Phase 1, The Berserker: Hugr & Hamr. A duelist: speed, pressure, dodging, parrying (Brain), steam and a red eye (Aura).
        // Phase 2, The Unbroken: Skeggöld. A wall that counterattacks, with wide sweeps and area blows (Brain.Phase2),
        // red veins and glowing runes (Aura).
        internal static readonly BossPhase[] Phases =
        {
            new BossPhase { Name = "The Berserker", Weapon = Weapons.HugrHamr, Speed = 1.3f },
            new BossPhase { Name = "The Unbroken", Weapon = Weapons.Skeggold, Speed = 0.8f },
        };

        private static readonly string[] Numerals = { "I", "II", "III", "IV" };

        private static readonly int StateKey = "sc_alfrik".GetStableHashCode();
        private static readonly int PhaseKey = "sc_alfrik_phase".GetStableHashCode();
        private static readonly int RageKey = "sc_alfrik_rage".GetStableHashCode();
        private static readonly int WinnerKey = "sc_alfrik_winner".GetStableHashCode();
        private static readonly int WinnerNameKey = "sc_alfrik_winner_name".GetStableHashCode();
        private static readonly int BornKey = "vnpc_born".GetStableHashCode();
        private static readonly int HermitKey = "vnpc_hermit".GetStableHashCode();
        private static readonly int SmithKey = "vnpc_smith".GetStableHashCode();
        private static readonly int FormerOutlawKey = "vnpc_former_outlaw".GetStableHashCode();
        private static readonly int BetrayAtKey = "vnpc_betray_at".GetStableHashCode();
        private static readonly int CombatRoleKey = "vnpc_combat_role".GetStableHashCode();
        private static readonly int DownedKey = "vnpc_downed".GetStableHashCode();
        private static readonly int SurrenderedKey = "vnpc_surrendered".GetStableHashCode();
        private static readonly int TraitsKey = "vnpc_traits".GetStableHashCode();
        private static readonly int LevelKey = "vnpc_level".GetStableHashCode();
        private static readonly int LevelledKey = "sc_alfrik_levelled".GetStableHashCode();
        private static readonly int PartyKey = "sc_alfrik_party".GetStableHashCode();
        private const int JoinAtLevel = 10;

        // Owner: a defeated boss is no green recruit - he joins at level 10 (Vikings for Hire's own level, which sets his
        // health and damage), or its max level if that is lower; never lowered if he is already past it.
        private static void JoinLevel(ZDO zdo)
        {
            int max = JoinAtLevel;
            var maxLevel = AccessTools.Field(AccessTools.TypeByName("ValheimNpc.Plugin"), "MaxLevel")?.GetValue(null) as BepInEx.Configuration.ConfigEntry<int>;
            if (maxLevel != null)
                max = Mathf.Min(max, maxLevel.Value);
            int now = zdo.GetInt(LevelKey, 1);
            if (now < max)
                zdo.Set(LevelKey, max);
            zdo.Set(LevelledKey, true);
            Plugin.Log.LogInfo(_name + " joins at level " + Mathf.Max(now, max) + " (was " + now + ")");
        }

        // Vikings for Hire traits (VikingTrait numbers): Sturdy 1, Strong 2, Berserker 4, Veteran 20, Brave 21, Loyal 30,
        // Northerner 32 - tough, hits hard, stronger below half health, never flees, steadies the squad, stays loyal.
        private const string Traits = "1,2,4,20,21,30,32";

        // Best first; each needs that many bosses defeated. A fallback when his own weapon is missing.
        private static readonly (string Prefab, int Bosses)[] Hammers =
        {
            ("BWA_FlametalHammer", 6), ("BWA_ElvenHammer", 5), ("VASilverSledge", 3), ("BWA_BattleHammer", 2),
            ("VABronzeSledge", 0), ("BWA_BronzeHammer", 0), ("BWA_GiantMace", 0),
        };
        private static readonly ItemDrop.ItemData.ItemType[] Armour =
        {
            ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility, ItemDrop.ItemData.ItemType.Shield,
        };
        private static readonly string[] CombatOptions =
        {
            "$vnpc_opt_to_squad", "$vnpc_order_stay", "$vnpc_order_follow", "$vnpc_opt_backpack", "$vnpc_opt_leave",
        };

        private static string _name;
        private static MethodInfo _configureBandit, _surrender, _joinPlayer, _updateMaxHealth, _bossesDefeated;
        internal static MethodInfo DamageMultiplier;
        private static FieldInfo _surrenderRolled, _gearSignature, _optionLabel;
        private static readonly AccessTools.FieldRef<Character, HitData> LastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
        private static readonly AccessTools.FieldRef<MonsterAI, Character> TargetOf = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");

        private static MonoBehaviour _npc;
        private static Humanoid _char;
        private static ZNetView _view;
        private static int _state, _phase, _lastState = -1, _lastPhase;
        private static bool _rage, _quoteShown, _transitionPending;
        private static float _joinAt, _nextTaunt, _nextShout, _transitionUntil;
        private static float _baseRun, _baseSpeed, _baseWalk, _speedFactor = 1f;

        internal static MonoBehaviour Npc => _npc;
        internal static Humanoid Char => _char;
        internal static ZNetView View => _view;
        internal static int State => _state;
        internal static int PhaseNumber => _phase;
        internal static BossPhase CurrentPhase => _state == 1 ? Phases[Mathf.Clamp(_phase, 1, Phases.Length) - 1] : null;
        internal static bool InTransition => _transitionPending || Time.time < _transitionUntil;

        internal static bool Helpless =>
            _view == null || !_view.IsValid() || _view.GetZDO().GetBool(DownedKey, false) || _view.GetZDO().GetBool(SurrenderedKey, false);

        internal static void Apply(Harmony harmony, Type viking, string name)
        {
            _name = name;
            _configureBandit = AccessTools.Method(viking, "ConfigureRoadBandit");
            _surrender = AccessTools.Method(viking, "Surrender");
            _joinPlayer = AccessTools.Method(viking, "JoinPlayer");
            _updateMaxHealth = AccessTools.Method(viking, "UpdateMaxHealth");
            _bossesDefeated = AccessTools.Method(viking, "BossesDefeated");
            DamageMultiplier = AccessTools.Method(viking, "GetDamageMultiplier");
            _surrenderRolled = AccessTools.Field(viking, "m_surrenderRolled");
            _gearSignature = AccessTools.Field(viking, "m_gearSignature");
            Type dialog = AccessTools.TypeByName("ValheimNpc.VikingDialog");
            Type option = dialog == null ? null : AccessTools.Inner(dialog, "Option");
            _optionLabel = option == null ? null : AccessTools.Field(option, "Label");
            MethodInfo open = dialog == null ? null : AccessTools.GetDeclaredMethods(dialog)
                .FirstOrDefault(m => m.Name == "Open" && m.IsPublic && m.GetParameters().Length == 5);

            harmony.Patch(_updateMaxHealth, prefix: new HarmonyMethod(typeof(AlfrikBoss), nameof(MaxHealth)));
            harmony.Patch(DamageMultiplier, postfix: new HarmonyMethod(typeof(AlfrikBoss), nameof(DamageDealt)));
            harmony.Patch(AccessTools.Method(viking, "GetArmor"), postfix: new HarmonyMethod(typeof(AlfrikBoss), nameof(ArmourValue)));
            harmony.Patch(AccessTools.Method(viking, "RebuildGear"), postfix: new HarmonyMethod(typeof(AlfrikBoss), nameof(Gear)));
            harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.SetHealth)), prefix: new HarmonyMethod(typeof(AlfrikBoss), nameof(Health)));
            harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.GetHoverName)), postfix: new HarmonyMethod(typeof(AlfrikBoss), nameof(HoverName)));
            harmony.Patch(AccessTools.Method(typeof(Character), "RPC_Damage"), prefix: new HarmonyMethod(typeof(Brain), nameof(Brain.OnHit)));
            harmony.Patch(AccessTools.Method(typeof(Character), "RPC_Damage"), prefix: new HarmonyMethod(typeof(AlfrikBoss), nameof(StaggerBonus)));
            if (open != null && _optionLabel != null)
                harmony.Patch(open, prefix: new HarmonyMethod(typeof(AlfrikBoss), nameof(Dialog)));
            harmony.Patch(AccessTools.Method(viking, "GetHoverText"), postfix: new HarmonyMethod(typeof(AlfrikBoss), nameof(BossHoverText)));
            harmony.Patch(AccessTools.Method(typeof(BaseAI), "UpdateRegeneration"), prefix: new HarmonyMethod(typeof(AlfrikBoss), nameof(NoRegeneration)));
            harmony.Patch(AccessTools.Method(viking, "Interact"), prefix: new HarmonyMethod(typeof(AlfrikBoss), nameof(NoTalking)));
            harmony.Patch(AccessTools.Method(viking, "UseItem"), prefix: new HarmonyMethod(typeof(AlfrikBoss), nameof(NoTalking)));
            Brain.Init(harmony, viking);
            Plugin.Log.LogInfo(name + ", " + Title + ": " + Phases.Length + " boss phase(s), then a Berserker companion (console: " +
                name.Replace(" ", "").ToLowerInvariant() + ")");
        }

        internal static int Bosses() => _bossesDefeated == null ? 0 : (int)_bossesDefeated.Invoke(null, null);
        // A phase's health: [Boss] PhaseHealth (and PerBoss), and [Boss] PartyHealth more for every other player who was
        // within 60 m when the phase began (counted then and kept in his ZDO, so it holds through the phase).
        private static float PhaseMaxHealth() =>
            (Plugin.PhaseHealth.Value + Plugin.PhaseHealthPerBoss.Value * Bosses()) * (1f + Plugin.PartyHealth.Value * (Party() - 1));

        private static int Party() => _view != null && _view.IsValid() ? Mathf.Max(1, _view.GetZDO().GetInt(PartyKey, 1)) : 1;

        // Owner, as a phase begins: the players within 60 m of him (at least one).
        private static void CountParty(ZDO zdo)
        {
            int count = 0;
            foreach (Player player in Player.GetAllPlayers())
                if (player != null && !player.IsDead() && Vector3.Distance(player.transform.position, _char.transform.position) <= 60f)
                    count++;
            zdo.Set(PartyKey, Mathf.Max(1, count));
        }
        // As a companion his levels count the way Vikings for Hire counts them for any viking ([3] HealthPerLevel).
        private static float CompanionMaxHealth() =>
            (Plugin.CompanionHealth.Value + Plugin.CompanionHealthPerBoss.Value * Bosses()) *
            (1f + HealthPerLevel() * (Mathf.Max(1, _view != null && _view.IsValid() ? _view.GetZDO().GetInt(LevelKey, 1) : 1) - 1));

        private static BepInEx.Configuration.ConfigEntry<float> _healthPerLevel;
        private static bool _healthPerLevelLooked;

        private static float HealthPerLevel()
        {
            if (!_healthPerLevelLooked)
            {
                _healthPerLevelLooked = true;
                _healthPerLevel = AccessTools.Field(AccessTools.TypeByName("ValheimNpc.Plugin"), "HealthPerLevel")?.GetValue(null)
                    as BepInEx.Configuration.ConfigEntry<float>;
            }
            return _healthPerLevel != null ? _healthPerLevel.Value : 0f;
        }
        private static bool Is(object npc) => npc != null && npc == (object)_npc && _state != 0;

        private static float BossDamageShare() =>
            Mathf.Clamp(Plugin.BossDamageAtStart.Value + Plugin.BossDamagePerBoss.Value * Bosses(), 0.05f, Plugin.BossDamageMax.Value);

        // Called right after the summon command named the new viking.
        internal static string MakeBoss(MonoBehaviour npc)
        {
            Track(npc);
            ZDO zdo = _view.GetZDO();
            zdo.Set(StateKey, 1);
            zdo.Set(PhaseKey, 1);
            _state = 1;
            _phase = 1;
            zdo.Set(CombatRoleKey, Berserker);
            zdo.Set(TraitsKey, Traits);
            int bosses = Bosses();
            _configureBandit?.Invoke(null, new object[] { npc.gameObject, Mathf.Clamp(bosses, 0, 7), Mathf.Clamp(3 + bosses, 3, 10) });
            zdo.Set(BornKey, ZNet.instance.GetTime().Ticks);
            _surrenderRolled?.SetValue(npc, true);
            CountParty(zdo);
            _updateMaxHealth?.Invoke(npc, null);
            _char.SetHealth(_char.GetMaxHealth());
            _gearSignature?.SetValue(npc, null);
            Brain.NewPhase();
            Plugin.Log.LogInfo("Alfrik: summoned, phase 1, " + _char.GetMaxHealth().ToString("0") + " health (" + Party() + " player(s))");
            return _name + ", " + Title + ", comes down from the sky: " + Phases.Length + " phase(s) of " +
                _char.GetMaxHealth().ToString("0") + " health. Beat him and he is yours.";
        }

        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> AllZdos =
            AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
        private static readonly int VikingNameKey = "vnpc_name".GetStableHashCode();

        // Is he anywhere in the world already (any viking with his name: boss, yielding, companion, or one from before
        // the boss fight existed)? The host knows every object;
        // another player only what is near them.
        internal static bool InWorld(out Vector3 position)
        {
            position = Vector3.zero;
            Dictionary<ZDOID, ZDO> all = ZDOMan.instance == null ? null : AllZdos(ZDOMan.instance);
            if (all == null)
                return false;
            foreach (ZDO zdo in all.Values)
                if (zdo != null &&
                    string.Equals(zdo.GetString(VikingNameKey, "").Trim(), _name, StringComparison.OrdinalIgnoreCase))
                {
                    position = zdo.GetPosition();
                    return true;
                }
            return false;
        }

        // Console "alfrik reset": every viking with his name is taken out of the world wherever he is, loaded or
        // not, so he can be called again. The host can reach him anywhere; another player only near them.
        internal static string Remove()
        {
            Dictionary<ZDOID, ZDO> all = ZDOMan.instance == null ? null : AllZdos(ZDOMan.instance);
            if (all == null || ZNetScene.instance == null)
                return "Load into a world first.";
            var found = new List<ZDO>();
            foreach (ZDO zdo in all.Values)
                if (zdo != null &&
                    string.Equals(zdo.GetString(VikingNameKey, "").Trim(), _name, StringComparison.OrdinalIgnoreCase))
                    found.Add(zdo);
            if (found.Count == 0)
                return "No " + _name + " in the world (that this game can see).";
            Brain.Stop();
            foreach (ZDO zdo in found)
            {
                ZNetView loaded = ZNetScene.instance.FindInstance(zdo);
                if (loaded != null)
                {
                    loaded.ClaimOwnership();
                    ZNetScene.instance.Destroy(loaded.gameObject);
                }
                else
                {
                    zdo.SetOwner(ZDOMan.GetSessionID());
                    ZDOMan.instance.DestroyZDO(zdo);
                }
            }
            return _name + " is gone from the world (" + found.Count + "). Call him again with the stone or the command.";
        }

        // Console "alfrik phase": the boss bar he is on is over at once - the next phase, or after the last he yields
        // (to whoever is nearest). Only on the game that runs him.
        internal static string SkipPhase()
        {
            if (_char == null || _view == null || !_view.IsValid() || _state != 1)
                return "No " + _name + " fighting as a boss near you.";
            if (!_view.IsOwner())
                return "Another player's game runs " + _name + " right now; they have to use this.";
            if (InTransition || Entrance.Active)
                return "Wait until he is fighting.";
            if (_phase < Phases.Length)
            {
                BeginTransition();
                return _name + " goes to phase " + (_phase + 1) + ".";
            }
            Yield(_char);
            return _name + " yields; he joins the nearest player in a few seconds.";
        }

        private static readonly MethodInfo WorldTimeDelta = AccessTools.Method(typeof(BaseAI), "GetWorldTimeDelta");

        // BaseAI.UpdateRegeneration prefix: as the boss or yielding he does not heal. The game heals by the world time
        // since the AI last ran, and his AI is paused through every move, so each pause came back as one big heal (his
        // bar refilling). The time is still taken off, so he does not heal it all at once later as a companion.
        private static bool NoRegeneration(BaseAI __instance)
        {
            if (_char == null || __instance == null || __instance.gameObject != _char.gameObject || (_state != 1 && _state != 3))
                return true;
            WorldTimeDelta?.Invoke(__instance, null);
            return false;
        }

        // Vikings for Hire's hover text, while he is the boss or yielding: only his name and title (no faction, job or
        // keys), since nothing can be done with him then.
        private static void BossHoverText(object __instance, ref string __result)
        {
            if (Is(__instance) && (_state == 1 || _state == 3))
                __result = _name + ", " + ShownTitle;
        }

        // Vikings for Hire's Interact and UseItem, while he is the boss or yielding: nothing happens.
        private static bool NoTalking(object __instance, ref bool __result)
        {
            if (!Is(__instance) || (_state != 1 && _state != 3))
                return true;
            __result = false;
            return false;
        }

        private static void Track(MonoBehaviour npc)
        {
            if (npc == _npc)
                return;
            if (_char != null)
            {
                _char.m_boss = false;
                SetSpeed(1f);
            }
            Aura.Clear();
            _npc = npc;
            _char = npc == null ? null : npc.GetComponent<Humanoid>();
            _view = npc == null ? null : npc.GetComponent<ZNetView>();
            if (_char != null)
            {
                _baseRun = _char.m_runSpeed;
                Rigidbody body = _char.GetComponent<Rigidbody>();
                _baseInterpolation = body != null ? body.interpolation : RigidbodyInterpolation.None;
                _baseSpeed = _char.m_speed;
                _baseWalk = _char.m_walkSpeed;
            }
            _speedFactor = 1f;
            _barShare = -1f;
            _state = 0;
            _phase = 0;
            _lastState = -1;
            _lastPhase = 0;
            _rage = false;
            _quoteShown = false;
            _transitionPending = false;
            _transitionUntil = 0f;
            _transitionStage = 0;
            _joinAt = 0f;
            _companionWeapon = Weapons.HugrHamr;
            _weaponSince = -100f;
        }

        // Owner: movement speed against his own base (Brain adds the phase speed and the chase sprint).
        internal static void SetSpeed(float factor)
        {
            if (_char == null || Mathf.Approximately(factor, _speedFactor))
                return;
            _speedFactor = factor;
            _char.m_runSpeed = _baseRun * factor;
            _char.m_speed = _baseSpeed * factor;
            _char.m_walkSpeed = _baseWalk * factor;
        }

        internal static void Tick(MonoBehaviour npc)
        {
            Track(npc);
            if (_char == null || _view == null || !_view.IsValid())
                return;
            ZDO zdo = _view.GetZDO();
            _state = zdo.GetInt(StateKey, 0);
            _phase = zdo.GetInt(PhaseKey, 1);
            _rage = zdo.GetBool(RageKey, false);
            _char.m_boss = _state == 1;
            KeepMaxHealth();
            KeepSmooth();
            KeepAnimating();
            DamageReport();
            Messages();
            Aura.Update(_state == 1 && _phase == 1, _state != 0, _char, VikingVrmAvatarCore.Avatar, _state == 1 && zdo.GetBool(Brain.BloodKey, false),
                _state == 1 && _phase == 2);
            if (_state == 0 || !_view.IsOwner() || _char.IsDead())
                return;

            switch (_state)
            {
                case 1:
                    zdo.Set(BornKey, ZNet.instance.GetTime().Ticks);
                    zdo.Set(HermitKey, false);
                    zdo.Set(SmithKey, false);
                    _surrenderRolled?.SetValue(_npc, true);
                    if (_transitionPending)
                        Transition(zdo);
                    break;
                case 3:
                    if (_joinAt <= 0f)
                        _joinAt = Time.time + 5f;
                    if (Time.time >= _joinAt)
                        Join(zdo);
                    return;
                case 2:
                    zdo.Set(FormerOutlawKey, false);
                    zdo.Set(BetrayAtKey, 0L);
                    zdo.Set(CombatRoleKey, Berserker);
                    // One who joined before he was given his level gets it now, once.
                    if (!zdo.GetBool(LevelledKey, false))
                    {
                        JoinLevel(zdo);
                        _updateMaxHealth?.Invoke(_npc, null);
                    }
                    CompanionWeapon();
                    Taunt();
                    Rage(zdo);
                    break;
            }
            KeepWeapon();
        }

        private static int _healthFixes;
        private static float _barShare = -1f, _lastHealthFix = -100f;
        private static bool _healthFixStopped;

        // Owner, as a boss: his bar is a phase's full health ([Boss] PhaseHealth). The 0.9.8-0.9.10 logs show a bar of about
        // 66 after he was loaded mid-fight (something else set his max health); it is put back, the bar as full as it was
        // (as last seen right, so a max health cut that also cut his health never refills the bar). Reset again within
        // 10 s, it is left alone and logged, rather than fought over every frame.
        private static void KeepMaxHealth()
        {
            if (_state != 1 || !_view.IsOwner() || InTransition || Entrance.Active || _char.IsDead() || _healthFixStopped)
                return;
            float want = PhaseMaxHealth(), max = _char.GetMaxHealth();
            if (want <= 0f)
                return;
            if (Mathf.Abs(max - want) < 1f)
            {
                _barShare = Mathf.Clamp01(_char.GetHealth() / want);
                return;
            }
            if (Time.time - _lastHealthFix < 10f)
            {
                _healthFixStopped = true;
                Plugin.Log.LogWarning("Alfrik's max health keeps being set to " + max.ToString("0") + " by something else; no longer put back");
                return;
            }
            _lastHealthFix = Time.time;
            float share = _barShare >= 0f ? _barShare : (max > 0f ? Mathf.Clamp01(_char.GetHealth() / max) : 1f);
            _char.SetMaxHealth(want);
            _char.SetHealth(Mathf.Max(2f, want * share));
            if (_healthFixes++ < 5)
                Plugin.Log.LogInfo("Alfrik's max health was " + max.ToString("0") + ", set to " + want.ToString("0") + " (phase " + _phase +
                    ", bar kept at " + (share * 100f).ToString("0") + "%)");
        }

        private static RigidbodyInterpolation _baseInterpolation;
        private static bool _smoothLogged;

        // On the game that runs him his body moves with the physics steps, 50 a second; smoothed between them it does
        // not judder on screen at a higher frame rate. Not during the sky dive, which moves him directly (Entrance).
        private static void KeepSmooth()
        {
            Rigidbody body = _char.GetComponent<Rigidbody>();
            if (body == null)
                return;
            // In Odin's Fall his kinematic body is moved along the arc: no smoothing then (Brain.HoldInAir keeps it so).
            RigidbodyInterpolation want = _view.IsOwner() && !Entrance.Active && !Brain.Leaping ? RigidbodyInterpolation.Interpolate
                : (Brain.Leaping ? RigidbodyInterpolation.None : _baseInterpolation);
            if (body.interpolation == want)
                return;
            if (!_smoothLogged)
            {
                _smoothLogged = true;
                Plugin.Log.LogInfo("Alfrik's body smoothing was " + body.interpolation + ", now " + want);
            }
            body.interpolation = want;
        }

        private static bool _animLogged;

        // His (hidden) body's animator, which the avatar copies: evaluated every frame, and never culled for being
        // unseen. Set to run with the physics steps it only made a new pose on some frames, so the avatar's limbs
        // stuttered against a smoothly moving body - the shake.
        private static void KeepAnimating()
        {
            Animator animator = Poses.AnimatorOf(_char);
            if (animator == null)
                return;
            if (!_animLogged)
            {
                _animLogged = true;
                Plugin.Log.LogInfo("Alfrik's animator was " + animator.updateMode + " / " + animator.cullingMode + "; now Normal / AlwaysAnimate");
            }
            if (animator.updateMode != AnimatorUpdateMode.Normal)
                animator.updateMode = AnimatorUpdateMode.Normal;
            if (animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private static int _hits, _capped;
        private static float _damage, _biggest, _nextDamageReport;

        // Diagnostics (owner, as a boss): the hits he took in the last 15 s, so the fight's length can be tuned.
        private static void DamageReport()
        {
            if (Time.time < _nextDamageReport)
                return;
            _nextDamageReport = Time.time + 15f;
            if (_hits > 0)
                Plugin.Log.LogInfo("Alfrik took " + _hits + " hits in 15 s (phase " + _phase + "): " + _damage.ToString("0") + " damage = " +
                    (_damage / Mathf.Max(1f, _char.GetMaxHealth()) * 100f).ToString("0.0") + "% of the bar, biggest hit " +
                    _biggest.ToString("0") + " before the cap, " + _capped + " capped at " + (Plugin.MaxHitShare.Value * 100f).ToString("0.#") + "%");
            _hits = _capped = 0;
            _damage = _biggest = 0f;
        }

        private static void Messages()
        {
            Player me = Player.m_localPlayer;
            if (me == null || MessageHud.instance == null)
            {
                _lastState = _state;
                return;
            }
            bool near = Vector3.Distance(me.transform.position, _char.transform.position) < 50f;
            if (_state == 1 && near && !_quoteShown)
            {
                _quoteShown = true;
                _lastPhase = _phase;
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, _name + ", " + ShownTitle + "\n\n" + Quote + "\n\n" + PhaseLine());
            }
            else if (_state == 1 && near && _phase != _lastPhase)
            {
                _lastPhase = _phase;
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, PhaseLine());
            }
            if (_lastState == 1 && _state == 3 && near)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, _name + " yields.");
            if ((_lastState == 1 || _lastState == 3) && _state == 2)
            {
                string winner = _view.GetZDO().GetString(WinnerNameKey, "");
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    Title + " now fights for " + (winner.Length > 0 ? winner : "you") + ".");
            }
            _lastState = _state;
        }

        private static string PhaseLine()
        {
            BossPhase phase = CurrentPhase;
            string weapon = phase == null ? "" : Weapons.DisplayName(phase.Weapon);
            return "Phase " + Numerals[Mathf.Clamp(_phase, 1, Numerals.Length) - 1] + " - " + phase?.Name +
                (weapon.Length > 0 ? " (" + weapon + ")" : "");
        }

        // Owner: the current phase's bar is empty. Untouchable through the change: he staggers, sinks to one knee with a
        // fist to the ground, rises with the next phase's weapon bursting into his hands, and roars, the blast throwing
        // everyone near him back (no damage). Then the fight goes on.
        private const float FalterTime = 0.9f, KneelUntil = 2.7f, RoarAt = 2.95f;
        private static int _transitionStage;

        private static void BeginTransition()
        {
            _transitionPending = true;
            _transitionUntil = Time.time + TransitionTime;
            _transitionStage = 0;
            Brain.Stop();
            Brain.Trigger("stagger");
            Fx.Send(Fx.Roar);
            Say("ODIN! IS THAT ALL?!");
        }

        // Owner, every frame of the change.
        private static void Transition(ZDO zdo)
        {
            float into = Time.time - (_transitionUntil - TransitionTime);
            if (_transitionStage == 0 && into >= FalterTime)
            {
                _transitionStage = 1;
                Brain.Trigger("emote_kneel");
                Fx.Send(Fx.Rune);
            }
            else if (_transitionStage == 1 && into >= KneelUntil)
            {
                _transitionStage = 2;
                Brain.Trigger("emote_stop");
                NextPhase(zdo);
            }
            else if (_transitionStage == 2 && into >= RoarAt)
            {
                _transitionStage = 3;
                Brain.Trigger("emote_roar");
                Entrance.Shockwave(_char, _char.transform.position, 6f);
            }
            else if (_transitionStage == 3 && Time.time >= _transitionUntil)
            {
                _transitionStage = 0;
                _transitionPending = false;
                Brain.Trigger("emote_stop");
            }
        }

        // His pose through the change (the stagger is the game's own animation; then the fist to the ground).
        internal static int TransitionPose => _transitionPending && _transitionStage == 1 ? Poses.Landing : Poses.None;

        private static void NextPhase(ZDO zdo)
        {
            _phase++;
            zdo.Set(PhaseKey, _phase);
            Fx.Send(Fx.Swap);
            Brain.NewPhase();
            CountParty(zdo);
            _updateMaxHealth?.Invoke(_npc, null);
            _char.SetHealth(_char.GetMaxHealth());
            Plugin.Log.LogInfo("Alfrik: phase " + _phase + ", " + CurrentPhase?.Name + ", " + _char.GetMaxHealth().ToString("0") + " health (" +
                Party() + " player(s))");
            // The new phase's weapon straight into his hands (Gear: the old one out, the new one in and equipped),
            // instead of a whole gear rebuild that re-equipped him over and over.
            Gear(_npc);
        }

        // Owner: rage below half health as a companion, with a war cry once per rage.
        private static void Rage(ZDO zdo)
        {
            bool rage = _char.GetHealthPercentage() < RageAt;
            if (rage == _rage)
                return;
            _rage = rage;
            zdo.Set(RageKey, rage);
            if (rage)
                Say("RAAAGH! ODIN, WATCH YOUR FIST!");
        }

        // Owner: as a companion, enemies near him whose AI runs on this game turn on him, so he holds the front line.
        private static void Taunt()
        {
            if (Time.time < _nextTaunt)
                return;
            _nextTaunt = Time.time + 6f;
            int taunted = 0;
            foreach (Character enemy in Character.GetAllCharacters())
            {
                if (taunted >= 6)
                    break;
                if (enemy == null || enemy == _char || enemy.IsPlayer() || enemy.IsDead() || enemy.IsTamed() ||
                    Vector3.Distance(enemy.transform.position, _char.transform.position) > TauntRadius ||
                    !BaseAI.IsEnemy(_char, enemy) || !(enemy.GetBaseAI() is MonsterAI ai))
                    continue;
                ZNetView view = enemy.GetComponent<ZNetView>();
                if (view == null || !view.IsOwner() || TargetOf(ai) == _char)
                    continue;
                TargetOf(ai) = _char;
                ai.Alert();
                taunted++;
            }
            if (taunted > 0 && Time.time >= _nextShout)
            {
                _nextShout = Time.time + 30f;
                Say("Face ME, cowards!");
            }
        }

        internal static void Say(string text)
        {
            if (_view != null && _view.IsValid())
                _view.InvokeRPC(ZNetView.Everybody, "VNPC_Say", text);
        }

        // As a companion he picks his own weapon for the fight in front of him: Fjallbrjótr against a boss, anything big
        // or three or more enemies, Hugr & Hamr otherwise (the other one goes on his back). Out of a fight he keeps what he
        // has; he changes at most every 15 s, never mid-swing.
        private static string _companionWeapon = Weapons.HugrHamr;
        private static float _nextWeaponCheck, _weaponSince = -100f;

        private static void CompanionWeapon()
        {
            if (Time.time < _nextWeaponCheck)
                return;
            _nextWeaponCheck = Time.time + 1f;
            if (_char.InAttack() || Time.time - _weaponSince < 15f)
                return;
            int near = 0;
            bool big = false;
            foreach (Character other in Character.GetAllCharacters())
            {
                if (other == null || other == _char || other.IsDead() || !BaseAI.IsEnemy(_char, other) ||
                    Vector3.Distance(other.transform.position, _char.transform.position) > 12f)
                    continue;
                near++;
                if (other.IsBoss() || other.GetMaxHealth() >= 1500f || other.GetRadius() >= 1.2f)
                    big = true;
            }
            string want = near >= 3 || big ? Weapons.Skeggold : Weapons.HugrHamr;
            if (near == 0 || want == _companionWeapon)
                return;
            _companionWeapon = want;
            _weaponSince = Time.time;
            Gear(_npc);
            Say(want == Weapons.Skeggold ? "Fjallbrjótr!" : "Hugr! Hamr!");
            Plugin.Log.LogInfo(_name + " takes up " + Weapons.DisplayName(want) + " (" + near + " enemies near" + (big ? ", one of them big)" : ")"));
        }

        private static GameObject WeaponPrefab()
        {
            if (ObjectDB.instance == null)
                return null;
            string own = _state == 1 ? CurrentPhase?.Weapon : _companionWeapon;
            GameObject prefab = string.IsNullOrEmpty(own) ? null : ObjectDB.instance.GetItemPrefab(own);
            return prefab != null ? prefab : BestHammer();
        }

        // Owner: when his weapon should change (a new phase, a better fallback hammer), rebuild his gear. It checks what
        // he carries, not what is in his hand, since Vikings for Hire sheathes weapons outside a fight.
        private static void KeepWeapon()
        {
            GameObject wanted = WeaponPrefab();
            if (wanted != null && !_char.GetInventory().GetAllItems().Any(item => item.m_dropPrefab != null && item.m_dropPrefab.name == wanted.name))
                _gearSignature?.SetValue(_npc, null);
        }

        private static GameObject BestHammer()
        {
            int bosses = Bosses();
            GameObject oneHanded = null;
            foreach (var (prefab, needed) in Hammers)
            {
                if (needed > bosses)
                    continue;
                GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
                ItemDrop.ItemData.SharedData shared = item == null ? null : item.GetComponent<ItemDrop>()?.m_itemData.m_shared;
                if (shared == null || shared.m_skillType != Skills.SkillType.Clubs)
                    continue;
                if (shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon)
                    return item;
                if (oneHanded == null)
                    oneHanded = item;
            }
            return oneHanded;
        }

        private static void Yield(Character __instance)
        {
            ZDO zdo = _view.GetZDO();
            Character attacker = LastHit(__instance)?.GetAttacker();
            if (attacker is Player player)
            {
                zdo.Set(WinnerKey, player.GetPlayerID());
                zdo.Set(WinnerNameKey, player.GetPlayerName());
            }
            zdo.Set(StateKey, 3);
            _state = 3;
            _joinAt = Time.time + 5f;
            Brain.Stop();
            Brain.NewPhase();
            SetSpeed(1f);
            _surrender?.Invoke(_npc, null);
            Fx.Send(Fx.Yield);
        }

        private static void Join(ZDO zdo)
        {
            long winner = zdo.GetLong(WinnerKey, 0L);
            string winnerName = zdo.GetString(WinnerNameKey, "");
            if (winner == 0L)
            {
                Player closest = Player.GetClosestPlayer(_char.transform.position, 60f);
                if (closest == null)
                    return;
                winner = closest.GetPlayerID();
                winnerName = closest.GetPlayerName();
                zdo.Set(WinnerNameKey, winnerName);
            }
            _joinPlayer.Invoke(_npc, new object[] { winner, winnerName });
            zdo.Set(FormerOutlawKey, false);
            zdo.Set(BetrayAtKey, 0L);
            zdo.Set(CombatRoleKey, Berserker);
            zdo.Set(StateKey, 2);
            zdo.Set(RageKey, false);
            _state = 2;
            _rage = false;
            _char.m_boss = false;
            JoinLevel(zdo);
            _updateMaxHealth.Invoke(_npc, null);
            _char.SetHealth(_char.GetMaxHealth() * 0.6f);
            _gearSignature?.SetValue(_npc, null);
            Say("My axes are yours, " + winnerName + ". Point me at them.");
        }

        private static bool MaxHealth(object __instance)
        {
            if (!Is(__instance))
                return true;
            float max = _state == 2 ? CompanionMaxHealth() : PhaseMaxHealth();
            if (Mathf.Abs(_char.GetMaxHealth() - max) > 0.1f)
                _char.SetMaxHealth(max);
            return false;
        }

        private static void DamageDealt(object __instance, ref float __result)
        {
            if (Is(__instance))
                __result *= (_state == 2 ? 1.35f * (_rage ? 1.3f : 1f) : BossDamageShare()) * (Brain.WarCry ? 1.25f : 1f) * Brain.SwingBoost;
        }

        private static void ArmourValue(object __instance, ref float __result)
        {
            if (Is(__instance))
                __result += Plugin.HiddenArmor.Value + Plugin.HiddenArmorPerBoss.Value * Bosses();
        }

        // Owner, after Vikings for Hire rebuilt his gear: no armour or shield, no other weapon, his own weapon in hand.
        private static void Gear(object __instance)
        {
            if (!Is(__instance))
                return;
            Inventory inventory = _char.GetInventory();
            GameObject weapon = WeaponPrefab();
            ItemDrop.ItemData held = null;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
                bool isWeapon = weapon != null && item.m_dropPrefab != null && item.m_dropPrefab.name == weapon.name;
                if (isWeapon && held == null)
                {
                    held = item;
                    continue;
                }
                if (Array.IndexOf(Armour, item.m_shared.m_itemType) >= 0 || item.IsWeapon())
                {
                    _char.UnequipItem(item, false);
                    inventory.RemoveItem(item);
                }
            }
            if (held == null && weapon != null)
            {
                held = weapon.GetComponent<ItemDrop>().m_itemData.Clone();
                held.m_dropPrefab = weapon;
                held.m_quality = held.m_shared.m_maxQuality;
                held.m_durability = held.GetMaxDurability();
                if (!inventory.AddItem(held))
                    held = null;
            }
            if (held != null && !_char.IsItemEquiped(held))
                _char.EquipItem(held, false);
        }

        // Boss: a phase bar that empties starts the next phase, the last one makes him yield; he never dies.
        // Between phases and while yielding he takes no damage. No single hit (a parry counter, a crit, a staggered
        // double hit) takes more than [Boss] MaxHitShare of a bar.
        private static void Health(Character __instance, ref float health)
        {
            if (__instance != _char || (_state != 1 && _state != 3))
                return;
            if (_state == 3 || InTransition || Entrance.Active)
            {
                health = Mathf.Max(health, __instance.GetHealth());
                return;
            }
            float current = __instance.GetHealth(), cap = __instance.GetMaxHealth() * Plugin.MaxHitShare.Value;
            float taken = current - health;
            if (cap > 0f && taken > cap)
                health = current - cap;
            if (taken > 0f)
            {
                _hits++;
                _damage += current - health;
                _biggest = Mathf.Max(_biggest, taken);
                if (cap > 0f && taken > cap)
                    _capped++;
            }
            if (health > 1f || _view == null || !_view.IsOwner())
                return;
            health = 1f;
            if (_phase < Phases.Length)
                BeginTransition();
            else
                Yield(__instance);
        }

        // A staggered enemy takes double damage (Character.RPC_Damage); the boss takes one and a half.
        // As a boss, hits build up stagger on him at 40% (a perfect parry still staggers him outright).
        private static void StaggerBonus(Character __instance, HitData hit)
        {
            if (__instance != _char || _state != 1 || hit == null)
                return;
            hit.m_staggerMultiplier *= _phase == 2 ? 0.2f : 0.4f;
            if (__instance.IsStaggering())
                hit.ApplyModifier(0.75f);
        }

        private static void HoverName(Character __instance, ref string __result)
        {
            if (__instance == _char && _state != 0)
                __result = _name + ", " + ShownTitle;
        }

        // His companion menu: combat orders only, with his own title and saga.
        private static void Dialog(object npc, ref string title, ref string body, System.Collections.IList options)
        {
            if (!Is(npc) || _state != 2 || options == null)
                return;
            bool companionMenu = false;
            foreach (object option in options)
                if (option != null && (_optionLabel.GetValue(option) as string ?? "").StartsWith("$vnpc_opt_rename"))
                    companionMenu = true;
            if (!companionMenu)
                return;
            for (int i = options.Count - 1; i >= 0; i--)
            {
                string label = _optionLabel.GetValue(options[i]) as string ?? "";
                if (!CombatOptions.Any(label.StartsWith))
                    options.RemoveAt(i);
            }
            ItemDrop.ItemData weapon = _char.GetCurrentWeapon();
            title = _name + " - " + Title;
            body = "<i>" + Quote + "</i>\n\n" +
                "Berserker - holds the front line, draws enemies onto himself, rages below half health.\n" +
                "Health " + _char.GetHealth().ToString("0") + " / " + _char.GetMaxHealth().ToString("0") +
                "   Armour " + (Plugin.HiddenArmor.Value + Plugin.HiddenArmorPerBoss.Value * Bosses()).ToString("0") +
                "   Weapon: " + (weapon == null ? "none" : Localization.instance.Localize(weapon.m_shared.m_name));
        }
    }
}
