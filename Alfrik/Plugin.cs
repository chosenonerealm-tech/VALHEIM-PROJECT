using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace Alfrik
{
    // Alfrik, "The Left Fist of Odin": one Vikings for Hire viking wearing a VRM avatar, summoned as a boss in phases
    // and, once beaten, a Berserker companion.
    //   Avatar.cs  - the VRM avatar on the viking named VikingName (ValheimVRM's loader used as a library)
    //   Boss.cs    - boss / yielding / companion states, phases, stats, gear, menu
    //   Brain.cs   - his combat abilities, run on the game that owns him
    //   Weapons.cs - his weapons (Hugr & Hamr), models loaded from glTF files next to this plugin
    //   HeldAxes.cs - his axes as held: the model on each hand joint instead of the game's skinned dual axes
    //   Hatchet.cs - Flying Hatchet, the thrown axe that comes back
    //   Aura.cs    - his aura: steam and the red eye
    //   Effects.cs - his moments (tells, roar, dodge, slash, parry, hits) as sound plus particles, on every game
    //   Sounds.cs  - his own sounds from Audio/ and his fight music
    [BepInPlugin(Guid, "Alfrik", Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.alfrik";
        public const string Version = "0.9.12";

        internal static ManualLogSource Log;
        internal static string Folder;

        internal static ConfigEntry<string> VrmVikingName;
        internal static ConfigEntry<float> PhaseHealth, PhaseHealthPerBoss, BossDamageAtStart, BossDamagePerBoss, BossDamageMax, MaxHitShare;
        internal static ConfigEntry<float> HiddenArmor, HiddenArmorPerBoss, CompanionHealth, CompanionHealthPerBoss;
        internal static ConfigEntry<float> AxeScale;
        internal static ConfigEntry<Vector3> AxeRotation, AxeOffset, AxeLeftRotation, EyeAdjust, BackOffset;
        internal static ConfigEntry<float> SteamAmount, MusicVolume, SoundVolume;
        internal static ConfigEntry<bool> TellPoses, AlignAxes;
        internal static ConfigEntry<float> StaggerOnYou;
        internal static ConfigEntry<string> Offering, StoneRecipe;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Folder = System.IO.Path.GetDirectoryName(Info.Location);

            // The avatar is drawn by each player's own game from their own files, so this is not synced.
            VrmVikingName = Config.Bind("General", "VikingName", "Alfrik",
                "The one Vikings for Hire viking with this name wears the VRM avatar Valheim/ValheimVRM/<name>.vrm (with " +
                "settings_<name>.txt) and is the boss. The console command is this name in lower case. Everyone in the " +
                "group needs the same files. Empty = off. Restart required.");
            PhaseHealth = Admin("Boss", "PhaseHealth", 4000f, "Health of each boss phase (an endgame boss). Every phase has its own full health bar.");
            PhaseHealthPerBoss = Admin("Boss", "PhaseHealthPerBoss", 0f, "Extra health per phase for every boss defeated in the world (0 = always endgame).");
            BossDamageAtStart = Admin("Boss", "DamageAtStart", 0.6f,
                "His weapons hit like endgame weapons; as a boss he deals this share of that in a world with no boss defeated.");
            BossDamagePerBoss = Admin("Boss", "DamagePerBoss", 0f, "Added to that share for every boss defeated (0 = always endgame).");
            BossDamageMax = Admin("Boss", "DamageMax", 1.2f, "Highest share.");
            MaxHitShare = Admin("Boss", "MaxHitShare", 0.025f,
                "As a boss no single hit takes more than this share of a phase bar (0.025 = 2.5%), so a parry counter or a crit cannot" +
                " delete it; staggered he also takes x1.5 damage instead of x2. 0 = no cap.");
            HiddenArmor = Admin("Boss", "HiddenArmor", 50f,
                "Armour built into his body (he never wears armour, so nothing clips through the avatar). Vikings for Hire lets damage x 100 / (100 + armour) through: 50 = 67%.");
            HiddenArmorPerBoss = Admin("Boss", "HiddenArmorPerBoss", 0f, "Extra hidden armour for every boss defeated.");
            StaggerOnYou = Admin("Boss", "StaggerOnYou", 0.3f,
                "How much his hits (blocked or not) fill your stagger bar, against a normal hit of that size (1 = normal).");
            CompanionHealth = Admin("Companion", "Health", 1500f, "His health as your companion in a world with no boss defeated.");
            CompanionHealthPerBoss = Admin("Companion", "HealthPerBoss", 1000f, "Extra companion health for every boss defeated.");
            AxeScale = Config.Bind("Hugr & Hamr", "Scale", 1f, "Size of the axes in his hands. Applies at once.");
            AxeRotation = Config.Bind("Hugr & Hamr", "Rotation", Vector3.zero,
                "Extra rotation of both axes in his hands, in degrees (x, y, z). Applies at once.");
            AxeOffset = Config.Bind("Hugr & Hamr", "Offset", Vector3.zero,
                "Moves both axes in his hands, in metres (x, y, z) along the hand. Applies at once.");
            AxeLeftRotation = Config.Bind("Hugr & Hamr", "LeftRotation", Vector3.zero,
                "Extra rotation of the left-hand axe only, in degrees (x, y, z). Applies at once.");
            AlignAxes = Config.Bind("Hugr & Hamr", "AlignToAvatar", true,
                "Lay his axes in the avatar's visible hands (haft through the palm, head on the thumb side). Off = the game's own grip on the hidden body's hands. Applies at once.");
            BackOffset = Config.Bind("Back", "Offset", Vector3.zero,
                "Moves the weapons slung on his back, in metres: x = to his right (+), y = up (+), z = further out from his back (+). Applies at once.");
            AxeScale.SettingChanged += (_, __) => HeldAxes.Clear();
            AxeRotation.SettingChanged += (_, __) => HeldAxes.Clear();
            AxeOffset.SettingChanged += (_, __) => HeldAxes.Clear();
            AxeLeftRotation.SettingChanged += (_, __) => HeldAxes.Clear();
            // How his aura looks is each player's own view, so it is not synced. Both apply at once.
            EyeAdjust = Config.Bind("Aura", "EyeAdjust", Vector3.zero,
                "Moves the red glow of his eye, in metres on the avatar: x = to his left (-) or right (+), y = up, z = forward.");
            SteamAmount = Config.Bind("Aura", "SteamAmount", 1f,
                new ConfigDescription("How much steam rises off him in phase 1 (0 = none).", new AcceptableValueRange<float>(0f, 3f)));
            MusicVolume = Config.Bind("Audio", "MusicVolume", 0.4f,
                new ConfigDescription("Volume of his fight music, on top of the game's music volume (0 = off). Replace Audio/music_phase1.ogg to change the track.", new AcceptableValueRange<float>(0f, 1f)));
            SoundVolume = Config.Bind("Audio", "SoundVolume", 0.7f,
                new ConfigDescription("Volume of his sounds (on top of the game's sound-effect volume).", new AcceptableValueRange<float>(0f, 1f)));
            TellPoses = Config.Bind("Poses", "TellPoses", true,
                "His warning poses (Twin Reaper crouch, Blade Catch axes across the chest, Ravens' Flight arms back), worked " +
                "into his animation. Only how he looks on your screen. Applies at once.");
            EyeAdjust.SettingChanged += (_, __) => Aura.Clear();
            SteamAmount.SettingChanged += (_, __) => Aura.Clear();
            Offering = Admin("Summoning", "Offering", "TrophyFenring:1",
                "What Odin's Challenge Stone takes to call him down: item prefab name:count (empty = nothing).");
            StoneRecipe = Admin("Summoning", "StoneRecipe", "Stone:50,BlackMetal:30,YggdrasilWood:20,Eitr:20,BlackCore:5,DragonTear:5",
                "What building Odin's Challenge Stone costs (hammer, Misc, next to a Galdr table): item:count,item:count. Restart required.");

            _harmony = new Harmony(Guid);
            _harmony.Patch(AccessTools.Method(typeof(Game), "Start"), postfix: new HarmonyMethod(typeof(Plugin), nameof(RegisterRpcs)));
            Weapons.Init();
            BossBar.Apply(_harmony);
            RuneStone.Register();
            HeldAxes.Hook();
            BackWeapons.Hook();
            Slow.Register();
            Jotunn.Managers.CommandManager.Instance.AddConsoleCommand(new SummonCommand());
        }

        // Vikings for Hire loads after this plugin, so its types are looked up once every plugin has loaded.
        private void Start()
        {
            VikingVrmAvatar.Apply(_harmony);
            BossMusic.Apply(_harmony);
            StartCoroutine(Sounds.Load());
        }

        private void Update()
        {
            Entrance.Update();
            Brain.Update();
            BossMusic.Update();
        }

        private void LateUpdate()
        {
            Poses.Update();
            VikingVrmAvatar.Update();
            HeldAxes.Update();
            BackWeapons.Update();
        }

        private static void RegisterRpcs()
        {
            Hatchet.Register();
            GroundWave.Register();
            ReturnedShot.Register();
            Strikes.Register();
            Quake.Register();
            Brain.Register();
            Fx.Register();
        }

        // Balance belongs to the server: Jotunn sends the host's values to every player.
        private ConfigEntry<T> Admin<T>(string section, string key, T value, string description) =>
            Config.Bind(section, key, value, new ConfigDescription(description, null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
    }

    // "alfrik" (VikingName in lower case): summons him as the boss; "alfrik reset" takes him out of the world, "alfrik
    // phase" ends the phase he is on (after the last he yields).
    internal class SummonCommand : Jotunn.Entities.ConsoleCommand
    {
        private static string Viking => string.IsNullOrWhiteSpace(Plugin.VrmVikingName.Value) ? "Alfrik" : Plugin.VrmVikingName.Value.Trim();

        public override string Name => Viking.Replace(" ", "").ToLowerInvariant();

        public override string Help => "- summon " + Viking + ", " + AlfrikBoss.Title + " (only one at a time); '" + Name +
            " reset' removes him from the world, '" + Name + " phase' ends his current phase";

        public override bool IsCheat => true;

        public override List<string> CommandOptionList() => new List<string> { "reset", "phase" };

        public override void Run(string[] args)
        {
            string option = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "";
            string said;
            try
            {
                said = option == "reset" ? AlfrikBoss.Remove()
                    : option == "phase" ? AlfrikBoss.SkipPhase()
                    : option == "" ? VikingVrmAvatar.Spawn()
                    : "Use '" + Name + "', '" + Name + " reset' or '" + Name + " phase'.";
            }
            catch (System.Exception e)
            {
                said = Name + " " + option + " failed: " + e.Message;
                Plugin.Log.LogError(e);
            }
            Plugin.Log.LogInfo("Console '" + (Name + " " + option).Trim() + "': " + said);
            Console.instance.AddString(said);
        }
    }
}
