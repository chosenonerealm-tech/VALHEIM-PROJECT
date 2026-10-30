using UnityEngine;

namespace Alfrik
{
    // His aura, drawn on every game with particle systems built here (the game has no steam effect to borrow): steam
    // rising off his shoulders and back in phase 1, red veins and glowing runes in phase 2 (on the avatar only), and
    // always a red glow in his one eye (his left; the patch covers the right).
    internal static class Aura
    {
        private static GameObject _steam, _eye, _steamFor, _eyeFor, _runesFor;
        private static bool _blood;
        private static AudioSource _heartbeat;
        private static Material _soft;

        internal static void Update(bool steam, bool eye, Humanoid body, GameObject avatar, bool blood, bool runes = false)
        {
            if (body == null)
            {
                Clear();
                return;
            }
            Runes(runes ? avatar : null);
            if (!steam || Plugin.SteamAmount.Value <= 0f)
            {
                Destroy(ref _steam);
                _steamFor = null;
            }
            else
            {
                if (_steamFor != body.gameObject || _steam == null)
                {
                    Destroy(ref _steam);
                    _steamFor = body.gameObject;
                    _steam = MakeSteam(body.transform);
                    _blood = !blood;
                }
                FollowChest(body.transform, avatar);
            }
            if (!eye)
            {
                Destroy(ref _eye);
                _eyeFor = null;
            }
            else if (_eyeFor != avatar || (avatar != null && _eye == null))
            {
                Destroy(ref _eye);
                _eyeFor = avatar;
                if (avatar != null)
                    _eye = MakeEye(avatar);
                _blood = !blood;
            }
            if (blood != _blood || (blood && (_heartbeat == null)))
                Blood(blood, body.transform);
        }

        // The steam rises off the avatar's chest, so it follows the model's size, crouch and lean; on the plain viking
        // (avatar not drawn, far away) it sits at chest height on the body.
        private static void FollowChest(Transform body, GameObject avatar)
        {
            Animator animator = avatar == null ? null : avatar.GetComponent<Animator>();
            Transform chest = animator == null || !animator.isHuman ? null
                : animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) ??
                  animator.GetBoneTransform(HumanBodyBones.Spine);
            Transform parent = chest != null ? chest : body;
            if (_steam.transform.parent == parent)
                return;
            _steam.transform.SetParent(parent, false);
            _steam.transform.localPosition = chest != null ? Vector3.zero : new Vector3(0f, 1.35f, -0.05f);
            _steam.transform.rotation = body.rotation;
        }

        // Bloodscent: the steam turns blood red, the eye flares and a heartbeat thuds; back to normal when it ends.
        private static void Blood(bool on, Transform body)
        {
            if (_steam != null)
            {
                ParticleSystem.MainModule main = _steam.GetComponent<ParticleSystem>().main;
                main.startColor = on ? new Color(0.75f, 0.06f, 0.04f, 0.35f) : new Color(0.9f, 0.9f, 0.9f, 0.22f);
            }
            if (_eye != null)
            {
                Light light = _eye.GetComponent<Light>();
                if (light != null)
                    light.intensity = on ? 5f : 2f;
                _eye.transform.localScale = Vector3.one * (on ? 1.8f : 1f);
            }
            if (on && _heartbeat == null && body != null)
                _heartbeat = Sounds.Loop("heartbeat", body, 0.6f, 25f);
            if (!on && _heartbeat != null)
            {
                Object.Destroy(_heartbeat.gameObject);
                _heartbeat = null;
            }
            _blood = on;
        }

        internal static void Clear()
        {
            Destroy(ref _steam);
            Destroy(ref _eye);
            _steamFor = _eyeFor = null;
            Runes(null);
            Blood(false, null);
        }

        private static readonly HumanBodyBones[] RuneBones =
        {
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.Chest, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        };

