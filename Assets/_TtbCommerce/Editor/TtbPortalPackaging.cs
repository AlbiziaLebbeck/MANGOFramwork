using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace Mangos.Ttb.Commerce.Editor
{
    public static class TtbPortalPackaging
    {
        public static bool Enabled(MangosBuildProfile profile) => profile.commerceEnvironment != "None";

        public static void Preflight(MangosBuildProfile profile)
        {
            if (!Enabled(profile)) return;
            TtbProjectWorld.Validate();
            if (profile.network.clientHost == "multiplayer-plus.mangosgo.com" && profile.network.clientPath == "/systemtester")
                throw new InvalidOperationException("Select the TTB game host/path before building. The existing KKU systemtester default is not a TTB destination.");
            foreach (var path in new[] { "commerce/mango-ttb.js", "commerce/mango-ttb.css", "ttb-viewport.css", "TemplateData/style.css", "index.html" })
                if (!File.Exists("Assets/WebGLTemplates/MANGOsTTB/" + path))
                    throw new FileNotFoundException("Prepare the TTB overlay with Tools/TTB/prepare-overlay.ps1 first: " + path);
        }

        public static string ApiBase(string environment) => environment == "Uat"
            ? "https://ttb-api-test.mangosgo.com/ttb-commerce-test/v1"
            : environment == "Production" ? "https://ttb-api.mangosgo.com/ttb-commerce/v1" : "/ttb-commerce/v1";

        public static void CompleteClient(MangosBuildProfile profile, string output)
        {
            foreach (var path in new[] { "index.html", "commerce/mango-ttb.js", "commerce/mango-ttb.css", "ttb-viewport.css", "TemplateData/style.css" })
                if (!File.Exists(Path.Combine(output, path))) throw new IOException("The TTB client package is missing " + path);
            var config = new { environment = profile.commerceEnvironment, experience = "world", pwaEnabled = true,
                apiBase = ApiBase(profile.commerceEnvironment), assetBase = "./commerce/" };
            File.WriteAllText(Path.Combine(output, "ttb-commerce-config.js"),
                "window.MANGOS_TTB_CONFIG=Object.freeze(" + Newtonsoft.Json.JsonConvert.SerializeObject(config) + ");\n");
            // Package the same reviewed update/cache policy as the isolated world builds.
            var script = Path.GetFullPath("Tools/TTB/package-pwa.mjs");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("MANGOS_NODE_PATH") ?? "node",
                "\"" + script + "\" \"" + Path.GetFullPath(output) + "\"") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetFullPath(".") };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new IOException("The TTB PWA packager did not start.");
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new IOException("TTB PWA packaging failed: " + stderr);
                UnityEngine.Debug.Log("TTB_PORTAL_PWA_PACKAGED: " + stdout.Trim());
            }
        }

        public static void WriteEvidence(MangosBuildProfile profile, string output)
        {
            var evidence = new { environment = profile.commerceEnvironment, scene = TtbProjectWorld.ScenePath,
                sceneFingerprint = TtbProjectWorld.Fingerprint(), apiBase = ApiBase(profile.commerceEnvironment),
                template = "MANGOsTTB", portalSlugRouting = profile.network.portalRouting,
                bankCredentialsIncluded = false, bankPaymentAccepted = false };
            File.WriteAllText(Path.Combine(output, "ttb-commerce-build.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(evidence, Newtonsoft.Json.Formatting.Indented));
        }

        public static void CreateUploadArchives(MangosBuildProfile profile, string output, string[] completedTargets)
        {
            var directory = Path.Combine(output, "Upload");
            Directory.CreateDirectory(directory);
            var artifacts = completedTargets.Select(target => {
                var filename = "ttb-" + profile.commerceEnvironment.ToLowerInvariant() + "-" + target.ToLowerInvariant() + ".zip";
                var archive = Path.Combine(directory, filename);
                var source = Path.Combine(output, target);
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories).OrderBy(x => x))
                    {
                        var relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (parts.Any(part => part.EndsWith("_BurstDebugInformation_DoNotShip", StringComparison.Ordinal) ||
                            part.EndsWith("_BackUpThisFolder_ButDontShipItWithYourGame", StringComparison.Ordinal))) continue;
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("Portal archives cannot contain symlinks: " + relative);
                        zip.CreateEntryFromFile(file, relative.Replace('\\', '/'), System.IO.Compression.CompressionLevel.Optimal);
                    }
                using var sha = SHA256.Create();
                using var input = File.OpenRead(archive);
                return new { target, file = filename, bytes = input.Length,
                    sha256 = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant() };
            }).ToArray();
            File.WriteAllText(Path.Combine(directory, "portal-artifacts.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(new { portal = "https://deploy-plus.mangosgo.com", artifacts,
                    useSameSlugAndVersion = true, uploaded = false, bankPaymentAccepted = false }, Newtonsoft.Json.Formatting.Indented));
        }

        public static void WriteDeployment(MangosBuildProfile profile, string folder)
        {
            File.WriteAllText(Path.Combine(folder, "TTB-COMMERCE.txt"),
                "This package includes the editable project BaseWorld and TTB HTML overlay.\n" +
                "Environment: " + profile.commerceEnvironment + "\nCommerce API: " + ApiBase(profile.commerceEnvironment) + "\n" +
                "Client: serve the entire Client directory, including commerce/, StreamingAssets/, Build/ and PWA files.\n" +
                "Server: supply MANGOS_TTB_GAME_API, MANGOS_TTB_GAME_ENVIRONMENT, MANGOS_TTB_GAME_ID and MANGOS_TTB_GAME_KEY privately on the deployment host.\n" +
                "The game service identity must also be provisioned in the matching commerce backend. No key is embedded in this package.\n" +
                "For Uat inside ttb-uat, use the existing private API Service and separately provisioned game Secret.\n" +
                "Other deployment locations require explicit backend access and service identity configuration; the operator-only UAT allowlist is unchanged.\n" +
                (profile.network.portalRouting
                    ? "Portal routing is enabled: use the SAME slug/version for both archives. On plus.mangosgo.com the Client derives /slug[/version] from its page URL and uses WSS443 at multiplayer-plus.mangosgo.com; Server listens on WS7770.\n"
                    : "Configure the portal game WebSocket host/path/ports to match build-manifest.json. Do not reuse KKU routes.\n") +
                "A successful build does not enable bank payments or certify Production readiness.\n");
        }
    }
}
