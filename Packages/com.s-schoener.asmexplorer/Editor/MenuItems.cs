using System;
using System.IO;
using AsmExplorer.Profiler;
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


        [MenuItem("Window/Asm Explorer/Start Profiler Session")]
        public static void StartProfiler()
        {
            var profile = Path.Combine(Application.dataPath, "ProfileTrace.dat");
            ProfilerSessionInstance.SetupSession(profile);
        }

        [MenuItem("Window/Asm Explorer/Stop Profiler Session")]
        public static void StopProfiler()
        {
            ProfilerSessionInstance.StopSession();
        }
    }
}