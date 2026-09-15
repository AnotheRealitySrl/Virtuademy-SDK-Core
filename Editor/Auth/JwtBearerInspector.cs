using Newtonsoft.Json.Linq;

using System;
using System.Text;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Reads registered claims out of a Bearer JWT without validating its signature.
    ///
    /// Used to know when the editor session expires. The <c>exp</c> claim is preferred over any
    /// expiry we store alongside the token: it is the value the API actually enforces, it cannot
    /// drift out of sync with the token it belongs to, and it is available even for sessions that
    /// were saved before the expiry was tracked at all.
    ///
    /// No signature check happens here, and none is needed: the token is ours, the server is the
    /// one that validates it, and a tampered expiry would only make the editor refresh earlier.
    /// </summary>
    internal static class JwtBearerInspector
    {
        private static readonly DateTime unixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Extracts the <c>exp</c> claim as a UTC <see cref="DateTime"/>.
        /// Returns false when the string is not a JWT or carries no expiry.
        /// </summary>
        public static bool TryGetExpiry(string bearer, out DateTime expiryUtc)
        {
            expiryUtc = default;

            if (string.IsNullOrEmpty(bearer))
                return false;

            string[] segments = bearer.Split('.');
            if (segments.Length < 2)
                return false;

            try
            {
                string payloadJson = Encoding.UTF8.GetString(FromBase64Url(segments[1]));
                JToken exp = JObject.Parse(payloadJson)["exp"];
                if (exp == null)
                    return false;

                expiryUtc = unixEpoch.AddSeconds(exp.Value<long>());
                return true;
            }
            catch
            {
                // Not a JWT, not base64url, no numeric exp — the caller falls back to the
                // expiry reported by the profile API.
                return false;
            }
        }

        private static byte[] FromBase64Url(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');

            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            return Convert.FromBase64String(padded);
        }
    }
}
