using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Orchard.EditorTools
{
    // makes the game view sharp on retina screens
    // unity option "Low Resolution Aspect Ratios" draws the game at half size and
    // zooms it (the Scale 2x in the game view). this makes all text blurry.
    // this script turns the option off when the project opens and when you press play
    [InitializeOnLoad]
    public static class OrchardGameViewSharp
    {
        static OrchardGameViewSharp()
        {
            EditorApplication.delayCall += Sharpen;
            EditorApplication.playModeStateChanged += OnPlayChange;
        }

        static void OnPlayChange(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode || s == PlayModeStateChange.ExitingEditMode)
            {
                Sharpen();
            }
        }

        [MenuItem("Orchard/Make Game View Sharp")]
        public static void Sharpen()
        {
            try
            {
                Type gv = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gv == null) return;
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                PropertyInfo low = gv.GetProperty("lowResolutionForAspectRatios", flags);
                if (low == null || !low.CanWrite)
                {
                    Debug.Log("Orchard: in the Game view untick Low Resolution Aspect Ratios and set Scale to 1x.");
                    return;
                }
                UnityEngine.Object[] views = Resources.FindObjectsOfTypeAll(gv);
                for (int i = 0; i < views.Length; i++)
                {
                    if ((bool)low.GetValue(views[i], null))
                    {
                        low.SetValue(views[i], false, null);
                        Debug.Log("Orchard: Game view is now full resolution.");
                    }
                    ((EditorWindow)views[i]).Repaint();
                }
            }
            catch (Exception e)
            {
                Debug.Log("Orchard: could not change the Game view (" + e.GetType().Name + "). Untick Low Resolution Aspect Ratios by hand.");
            }
        }
    }
}
