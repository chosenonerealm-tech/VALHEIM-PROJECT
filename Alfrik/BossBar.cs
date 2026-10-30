using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Alfrik
{
    // His emblem beside his name on the boss health bar at the top of the screen (the game's boss bar has no icon).
    internal static class BossBar
    {
        private const string Child = "Alfrik_Emblem";
        private static readonly AccessTools.FieldRef<EnemyHud, IDictionary> Huds = AccessTools.FieldRefAccess<EnemyHud, IDictionary>("m_huds");

        internal static void Apply(Harmony harmony) =>
            harmony.Patch(AccessTools.Method(typeof(EnemyHud), "ShowHud"), postfix: new HarmonyMethod(typeof(BossBar), nameof(Shown)));

        // EnemyHud.ShowHud postfix: the bar it just made (or already had) for him gets the emblem once.
        private static void Shown(EnemyHud __instance, Character c)
        {
            if (c == null || c != AlfrikBoss.Char || !c.IsBoss())
                return;
            IDictionary huds = Huds(__instance);
            object data = huds != null && huds.Contains(c) ? huds[c] : null;
            GameObject gui = data == null ? null : Traverse.Create(data).Field("m_gui").GetValue<GameObject>();
            if (gui == null)
                return;
            Transform health = gui.transform.Find("Health") ?? gui.transform;
            if (health.Find(Child) != null)
                return;
            var emblem = new GameObject(Child, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)emblem.transform;
            rect.SetParent(health, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f, 6f);
            rect.sizeDelta = new Vector2(64f, 64f);
            Image image = emblem.GetComponent<Image>();
            image.sprite = Textures.Emblem;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
    }
}
