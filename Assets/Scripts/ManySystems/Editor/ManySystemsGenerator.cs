using System;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static class ManySystemsGenerator
{
    [MenuItem("DOTS/Stress Test/Generate Many Systems")]
    public static void CreateSystems()
    {
        const string stressTestPath = "Scripts/ManySystems/";
        var assetsDir = Application.dataPath;
        var stressTestDir = Path.Combine(assetsDir, stressTestPath);
        var outputFile = Path.Combine(stressTestDir, "ManySystems_generated.cs");

        var includeFile = Path.Combine(stressTestDir, "ManySystems_includes.txt");
        var inputFile = Path.Combine(stressTestDir, "ManySystems_template.txt");
        var includeTemplate = File.ReadAllText(includeFile);
        var systemTemplate = File.ReadAllText(inputFile);
        const int numSystems = 1000;

        CreateSystems(systemTemplate, includeTemplate, outputFile, numSystems);
    }

    static void CreateSystems(string systemTemplate, string includeTemplate, string outputPath, int numCopies)
    {
        const string pattern = "{n}";
        if (File.Exists(outputPath))
            File.Delete(outputPath);
        string[] patternSplit = systemTemplate.Split(new[] { pattern }, StringSplitOptions.RemoveEmptyEntries);
        using (var fs = new StreamWriter(File.OpenWrite(outputPath)))
        {
            fs.WriteLine(includeTemplate);
            fs.WriteLine();
            for (int i = 0; i < numCopies; i++)
            {
                for (int p = 0; p < patternSplit.Length; p++)
                {
                    if (p > 0)
                        fs.Write(i);
                    fs.Write(patternSplit[p]);
                }
            }
            fs.Flush();
        }
        AssetDatabase.Refresh();
    }
}