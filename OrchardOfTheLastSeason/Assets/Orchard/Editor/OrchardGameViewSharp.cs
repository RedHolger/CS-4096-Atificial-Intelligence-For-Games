using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Orchard.EditorTools
{
    // keeps the Game view sharp on Retina / high-dpi screens.
    // unity's "Low Resolution Aspect Ratios" option renders the Game view at half resolution and
    // zooms it 2x (the "Scale 2x" you see), which makes the whole game look blurry.
    // this turns that option off when the project opens and every time you press Play.
    [InitializeOnLoad]
    public static class OrchardGameViewSharp
    {
        static OrchardGameViewSharp()
        {
            EditorApplication.delayCall += Sharpen;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode || s == PlayModeStateChange.ExitingEditMode) Sharpen();
            };
        }

        [MenuItem("Orchard/Make Game View Sharp")]
        public static void Sharpen()
        {
            try
            {
                Type gv = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gv == null) return;
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                PropertyInfo low = gv.GetProperty("lowResolutionForAspectRatios", F);
                bool changed = false, found = low != null && low.CanWrite;
                foreach (UnityEngine.Object w in Resources.FindObjectsOfTypeAll(gv))
                {
                    if (found)
                    {
                        if ((bool)low.GetValue(w, null)) { low.SetValue(w, false, null); changed = true; }
                    }
                    ((EditorWindow)w).Repaint();
                }
                if (!found)
                    Debug.Log("Orchard: for a sharp picture, open the Game view's aspect menu and untick \"Low Resolution Aspect Ratios\", then drag Scale to 1x.");
                else if (changed)
                    Debug.Log("Orchard: Game view set to full resolution (Low Resolution Aspect Ratios off) for a sharp picture.");
            }
            catch (Exception e)
            {
                Debug.Log("Orchard: could not change the Game view automatically (" + e.GetType().Name + "). Untick \"Low Resolution Aspect Ratios\" in the Game view's aspect menu.");
            }
        }
    }
}
