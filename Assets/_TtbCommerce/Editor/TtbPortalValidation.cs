using System;
using UnityEditor;
using UnityEngine;

namespace Mangos.Ttb.Commerce.Editor
{
    public static class TtbPortalValidation
    {
        [MenuItem("MANGOs Framework/TTB/Validate Deployment Portal build profile")]
        public static void Run()
        {
            var profile = MangosBuildProfile.Load();
            if (!profile.network.portalRouting || profile.commerceEnvironment != "Uat")
                throw new InvalidOperationException("Select and save the Deployment Portal UAT profile first.");
            profile.Validate();
            TtbPortalPackaging.Preflight(profile);
            foreach (var row in new[] {
                new[] { "https://plus.mangosgo.com/ttb-store/", "/ttb-store" },
                new[] { "https://plus.mangosgo.com/ttb-store", "/ttb-store" },
                new[] { "https://plus.mangosgo.com/ttb-store/v2/", "/ttb-store/v2" },
                new[] { "https://plus.mangosgo.com/ttb-store/v2/index.html?build=1#shop", "/ttb-store/v2" } })
                if (!MangosRuntimeConfig.TryPortalPath(row[0], out var path) || path != row[1])
                    throw new InvalidOperationException("Portal slug/version routing failed.");
            foreach (var url in new[] { "http://plus.mangosgo.com/ttb-store/", "https://example.com/ttb-store/",
                "https://plus.mangosgo.com:444/ttb-store/", "https://plus.mangosgo.com/", "https://plus.mangosgo.com/a/b/c/",
                "https://plus.mangosgo.com/a%2fb/", "https://plus.mangosgo.com/Upper/", "https://user@plus.mangosgo.com/ttb-store/" })
                if (MangosRuntimeConfig.TryPortalPath(url, out _)) throw new InvalidOperationException("An unrelated portal route was accepted.");
            Debug.Log("TTB_PORTAL_PROFILE_VALIDATED: 12 routing cases; canonical BaseWorld; UAT API; WS7770/WSS443; no upload");
        }
    }
}
