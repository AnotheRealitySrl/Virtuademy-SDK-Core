using Virtuademy.SDK.Core.Utilities;
using Virtuademy.SDK.Http;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.Networking;

using Virtuademy.SDK.Core.Authentication;

using Virtuademy.SDK.Core;

using SPACS.Utilities;

namespace Virtuademy.SDK.Core.ApiSystem
{
    /// <summary>
    /// Builds authenticated API requests. Static, and self-contained: a credential, a base URL
    /// and an endpoint are all it needs.
    /// </summary>
    /// <remarks>
    /// This is the whole transport, which makes it the API surface of this assembly for anyone
    /// outside the Unity system framework — an external app or a creator's editor tooling can
    /// call <see cref="BuildRequest"/>, <see cref="IsAlive"/> and <see cref="GetApiInfo"/>
    /// directly, with no bootstrap of any kind. <c>ApiSystemBase</c> is a convenience over
    /// these three, holding the configuration as a serialized asset and resolving a token
    /// provider; it is a system, and lives with the framework rather than here.
    /// </remarks>
    public static class ApiHelper
    {
        public static (string timestamp, string hmac) CalculateHmacHeader(HmacCredential credential, DateTime timestamp)
        {
            string appId = credential.AppId.ToString().ToUpperInvariant();
            string timestampFormatted = timestamp.ToString("yyyy-MM-ddTHH:mm:ss'z'", CultureInfo.InvariantCulture).ToUpperInvariant();
            byte[] keyBytes = Encoding.UTF8.GetBytes(credential.AppSecret);
            using HMACSHA256 sha256 = new(keyBytes);
            byte[] hmac = sha256.ComputeHash(Encoding.UTF8.GetBytes($"{appId}:{timestampFormatted}"));

            return (timestampFormatted, Convert.ToBase64String(hmac));
        }

        /// <summary>
        /// Builds an authenticated UnityWebRequest.
        /// </summary>
        public static UnityWebRequest BuildRequest(
            string method,
            string endpoint,
            AppIdentification apiConfig,
            Dictionary<string, string> queryParams = null,
            HttpHelper.ERequestBodyType requestBodyType = HttpHelper.ERequestBodyType.RawString,
            object body = null,
            EAuthentication authentication = EAuthentication.BearerAndHmac,
            bool allowEmptyQueryValues = false,
            Dictionary<string, string> additionalHeaders = null,
            JwtToken jwtToken = null,
            TimeSpan? serverTimeOffset = null,
            bool secureConnection = true,
            bool allowUntrustedServers = false)
        {
            TimeSpan offset = serverTimeOffset ?? TimeSpan.Zero;

            queryParams ??= new Dictionary<string, string>();
            queryParams = queryParams
                .Where(x => allowEmptyQueryValues ? x.Value != null : !string.IsNullOrWhiteSpace(x.Value))
                .ToDictionary(x => x.Key, x => x.Value);

            if (!string.IsNullOrEmpty(apiConfig.ApiVersion))
            {
                queryParams["api-version"] = apiConfig.ApiVersion;
            }

            (string timestamp, string hmac) = CalculateHmacHeader(apiConfig.Credential, DateTime.UtcNow - offset);

            Dictionary<string, string> headers = new()
            {
                { "AppId", apiConfig.Credential.AppId.ToString() },
                { "Timestamp", timestamp },
            };

            if (additionalHeaders != null)
            {
                foreach (var kvp in additionalHeaders)
                {
                    headers[kvp.Key] = kvp.Value;
                }
            }

            if (authentication.HasFlag(EAuthentication.Bearer))
            {
                if (jwtToken == null)
                {
                    Debug.LogWarning("[ApiHelper] Bearer authentication requested but no JwtToken provided.");
                }
                else
                {
                    headers["Authorization"] = $"Bearer {jwtToken.Bearer}";
                }
            }

            if (authentication.HasFlag(EAuthentication.Hmac))
            {
                headers["Hmac"] = hmac;
            }

            CertificateHandler certificateHandler = allowUntrustedServers ? new AcceptAllCertificates() : default;

            UnityWebRequest request = HttpHelper.CreateHttpRequest(
                method,
                $"{apiConfig.ApiBaseUrl}/{endpoint}",
                requestBodyType,
                body,
                queryParams,
                headers,
                certificateHandler,
                secureConnection);

            return request;
        }

        /// <summary>
        /// Checks if the API server is alive.
        /// </summary>
        public static async Task<bool> IsAlive(AppIdentification apiConfig, bool secureConnection = true)
        {
            using UnityWebRequest request = BuildRequest(
                UnityWebRequest.kHttpVerbGET, "health", apiConfig,
                authentication: EAuthentication.None,
                secureConnection: secureConnection);
            await request.SendWebRequest();
            return request.result == UnityWebRequest.Result.Success;
        }

        /// <summary>
        /// Gets API server info (label, version, server time).
        /// </summary>
        public static async Task<ApiResponse<ApiInfo>> GetApiInfo(AppIdentification apiConfig, bool secureConnection = true)
        {
            using UnityWebRequest request = BuildRequest(
                UnityWebRequest.kHttpVerbGET, "apiserver/info", apiConfig,
                authentication: EAuthentication.None,
                secureConnection: secureConnection);
            await request.SendWebRequest();
            return new ApiResponse<ApiInfo>(request.responseCode, request.error, request.downloadHandler.text);
        }
    }
}
