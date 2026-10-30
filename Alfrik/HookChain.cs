using UnityEngine;

namespace Alfrik
{
    // Bearded Hook's chain, on every game: from his hand to where it caught, the far end drawn in toward him over the
    // reel (the pull itself runs on the caught one's game, SE_AlfrikHook), then it falls slack and fades. The links are
    // drawn here.
    internal class HookChain : MonoBehaviour
    {
        private const float Reel = 0.55f, Fade = 0.25f, Stop = 2f;

        private static Material _material;
        private Humanoid _holder;
        private LineRenderer _line;
        private Vector3 _from;
        private float _start;

        internal static void Show(Humanoid holder, Vector3 caught)
        {
            if (holder == null)
                return;
            var chain = new GameObject("Alfrik_HookChain").AddComponent<HookChain>();
            chain._holder = holder;
            chain._from = caught;
            chain._start = Time.time;
        }

        private void Start()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial = Material;
            _line.textureMode = LineTextureMode.Tile;
            _line.widthMultiplier = 0.09f;
            _line.positionCount = 12;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.numCapVertices = 2;
        }

        private void Update()
        {
            float age = Time.time - _start;
            if (_holder == null || age > Reel + Fade)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 hand = Hatchet.HandPosition(_holder);
            Vector3 front = _holder.transform.position + _holder.transform.forward * Stop + Vector3.up * 1.1f;
            Vector3 end = Vector3.Lerp(_from, front, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / Reel)));
            // Taut while it reels, sagging once it lets go.
            float sag = age < Reel ? 0.05f : Mathf.Lerp(0.05f, 0.6f, (age - Reel) / Fade);
            for (int i = 0; i < _line.positionCount; i++)
            {
                float t = i / (float)(_line.positionCount - 1);
                _line.SetPosition(i, Vector3.Lerp(hand, end, t) + Vector3.down * sag * 4f * t * (1f - t));
            }
            float alpha = age < Reel ? 1f : 1f - (age - Reel) / Fade;
            var colour = new Color(0.75f, 0.72f, 0.7f, alpha);
            _line.startColor = colour;
            _line.endColor = colour;
        }

        // Dark iron links on transparent, tiling along the chain.
        private static Material Material
        {
            get
            {
                if (_material != null)
                    return _material;
                const int w = 64, h = 32;
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        // Two links per tile: an oval ring seen from the side, then one seen edge-on (a bar).
                        float u = (x + 0.5f) / w, v = (y + 0.5f) / h - 0.5f;
                        float a = 0f;
                        if (u < 0.6f)
                        {
                            float dx = (u - 0.3f) / 0.3f, dy = v / 0.42f;
                            float r = Mathf.Sqrt(dx * dx + dy * dy);
                            a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.78f) / 0.2f);
                        }
                        else
                            a = Mathf.Abs(v) < 0.1f ? 1f : 0f;
                        float shade = 0.35f + 0.5f * Mathf.Clamp01(0.5f - v);
                        pixels[y * w + x] = new Color(shade, shade * 0.97f, shade * 0.95f, a);
                    }
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Alfrik_chain" };
                texture.SetPixels32(pixels);
                texture.Apply(true);
                _material = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture, name = "Alfrik_chain" };
                return _material;
            }
        }
    }
}
