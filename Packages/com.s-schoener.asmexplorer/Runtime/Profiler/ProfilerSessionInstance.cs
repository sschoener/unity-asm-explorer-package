using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AsmExplorer.Profiler
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    static class ProfilerSessionInstance
    {
        static string s_SessionDir;
        static string s_SessionOutputFileName;
        const string k_RecordingName = "__tmp_raw.kernel.etl";
        static string RecordingEtlFile => Path.Combine(s_SessionDir, k_RecordingName);
        static string OutputFile => Path.Combine(s_SessionDir, s_SessionOutputFileName);
        static bool s_SessionActive;
        public static bool SessionActive => s_SessionActive;

        static ProcessStartInfo XPerfStartInfo(string args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "xperf",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas"
            };
            return startInfo;
        }

        public static void SetupSession(string path)
        {
            StopSession();
            Process.Start(XPerfStartInfo("-setprofint 1221"));
            Process.Start(XPerfStartInfo("-on SysProf -stackwalk PROFILE -buffersize 1024 -minbuffers 300"));
            s_SessionActive = true;
            var p = Path.GetFullPath(path);
            s_SessionOutputFileName = Path.GetFileName(path);
            s_SessionDir = Path.GetDirectoryName(p);
            Debug.Log("Starting profiler session, recording to " + RecordingEtlFile);
        }

#if UNITY_EDITOR
        static ProfilerSessionInstance()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }
#else
        [RuntimeInitializeOnLoadMethod]
        static void InstallStopHandler()
        {
            Application.quitting += () => StopSession(true);
        }
#endif

        public static string StopSession(bool finish = true)
        {
            if (!s_SessionActive)
                return null;
            s_SessionActive = false;
            Debug.Log("Stopping profiler session");
            try
            {
                var info = XPerfStartInfo("-stop -d \"" + RecordingEtlFile + "\"");
                var proc = Process.Start(info);
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    Debug.LogError("Closing profiler session failed!");
                    Process.Start(XPerfStartInfo("-cancel"));
                    return null;
                }
            }
            catch
            {
                Process.Start(XPerfStartInfo("-cancel"));
                Debug.Log("Recording failed");
                throw;
            }

            if (finish)
            {
                FinishSession();
                return OutputFile;
            }

            return null;
        }

        static void FinishSession()
        {
            Debug.Log("Finishing profiler session, outputting to " + OutputFile);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.DisplayProgressBar("Finishing profiler session", "outputting to " + OutputFile, 0);
#endif
            try
            {
                using (var output = File.OpenWrite(OutputFile))
                    ProfilerDataSerialization.TranslateEtlFile(RecordingEtlFile, output);
            }
            finally
            {
#if UNITY_EDITOR
                UnityEditor.EditorUtility.ClearProgressBar();
#endif
            }
        }
#if UNITY_EDITOR
        static void BeforeAssemblyReload()
        {
            StopSession(false);
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
        }
#endif
    }
}
