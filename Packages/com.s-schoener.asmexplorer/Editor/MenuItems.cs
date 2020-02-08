using UnityEditor;
using UnityEngine;

namespace AsmExplorer
{
    public static class MenuItems {
        [MenuItem("Window/Asm Explorer/Inspect")]
        public static void Inspect()
        {
            ExplorerInstance.EnsureWebservice();
            Application.OpenURL(WebService.MakeCommandURL(ExplorerInstance.URL, WebServiceCommand.Inspect));
        }

        [MenuItem("Window/Asm Explorer/Lookup")]
        public static void Lookup()
        {
            ExplorerInstance.EnsureWebservice();
            Application.OpenURL(WebService.MakeCommandURL(ExplorerInstance.URL, WebServiceCommand.Lookup));
        }
    }
}