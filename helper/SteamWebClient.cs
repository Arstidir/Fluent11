using System;
using System.Net;
using System.Text;

namespace Fluent11Helper
{
    internal class SteamWebClient : WebClient
    {
        private const int TimeoutMs = 15000;

        public SteamWebClient()
        {
            Encoding = Encoding.UTF8;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            request.Timeout = TimeoutMs;
            if (request is HttpWebRequest http)
            {
                http.ReadWriteTimeout = TimeoutMs;
            }

            return request;
        }

        public static bool IsUnexpected(string json)
        {
            return string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{", StringComparison.Ordinal);
        }
    }
}
