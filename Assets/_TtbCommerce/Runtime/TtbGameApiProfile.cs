using System;

namespace Mangos.Ttb.Commerce
{
    public static class TtbGameApiProfile
    {
        // The private HTTP exception is exactly one UAT Service, selected only in the server environment.
        // Browser config and RPCs cannot change the endpoint or this profile.
        public static bool TryResolve(string configured, bool localEnabled, string environment, out string result)
        {
            result = null;
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
            if (environment == "Uat")
            {
                if (localEnabled) return false;
                var privateUat = uri.Scheme == "http" && uri.Host == "ttb-commerce-api.ttb-uat.svc.cluster.local" && uri.Port == 8080 && uri.AbsolutePath == "/ttb-commerce-test/v1";
                var publicUat = uri.Scheme == "https" && uri.IsDefaultPort && uri.Host == "ttb-api-test.mangosgo.com" && uri.AbsolutePath == "/ttb-commerce-test/v1";
                if (!privateUat && !publicUat) return false;
            }
            else
            {
                if (!string.IsNullOrEmpty(environment) && environment != "Local" && environment != "Production") return false;
                var local = localEnabled && environment != "Production" && uri.Scheme == "http" && uri.IsLoopback && uri.Port == 5286 && uri.AbsolutePath == "/ttb-commerce/v1";
                var bank = uri.Scheme == "https" && uri.IsDefaultPort &&
                    (uri.Host == "ttb-api-test.mangosgo.com" && uri.AbsolutePath == "/ttb-commerce-test/v1" ||
                     uri.Host == "ttb-api.mangosgo.com" && uri.AbsolutePath == "/ttb-commerce/v1");
                if (environment == "Production" && uri.Host != "ttb-api.mangosgo.com") return false;
                if (!local && !bank) return false;
            }
            result = uri.AbsoluteUri.TrimEnd('/'); return true;
        }
    }
}
