using System;
using UnityEngine;

namespace AsmExplorer {
    [ExecuteAlways]
    class AsmExplorerHelper : MonoBehaviour {
        static WebService s_WebService;

        static void RestartWebservice()
        {
            s_WebService?.Stop();
            s_WebService = new WebService(new Explorer(), "explorer");
            s_WebService.Start();
            UnityEngine.Debug.Log("(Re)Starting web service");
        }
        
        static void StopWebservice()
        {
            s_WebService?.Stop();
            s_WebService = null;
        }

        void Awake() {
            RestartWebservice();
        }

        void OnEnable()
        {
            RestartWebservice();
        }

        void OnDisable()
        {
            StopWebservice();
        }

        void OnDestroy()
        {
            StopWebservice();
        }

        void OnApplicationQuit() {
            StopWebservice();
        }
    }

}
