using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mangos.Ttb.Commerce.Editor
{
    public static class TtbEditorBuildPreparation
    {
        public static string NodeExecutable()
        {
            var configured = Environment.GetEnvironmentVariable("MANGOS_NODE_PATH");
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
            return Application.platform == RuntimePlatform.WindowsEditor && File.Exists(installed) ? installed : "node";
        }

        public static void Prepare(MangosBuildProfile profile)
        {
            if (!TtbPortalPackaging.Enabled(profile)) return;
            profile.Validate();
            var script = Path.GetFullPath("Tools/TTB/prepare-overlay.mjs");
            if (!File.Exists(script)) throw new FileNotFoundException("The TTB UI build helper is missing.", script);
            var start = new ProcessStartInfo(NodeExecutable(), "\"" + script + "\" --environment " + profile.commerceEnvironment + " --experience world")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetFullPath(".")
            };
            try
            {
                if (!Application.isBatchMode) EditorUtility.DisplayProgressBar("MANGOs Build", "Preparing the latest TTB UI...", 0.1f);
                using var process = Process.Start(start);
                if (process == null) throw new IOException("The TTB UI build did not start. Check the Node.js installation.");
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                var log = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
                Directory.CreateDirectory("Library/TTB");
                File.WriteAllText("Library/TTB/overlay-build.log", log);
                if (process.ExitCode != 0 || !log.Contains("TTB_OVERLAY_PREPARED:"))
                    throw new IOException("TTB UI preparation failed. Open Library/TTB/overlay-build.log. Server/Client build was not started.");
                UnityEngine.Debug.Log("TTB_EDITOR_UI_READY: latest UI prepared; continuing Unity build.");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            finally { if (!Application.isBatchMode) EditorUtility.ClearProgressBar(); }
        }
    }
}