        // Phase 2: red veins (the avatar's rim light turned to a pulsing blood red, AuraVeins) and runes lighting up and
        // fading on his arms, chest and thighs. Gone when the avatar is not drawn or the phase ends.
        private static void Runes(GameObject avatar)
        {
            if (avatar == _runesFor && (avatar == null || avatar.GetComponent<AuraVeins>() != null))
                return;
            if (_runesFor != null)
            {
                AuraVeins old = _runesFor.GetComponent<AuraVeins>();
                if (old != null)
                    Object.Destroy(old);
            }
            _runesFor = avatar;
            if (avatar == null)
                return;
            AuraVeins veins = avatar.AddComponent<AuraVeins>();
            Animator animator = avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
                return;
            foreach (HumanBodyBones bone in RuneBones)
            {
                Transform at = animator.GetBoneTransform(bone);
                if (at == null)
                    continue;
                var host = new GameObject("Alfrik_Rune");
                host.transform.SetParent(at, false);
                ParticleSystem system = Particles(host, new Color(1f, 0.15f, 0.05f, 0.9f), 0.11f, 1.4f, 4, false, Textures.Get(Textures.Runes));
                Textures.RuneSheet(system);
                ParticleSystem.EmissionModule emission = system.emission;
                emission.rateOverTime = 1.3f;
                ParticleSystem.ShapeModule shape = system.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.07f;
                ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
                fade.color = gradient;
                system.Play();
                veins.Marks.Add(host);
            }
        }

        private static void Destroy(ref GameObject go)
        {
            if (go != null)
                Object.Destroy(go);
            go = null;
        }

        // A soft round puff, white, fading to nothing at the edge; tinted per particle.
        internal static Material Soft
        {
            get
            {
                if (_soft != null)
                    return _soft;
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                    }
                texture.SetPixels32(pixels);
                texture.Apply();
                _soft = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture, name = "Alfrik_soft" };
                return _soft;
            }
        }

        internal static ParticleSystem Particles(GameObject host, Color colour, float size, float lifetime, int max, bool world, Material material = null)
        {
            ParticleSystem system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startSpeed = 0f;
            main.startColor = colour;
            main.maxParticles = max;
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material != null ? material : Soft;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static GameObject MakeSteam(Transform body)
        {
            var steam = new GameObject("Alfrik_Steam");
            steam.transform.SetParent(body, false);
            steam.transform.localPosition = new Vector3(0f, 1.35f, -0.05f);
            float amount = Plugin.SteamAmount.Value;
            ParticleSystem system = Particles(steam, new Color(0.9f, 0.9f, 0.9f, 0.22f), 0.35f, 1.8f, Mathf.RoundToInt(40 * amount), true, Textures.Get("p1_puff"));
            ParticleSystem.MainModule main = system.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.gravityModifier = -0.04f;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 11f * amount;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.6f, 0.5f, 0.35f);
            ParticleSystem.SizeOverLifetimeModule growth = system.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            system.Play();
            Sounds.Loop("steam", steam.transform, 0.35f, 12f);
            return steam;
        }

        private static GameObject MakeEye(GameObject avatar)
        {
            Animator animator = avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
                return null;
            Transform bone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (bone == null && head == null)
                return null;
            Transform root = avatar.transform;
            var eye = new GameObject("Alfrik_Eye");
            eye.transform.SetParent(head != null ? head : bone, false);
            // On the face in front of the eyeball. Without eye bones it is worked out from the head bone, which on a VRM
            // model sits at the base of the skull: the eyes are about 11 cm above it and 9 cm in front, scaled with him.
            float size = Mathf.Max(root.lossyScale.y, 0.01f);
            Vector3 adjust = Plugin.EyeAdjust.Value;
            eye.transform.position = (bone != null
                    ? bone.position + root.forward * 0.03f * size
                    : head.position + (root.forward * 0.09f + root.up * 0.11f - root.right * 0.034f) * size)
                + root.right * adjust.x + root.up * adjust.y + root.forward * adjust.z;
            Plugin.Log.LogInfo("Alfrik's eye glow is placed from the avatar's " + (bone != null ? "left eye bone" : "head bone (no eye bones)") +
                ", " + (eye.transform.position - (head != null ? head.position : bone.position)).ToString("F3") + " from the head");

            ParticleSystem system = Particles(eye, new Color(1f, 0.08f, 0.02f, 0.9f), 0.07f, 0.12f, 6, false, Textures.Get("p1_glow"));
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 30f;
            system.Play();

            Light light = eye.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.12f, 0.04f);
            light.range = 0.6f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
            return eye;
        }
    }
}
