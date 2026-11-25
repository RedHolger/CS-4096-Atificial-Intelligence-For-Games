using UnityEditor;
using UnityEngine;

namespace Orchard.EditorTools
{
    // browsers blend colours in gamma space; switching the project to gamma makes the
    // see-through shadows, rain, overlays and hp bars look exactly like the html version
    [InitializeOnLoad]
    public static class OrchardEditorSetup
    {
        static OrchardEditorSetup()
        {
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Gamma)
            {
                PlayerSettings.colorSpace = ColorSpace.Gamma;
                Debug.Log("Orchard: colour space set to Gamma (matches the browser).");
            }
        }
    }
}
