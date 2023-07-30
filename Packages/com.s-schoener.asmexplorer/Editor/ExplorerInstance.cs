using UnityEditor;

namespace AsmExplorer
{
    [InitializeOnLoadAttribute]
    public static class ExplorerInstance
    {
        public const int Port = 8080;
        public static string URL => "http://127.0.0.1:" + Port + "/explorer/";
        static WebService s_WebService;

        static ExplorerInstance() {
            RestartWebservice();
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }

        static void BeforeAssemblyReload()
        {
            StopWebservice();
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        }

        public static bool WebServiceRunning => s_WebService != null;

        public static void EnsureWebservice() {
            if (!WebServiceRunning)
                RestartWebservice();
        }

        public static void RestartWebservice()
        {
            s_WebService?.Stop();
            s_WebService = new WebService(new Explorer(), "explorer", Port);
            s_WebService.Start();
        }
        
        public static void StopWebservice()
        {
            s_WebService?.Stop();
            s_WebService = null;
        }
    }
}