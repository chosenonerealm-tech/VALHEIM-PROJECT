extern alias vrm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Alfrik
{
    // One Vikings for Hire viking, the one named [General] VikingName (Alfrik), wears ValheimVRM/<name>.vrm. The
    // ValheimVRM plugin is not installed (it only dresses players, matched by player name); its loader sits in
    // valheim_Data/Managed as a plain library and is called from VikingVrmAvatarCore, the way the plugin does it for a
    // player: import once, register (material fix + LOD), then per wearer instantiate it next to the animator, hide the
    // original body and let VRMAnimationSync copy the pose. Nothing here touches ValheimVRM types, so a game without
    // the library never loads it.
    internal static class VikingVrmAvatar
    {
        private static Action _update;
        private static Func<string> _spawn;

        // Console command "alfrik" (devcommands): spawn the avatar's viking in front of you.
        internal static string Spawn() =>
            _spawn != null ? _spawn() : "VRM avatar is off: set [General] VikingName and put its .vrm in Valheim/ValheimVRM.";

        private static Func<Player, Vector3, Vector3, string, int, string> _summon;

        // The summoning stone: he dives in onto 'ground' from the side 'from' points to, for the offering.
        internal static string Summon(Player player, Vector3 ground, Vector3 from, string offering, int amount) =>
            _summon != null ? _summon(player, ground, from, offering, amount) : "The stone is silent: the VRM avatar is off.";

        internal static void Apply(Harmony harmony)
        {
            string name = Plugin.VrmVikingName.Value?.Trim();
            if (string.IsNullOrEmpty(name) || !System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.ManagedPath, "ValheimVRM.dll")) ||
                BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.yoship1639.plugins.valheimvrm"))
                return;
            string file = System.IO.Path.Combine(System.IO.Path.Combine(Environment.CurrentDirectory, "ValheimVRM"), name + ".vrm");
            if (!System.IO.File.Exists(file))
            {
                Plugin.Log.LogWarning("VRM avatar for viking '" + name + "' not set up: " + file + " is missing");
                return;
            }
            Start(harmony, name);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Start(Harmony harmony, string name)
        {
            if (!VikingVrmAvatarCore.Apply(harmony, name))
                return;
            _update = VikingVrmAvatarCore.Update;
            _spawn = VikingVrmAvatarCore.Spawn;
            _summon = VikingVrmAvatarCore.Summon;
            _findShaders = VikingVrmAvatarCore.FindShaders;
        }

        internal static void Update() => _update?.Invoke();

        // Weapons loads its glTF models with the same shader lookup as the avatar.
        private static Action<bool> _findShaders;
        internal static void FindShaders(bool on) => _findShaders?.Invoke(on);
    }

    internal static class VikingVrmAvatarCore
    {
        private const float CheckInterval = 1f;
        private const float DrawDistance = 60f;

        private static readonly HashSet<MonoBehaviour> _vikings = new HashSet<MonoBehaviour>();
        private static string _name;
        private static vrm::ValheimVRM.Settings.VrmSettingsContainer _settings;
        private static GameObject _template;
        private static bool _loading, _failed;
        private static float _nextCheck;

        private static MonoBehaviour _wearer;
        internal static GameObject Avatar => _shown ? _avatar : null;
        private static GameObject _avatar;
        private static Animator _animator;
        private static AnimatorCullingMode _cullingMode;
        private static bool _shown;

        private static Dictionary<string, Shader> _gameShaders;
        private static bool _findingShaders;

        internal static void FindShaders(bool on)
        {
            if (on && vrm::ValheimVRM.VRMShaders.Shaders.Count == 0)
            {
                vrm::ValheimVRM.Settings.ReloadGlobalSettings();
                vrm::ValheimVRM.VRMShaders.Initialize();
            }
            _findingShaders = on;
        }

        internal static bool Apply(Harmony harmony, string name)
        {
            Type viking = AccessTools.TypeByName("ValheimNpc.VikingNpc");
            MethodInfo awake = viking == null ? null : AccessTools.Method(viking, "Awake");
            MethodInfo destroy = viking == null ? null : AccessTools.Method(viking, "OnDestroy");
            MethodInfo lodgroup = AccessTools.Method(typeof(VisEquipment), "UpdateLodgroup");
            MethodInfo find = AccessTools.Method(typeof(Shader), nameof(Shader.Find), new[] { typeof(string) });
            if (awake == null || destroy == null || lodgroup == null || find == null)
                return false;
            _name = name;
            harmony.Patch(awake, postfix: new HarmonyMethod(typeof(VikingVrmAvatarCore), nameof(Register)));
            harmony.Patch(destroy, postfix: new HarmonyMethod(typeof(VikingVrmAvatarCore), nameof(Forget)));
            harmony.Patch(lodgroup, postfix: new HarmonyMethod(typeof(VikingVrmAvatarCore), nameof(EquipmentChanged)));
            harmony.Patch(find, prefix: new HarmonyMethod(typeof(VikingVrmAvatarCore), nameof(FindShader)));
            Plugin.Log.LogInfo("VRM avatar '" + name + "' goes on the one Vikings for Hire viking named " + name);
            AlfrikBoss.Apply(harmony, viking, name);
            return true;
        }

        private static void Register(MonoBehaviour __instance) => _vikings.Add(__instance);

        private static void Forget(MonoBehaviour __instance)
        {
            _vikings.Remove(__instance);
            if (__instance == _wearer)
                TakeOff();
        }

        // Armour, hair and beard are rebuilt as new body renderers whenever the viking's gear or look changes.
        private static void EquipmentChanged(VisEquipment __instance)
        {
            if (_shown && _wearer != null && __instance.gameObject == _wearer.gameObject)
                SetBodyHidden(true);
        }

        // ValheimVRM's own Shader.Find patch, limited to while the avatar loads: UniVRM asks for its MToon/UniUnlit
        // shaders (from UniVrm.shaders) and the material fix asks for the game's Custom/Player, which Shader.Find
        // cannot see in a build.
        private static bool FindShader(string name, ref Shader __result)
        {
            if (!_findingShaders || name == null)
                return true;
            if (_gameShaders == null)
            {
                _gameShaders = new Dictionary<string, Shader>();
                foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>())
                    if (!_gameShaders.ContainsKey(shader.name))
                        _gameShaders.Add(shader.name, shader);
            }
            if (vrm::ValheimVRM.VRMShaders.Shaders.TryGetValue(name, out Shader found) || _gameShaders.TryGetValue(name, out found))
            {
                __result = found;
                return false;
            }
            return true;
        }

        private static readonly int NameKey = "vnpc_name".GetStableHashCode();
        private static readonly int TraderKey = "vnpc_trader".GetStableHashCode();
        private static MonoBehaviour _named;

        // The console command: he comes down 9 m in front of you, diving in from beyond.
        internal static string Spawn()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return "Load into a world first.";
            return Summon(player, player.transform.position + player.transform.forward * 9f, player.transform.forward, null, 0);
        }

        // Called down by the summoning stone or the command: Vikings for Hire's own vnpc_spawn makes a wandering viking
        // in front of you (its Awake gives a random name and maybe the trader role); renamed, AlfrikBoss makes him the
        // boss and he dives in from the sky (Entrance) onto 'ground', from the side 'from' points to. An offering (an
        // item prefab name and count, or none) is taken only once he is really coming. Only one of him in the world.
        internal static string Summon(Player player, Vector3 ground, Vector3 from, string offering, int amount)
        {
            foreach (MonoBehaviour viking in _vikings)
                if (viking != null && IsNamed(viking))
                    return _name + " is already here, " +
                        Vector3.Distance(player.transform.position, viking.transform.position).ToString("0") + " m away.";
            if (AlfrikBoss.InWorld(out Vector3 elsewhere))
                return _name + " already walks Midgard, " + Vector3.Distance(player.transform.position, elsewhere).ToString("0") + " m away.";
            string offeringName = null;
            if (!string.IsNullOrEmpty(offering) && amount > 0)
            {
                ItemDrop item = ObjectDB.instance == null ? null : ObjectDB.instance.GetItemPrefab(offering)?.GetComponent<ItemDrop>();
                if (item == null)
                    return "The offering " + offering + " is not an item in this game ([Summoning] Offering).";
                offeringName = item.m_itemData.m_shared.m_name;
                if (player.GetInventory().CountItems(offeringName) < amount)
                    return Localization.instance.Localize("The stone wants " + amount + " " + offeringName + ".");
            }
            MethodInfo spawnInFront = AccessTools.Method(AccessTools.TypeByName("ValheimNpc.VikingSpawner"), "SpawnInFront",
                new[] { typeof(Player), typeof(int) });
            if (spawnInFront == null)
                return "Vikings for Hire has no VikingSpawner.SpawnInFront any more.";
            var before = new HashSet<MonoBehaviour>(_vikings);
            spawnInFront.Invoke(null, new object[] { player, 1 });
            MonoBehaviour spawned = _vikings.FirstOrDefault(viking => viking != null && !before.Contains(viking));
            ZNetView view = spawned == null ? null : spawned.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
                return "Vikings for Hire did not spawn a viking.";
            view.GetZDO().Set(NameKey, _name);
            view.GetZDO().Set(TraderKey, false);
            _named = spawned;
            _nextCheck = 0f;
            string made = AlfrikBoss.MakeBoss(spawned);
            if (offeringName != null)
                player.GetInventory().RemoveItem(offeringName, amount);
            Entrance.Begin(spawned.GetComponent<Humanoid>(), ground, from);
            return made;
        }

        internal static void Update()
        {
            if (Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + CheckInterval;

            if (_named == null || !IsNamed(_named))
            {
                _named = null;
                foreach (MonoBehaviour viking in _vikings)
                    if (viking != null && IsNamed(viking))
                    {
                        _named = viking;
                        break;
                    }
            }
            AlfrikBoss.Tick(_named);
            if (_failed)
                return;

            if (_wearer != null && _wearer != _named)
                TakeOff();
            if (_wearer == null && _named != null)
                PutOn(_named);
            if (_avatar == null)
                return;
            Camera camera = Utils.GetMainCamera();
            bool near = camera == null ||
                (camera.transform.position - _wearer.transform.position).sqrMagnitude <= DrawDistance * DrawDistance;
            if (near != _shown)
                Show(near);
        }

        private static bool IsNamed(MonoBehaviour viking) =>
            viking is TextReceiver named && string.Equals(named.GetText()?.Trim(), _name, StringComparison.OrdinalIgnoreCase);

        private static void PutOn(MonoBehaviour viking)
        {
            if (_template == null)
            {
                if (!_loading)
                    vrm::ValheimVRM.CoroutineHelper.Instance.StartCoroutine(Load());
                return;
            }
            Humanoid humanoid = viking.GetComponent<Humanoid>();
            Animator animator = humanoid == null ? null : humanoid.GetComponentInChildren<Animator>();
            if (animator == null)
                return;
            _wearer = viking;
            _animator = animator;
            _cullingMode = animator.cullingMode;
            animator.keepAnimatorStateOnDisable = true;

            _avatar = UnityEngine.Object.Instantiate(_template);
            _avatar.name = "VRM_Visual";
            _avatar.transform.SetParent(animator.transform.parent, false);
            _avatar.transform.localPosition = animator.transform.localPosition;
            _avatar.SetActive(true);
            vrm::ValheimVRM.VRMAnimationSync sync = _avatar.GetComponent<vrm::ValheimVRM.VRMAnimationSync>() ?? _avatar.AddComponent<vrm::ValheimVRM.VRMAnimationSync>();
            sync.Setup(animator, _settings);
            if (_settings.UseMToonShader && !_settings.AttemptTextureFix)
                (_avatar.GetComponent<vrm::ValheimVRM.MToonColorSync>() ?? _avatar.AddComponent<vrm::ValheimVRM.MToonColorSync>()).Setup(_avatar);
            _shown = false;
            Show(true);
        }

        // Far away the plain viking is drawn instead: the avatar has no LOD of its own.
        private static void Show(bool show)
        {
            _shown = show;
            _avatar.SetActive(show);
            if (_animator != null)
                _animator.cullingMode = show ? AnimatorCullingMode.AlwaysAnimate : _cullingMode;
            SetBodyHidden(show);
        }

        private static void TakeOff()
        {
            if (_avatar != null)
                UnityEngine.Object.Destroy(_avatar);
            if (_animator != null)
            {
                _animator.cullingMode = _cullingMode;
                SetBodyHidden(false);
            }
            _avatar = null;
            _animator = null;
            _wearer = null;
            _shown = false;
        }

        private static readonly string[] HeldItems = { "m_rightItemInstance", "m_leftItemInstance", "m_rightBackItemInstance", "m_leftBackItemInstance" };

        // His body, hair, beard and any armour piece. Weapons stay: a two-handed or dual weapon is an "attach_skin" item,
        // a skinned mesh on the body's bones, which the avatar's pose drives into its hands.
        private static void SetBodyHidden(bool hidden)
        {
            if (_animator == null)
                return;
            VisEquipment visual = _wearer == null ? null : _wearer.GetComponent<VisEquipment>();
            var weapons = new List<Transform>();
            if (visual != null)
                foreach (string field in HeldItems)
                    if (AccessTools.Field(typeof(VisEquipment), field)?.GetValue(visual) is GameObject held && held != null)
                        weapons.Add(held.transform);
            foreach (SkinnedMeshRenderer body in _animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool weapon = weapons.Any(held => body.transform == held || body.transform.IsChildOf(held));
                body.forceRenderingOff = hidden && !weapon;
                body.updateWhenOffscreen = hidden && !weapon;
            }
        }

        private static System.Collections.IEnumerator Load()
        {
            _loading = true;
            string path = System.IO.Path.Combine(vrm::ValheimVRM.Settings.ValheimVRMDir, _name + ".vrm");
            vrm::ValheimVRM.Settings.ReloadGlobalSettings();
            vrm::ValheimVRM.Settings.AddSettingsFromFile(_name, false);
            _settings = vrm::ValheimVRM.Settings.GetSettings(_name);
            if (vrm::ValheimVRM.VRMShaders.Shaders.Count == 0)
                vrm::ValheimVRM.VRMShaders.Initialize();

            byte[] bytes = null;
            System.Threading.Tasks.Task<byte[]> read = System.Threading.Tasks.Task.Run(() => System.IO.File.ReadAllBytes(path));
            while (!read.IsCompleted)
                yield return null;
            if (read.IsFaulted)
            {
                Fail("could not read " + path + ": " + read.Exception?.GetBaseException().Message);
                yield break;
            }
            bytes = read.Result;

            GameObject root = null;
            _findingShaders = true;
            yield return vrm::ValheimVRM.VRM.ImportVisualAsync(bytes, path, _settings.ModelScale, loaded => root = loaded);
            if (root == null)
            {
                _findingShaders = false;
                Fail("ValheimVRM could not import " + path);
                yield break;
            }
            GameObject player = ZNetScene.instance == null ? null : ZNetScene.instance.GetPrefab("Player");
            LODGroup sample = player == null ? null : player.GetComponentInChildren<LODGroup>();
            if (sample == null)
                sample = new GameObject("VRM_LODSample").AddComponent<LODGroup>();
            vrm::ValheimVRM.VRM registered = vrm::ValheimVRM.VrmManager.RegisterVrm(new vrm::ValheimVRM.VRM(root, _name), sample, null);
            _findingShaders = false;
            if (registered == null)
            {
                Fail("ValheimVRM could not register " + _name);
                yield break;
            }
            _template = registered.VisualModel;
            _loading = false;
            _nextCheck = 0f;
            Plugin.Log.LogInfo("VRM avatar '" + _name + "' loaded");
        }

        private static void Fail(string why)
        {
            _failed = true;
            _loading = false;
            Plugin.Log.LogWarning("VRM avatar '" + _name + "' turned off: " + why);
        }
    }
}
