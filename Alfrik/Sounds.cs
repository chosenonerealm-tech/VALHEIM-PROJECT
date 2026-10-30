using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace Alfrik
{
    // His own sounds, .ogg or .wav files in Audio/ next to this plugin (see CREDITS.txt), loaded once at start. A name
    // ending in a number is a variant (punch1..punch5 play as "punch", one picked at random). Effects play in 3D where
    // they happen and go through the game's sound-effect volume.
    internal static class Sounds
    {
        private static readonly Dictionary<string, List<AudioClip>> Clips = new Dictionary<string, List<AudioClip>>();
        private static AudioMixerGroup _sfx;
        private static bool _sfxSearched;

        internal static IEnumerator Load()
        {
            string folder = Path.Combine(Plugin.Folder, "Audio");
            if (!Directory.Exists(folder))
                yield break;
            var files = Directory.GetFiles(folder).Where(f => f.EndsWith(".ogg") || f.EndsWith(".wav")).OrderBy(f => f).ToList();
            foreach (string path in files)
            {
                AudioType type = path.EndsWith(".wav") ? AudioType.WAV : AudioType.OGGVORBIS;
                using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), type))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Plugin.Log.LogWarning("Sound " + path + " could not be loaded: " + request.error);
                        continue;
                    }
                    string name = Path.GetFileNameWithoutExtension(path);
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    clip.name = "Alfrik_" + name;
                    string key = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                    if (key.Length == 0 || name.StartsWith("music_"))
                        key = name;
                    if (!Clips.TryGetValue(key, out List<AudioClip> list))
                        Clips[key] = list = new List<AudioClip>();
                    list.Add(clip);
                }
            }
            Plugin.Log.LogInfo("Alfrik sounds loaded: " + string.Join(", ", Clips.Select(c => c.Key + (c.Value.Count > 1 ? " x" + c.Value.Count : ""))));
        }

        internal static AudioClip Clip(string name) =>
            Clips.TryGetValue(name, out List<AudioClip> list) && list.Count > 0 ? list[Random.Range(0, list.Count)] : null;

        // The game's sound-effect mixer, taken from any sound prefab, so his sounds follow the volume setting.
        private static AudioMixerGroup Sfx
        {
            get
            {
                if (_sfxSearched || ZNetScene.instance == null)
                    return _sfx;
                _sfxSearched = true;
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null || !prefab.name.StartsWith("sfx_"))
                        continue;
                    AudioSource source = prefab.GetComponentInChildren<AudioSource>(true);
                    if (source != null && source.outputAudioMixerGroup != null)
                    {
                        _sfx = source.outputAudioMixerGroup;
                        break;
                    }
                }
                return _sfx;
            }
        }

        private static AudioSource Source(GameObject host, AudioClip clip, float volume, float range)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 4f;
            source.maxDistance = range;
            source.dopplerLevel = 0f;
            source.volume = volume * Plugin.SoundVolume.Value;
            source.outputAudioMixerGroup = Sfx;
            return source;
        }

        // Voices do not pile up: a roar or growl is not started again until the last one has had its moment.
        private static readonly Dictionary<string, float> VoiceGap = new Dictionary<string, float>
        {
            ["roar"] = 4f, ["p2roar"] = 4f, ["growl"] = 5f, ["p2growl"] = 5f, ["yield"] = 3f,
        };
        private static readonly Dictionary<string, float> _voiceAgain = new Dictionary<string, float>();

        internal static void PlayAt(string name, Vector3 position, float volume = 1f, float range = 45f)
        {
            AudioClip clip = Clip(name);
            if (clip == null)
                return;
            if (VoiceGap.TryGetValue(name, out float gap))
            {
                if (_voiceAgain.TryGetValue(name, out float again) && Time.time < again)
                    return;
                _voiceAgain[name] = Time.time + gap;
            }
            var host = new GameObject("Alfrik_sound_" + name);
            host.transform.position = position;
            AudioSource source = Source(host, clip, volume, range);
            source.pitch = Random.Range(0.94f, 1.06f);
            source.Play();
            Object.Destroy(host, clip.length / source.pitch + 0.2f);
        }

        internal static AudioSource Loop(string name, Transform parent, float volume, float range)
        {
            AudioClip clip = Clip(name);
            if (clip == null || parent == null)
                return null;
            var host = new GameObject("Alfrik_loop_" + name);
            host.transform.SetParent(parent, false);
            AudioSource source = Source(host, clip, volume, range);
            source.loop = true;
            source.Play();
            return source;
        }
    }

    // His fight music on each player's own game: it rises while that player is within 60 m of him as a boss, falls when
    // he yields or the player leaves, and holds the game's own music down meanwhile (through the music volume setting).
    internal static class BossMusic
    {
        private const float Range = 60f, Fade = 2.5f;
        private static AudioSource _source;
        private static float _level;
        private static readonly AccessTools.FieldRef<MusicMan, AudioSource> GameMusic =
            AccessTools.FieldRefAccess<MusicMan, AudioSource>("m_musicSource");
        private static readonly AccessTools.FieldRef<MusicMan, float> GameMusicVolume =
            AccessTools.FieldRefAccess<MusicMan, float>("m_musicVolume");

        internal static void Apply(Harmony harmony) =>
            harmony.Patch(AccessTools.Method(typeof(MusicMan), "UpdateMusic"), postfix: new HarmonyMethod(typeof(BossMusic), nameof(HushGame)));

        internal static void Update()
        {
            Humanoid boss = AlfrikBoss.Char;
            Player me = Player.m_localPlayer;
            bool fighting = boss != null && me != null && AlfrikBoss.State == 1 && !boss.IsDead() &&
                Vector3.Distance(me.transform.position, boss.transform.position) < Range;
            // Each phase has its own track (Audio/music_phaseN.ogg, phase 1's when there is none); a new phase fades
            // the old track out and the new one in.
            AudioClip wanted = Sounds.Clip("music_phase" + Mathf.Max(1, AlfrikBoss.PhaseNumber)) ?? Sounds.Clip("music_phase1");
            bool switching = _source != null && wanted != null && _source.clip != wanted;
            _level = Mathf.MoveTowards(_level, fighting && !switching ? 1f : 0f, Time.deltaTime / Fade);
            if (_level <= 0f)
            {
                if (_source != null && _source.isPlaying)
                    _source.Stop();
                if (switching)
                    _source.clip = wanted;
                return;
            }
            if (_source == null)
            {
                AudioClip clip = wanted;
                if (clip == null)
                    return;
                var host = new GameObject("Alfrik_music");
                Object.DontDestroyOnLoad(host);
                _source = host.AddComponent<AudioSource>();
                _source.clip = clip;
                _source.loop = true;
                _source.spatialBlend = 0f;
                _source.outputAudioMixerGroup = MusicMan.instance != null ? MusicMan.instance.m_musicMixer : null;
            }
            if (!_source.isPlaying)
                _source.Play();
            // The game's music slider sets MusicMan's own volume, not the mixer, so it is applied here too.
            float slider = MusicMan.instance != null ? GameMusicVolume(MusicMan.instance) : 1f;
            _source.volume = _level * Plugin.MusicVolume.Value * slider;
        }

        private static void HushGame(MusicMan __instance)
        {
            if (_level <= 0f)
                return;
            AudioSource game = GameMusic(__instance);
            if (game != null)
                game.volume *= 1f - _level;
        }
    }
}
