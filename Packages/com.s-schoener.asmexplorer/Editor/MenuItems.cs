using UnityEditor;
using UnityEngine;

namespace AsmExplorer
{
    public static class MenuItems {
        [MenuItem("Window/Asm Explorer")]
        public static void AsmExplorer()
        {
            ExplorerInstance.EnsureWebservice();
            Application.OpenURL("http://localhost:8080/explorer/");
        }
    }
}