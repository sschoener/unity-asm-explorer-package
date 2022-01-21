using System;
using System.IO;
using UnityEngine;

namespace AsmExplorer.Profiler {
    static class ScrewItConfig
    {
        public static string BasePath
        {
            get
            {
                var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ScrewIt");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }
    }
}
