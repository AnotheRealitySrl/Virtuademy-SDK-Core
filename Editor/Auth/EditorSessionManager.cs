using Newtonsoft.Json;

using Virtuademy.SDK.TenantConfiguration;
using Virtuademy.SDK.Core.Utilities;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;

using SPACS.Utilities;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Owns the editor's authenticated session: acquires it, renews it before it expires, and
    /// replays a request once when the server rejects a token we believed was still good.
    ///
    /// Every editor tool that calls a Reflectis API must go through
    /// <see cref="SendAuthorizedAsync"/> (or at least await <see cref="EnsureValidTokenAsync"/>
    /// immediately before each request). Reading <c>EditorLoginState.BearerToken</c> once and
    /// reusing it across a long operation is exactly the bug this class exists to prevent: a
    /// build-and-deploy runs for minutes, and the token can die halfway through it.
    /// </summary>
    public static class EditorSessionManager
    {
        private static readonly HttpClient httpClient = new();

        private static readonly object renewalLock = new();
        private static Task<bool> renewalInFlight;

        /// <summary>
        /// Guarantees a usable token, renewing it if needed. Renewal is silent whenever the MSAL
        /// cache still holds a refresh token; otherwise it falls back to an interactive login when
        /// <paramref name="allowInteractive"/> allows it.
        ///
        /// Concurrent callers share a single renewal — N parallel requests finding the same
        /// expired token must not trigger N logins.
        /// </summary>
        /// <returns>False when there is no session at all, or the renewal failed. The caller must
        /// abort: no request made with the current token can succeed.</returns>
        public static Task<bool> EnsureValidTokenAsync(bool allowInteractive = true)
        {
            if (!EditorLoginState.HasSession)
            {
                Debug.LogError("[EditorSessionManager] Not logged in. Log in via 'Reflectis / Show available tenants'.");
                return Task.FromResult(false);
            }

            if (EditorLoginState.IsTokenValid)
                return Task.FromResult(true);

            lock (renewalLock)
            {
                if (renewalInFlight != null && !renewalInFlight.IsCompleted)
                    return renewalInFlight;

                renewalInFlight = RenewSessionAsync(allowInteractive);
                return renewalInFlight;
            }
        }

        /// <summary>
        /// Sends an authenticated request, attaching a freshly validated token.
        ///
        /// The request is built by <paramref name="requestFactory"/> rather than passed in
        /// because an <see cref="HttpRequestMessage"/> cannot be sent twice: the 401 retry needs
        /// to build a second one.
        /// </summary>
        /// <returns>The response, or null when the session could not be established — which is
        /// not the same as a failed request and callers should report it as such.</returns>
        public static async Task<HttpResponseMessage> SendAuthorizedAsync(Func<HttpRequestMessage> requestFactory,
                                                                         HttpClient client = null,
                                                                         bool allowInteractive = true)
        {
            if (requestFactory == null)
                throw new ArgumentNullException(nameof(requestFactory));

            if (!await EnsureValidTokenAsync(allowInteractive))
                return null;

            HttpClient targetClient = client ?? httpClient;

            HttpResponseMessage response = await SendOnceAsync(requestFactory, targetClient);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return response;

            // The server rejected a token our own expiry check considered valid: revoked,
            // rotated, or our clock disagrees with the API's. Renew once and replay.
            Debug.LogWarning("[EditorSessionManager] Request rejected with 401 — renewing the session and retrying once.");
            response.Dispose();
            EditorLoginState.MarkTokenExpired();

            if (!await EnsureValidTokenAsync(allowInteractive))
                return null;

            return await SendOnceAsync(requestFactory, targetClient);
        }

        /// <summary>
        /// Completes a login for an already-resolved tenant and auth config: acquires the Azure
        /// token, exchanges it for the tenant's Reflectis token, reads the role and stores the
        /// session together with everything a later renewal needs.
        /// </summary>
        public static async Task<bool> LoginAsync(Tenant tenant, AzureB2CConfig authConfig, string clientId,
                                                  string app, string env)
        {
            if (tenant?.Config == null)
                throw new ArgumentException("Tenant configuration is missing.", nameof(tenant));
            if (authConfig == null)
                throw new ArgumentNullException(nameof(authConfig));
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentException("Client id is missing.", nameof(clientId));

            InitAuthService(clientId, authConfig);
            string[] scopes = AzureAuthService.BuildScopes(authConfig);

            (string accessToken, string username) = await AzureAuthService.LoginInteractive(scopes);
            if (string.IsNullOrEmpty(accessToken))
            {
                Debug.LogError("[EditorSessionManager] Login failed: Azure returned no access token.");
                return false;
            }

            JwtToken apiToken = await FetchTenantTokenAsync(tenant, accessToken);
            if (apiToken == null)
                return false;

            bool isTenantManager = await CheckTenantManagerAsync(tenant, apiToken.Bearer);

            EditorLoginState.Set(apiToken.Bearer, apiToken.Expiry, tenant, username,
                                 isTenantManager, app, env, clientId, authConfig);

            Debug.Log($"[EditorSessionManager] Login successful for tenant: {tenant.Label}, user: {username}, " +
                      $"isTenantManager: {isTenantManager}, token expires at {DescribeExpiry()}.");
            return true;
        }

        /// <summary>
        /// Ends the session on both sides: the stored token and the MSAL refresh token.
        /// </summary>
        public static async Task LogoutAsync()
        {
            await AzureAuthService.SignOutAsync();
            EditorLoginState.Clear();
            Debug.Log("[EditorSessionManager] Logged out.");
        }

        /// <summary>
        /// Human-readable session state, for status labels.
        /// </summary>
        public static string DescribeExpiry()
        {
            DateTime? expiry = EditorLoginState.TokenExpiryUtc;
            if (expiry == null)
                return "unknown";

            return expiry.Value.ToLocalTime().ToString("HH:mm:ss");
        }

        private static async Task<HttpResponseMessage> SendOnceAsync(Func<HttpRequestMessage> requestFactory, HttpClient client)
        {
            using HttpRequestMessage request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", EditorLoginState.BearerToken);
            return await client.SendAsync(request);
        }

        private static async Task<bool> RenewSessionAsync(bool allowInteractive)
        {
            Tenant tenant = EditorLoginState.CurrentTenant;
            AzureB2CConfig authConfig = EditorLoginState.AuthConfig;
            string clientId = EditorLoginState.AuthClientId;

            if (!EditorLoginState.CanRenewSession)
            {
                // Sessions stored before the auth config was persisted cannot be renewed: the
                // client id and policy needed to talk to Azure were never written down.
                Debug.LogError("[EditorSessionManager] The stored session cannot be renewed automatically " +
                               "(it predates automatic token refresh). Log in again via 'Reflectis / Show available tenants'.");
                return false;
            }

            InitAuthService(clientId, authConfig);
            string[] scopes = AzureAuthService.BuildScopes(authConfig);

            (string accessToken, string username) = await AzureAuthService.TryAcquireTokenSilent(scopes);

            if (string.IsNullOrEmpty(accessToken))
            {
                if (!allowInteractive)
                {
                    Debug.LogError("[EditorSessionManager] The session expired and could not be renewed silently.");
                    return false;
                }

                // An interactive login opens the system browser. A modal progress bar left up by
                // the operation that triggered this would sit on top of it, unclosable.
                EditorUtility.ClearProgressBar();
                Debug.Log("[EditorSessionManager] The session expired and cannot be renewed silently — asking for a new login.");

                try
                {
                    (accessToken, username) = await AzureAuthService.LoginInteractive(scopes);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[EditorSessionManager] Interactive re-login failed: {ex.Message}");
                    return false;
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    Debug.LogError("[EditorSessionManager] Interactive re-login returned no access token.");
                    return false;
                }
            }

            JwtToken apiToken = await FetchTenantTokenAsync(tenant, accessToken);
            if (apiToken == null)
                return false;

            EditorLoginState.UpdateToken(apiToken.Bearer, apiToken.Expiry, username);
            Debug.Log($"[EditorSessionManager] Session renewed for tenant {tenant.Label}, token expires at {DescribeExpiry()}.");
            return true;
        }

        private static void InitAuthService(string clientId, AzureB2CConfig authConfig)
        {
            if (authConfig.IsEntraId)
                AzureAuthService.InitEntraId(clientId, authConfig.Tenant, authConfig.RedirectUri);
            else
                AzureAuthService.Init(clientId, authConfig.Tenant, authConfig.Policy, authConfig.RedirectUri);
        }

        /// <summary>
        /// Exchanges an Azure access token for the Reflectis token of this tenant's API label.
        /// </summary>
        private static async Task<JwtToken> FetchTenantTokenAsync(Tenant tenant, string accessToken)
        {
            string tokensJson;
            try
            {
                tokensJson = await AzureAuthService.GetUserDataAsync(tenant.Config.ProfileApiUrl, accessToken);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EditorSessionManager] Failed to call the profile API: {ex.Message}");
                return null;
            }

            if (string.IsNullOrEmpty(tokensJson))
            {
                Debug.LogError("[EditorSessionManager] The profile API returned no tokens.");
                return null;
            }

            JwtToken[] tokens;
            try
            {
                tokens = JsonConvert.DeserializeObject<JwtToken[]>(tokensJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EditorSessionManager] Could not parse the profile API tokens: {ex.Message}");
                return null;
            }

            JwtToken matchingToken = tokens?.FirstOrDefault(t => t.ApiLabel == tenant.Label);
            if (matchingToken == null)
                Debug.LogError($"[EditorSessionManager] No token found for API label: {tenant.Label}");

            return matchingToken;
        }

        /// <summary>
        /// The endpoint answers 200 only to tenant managers, so its status is the role check.
        /// A network failure is reported as "not a tenant manager": the UI it gates is additive.
        /// </summary>
        private static async Task<bool> CheckTenantManagerAsync(Tenant tenant, string bearer)
        {
            string applicationApiUrl = tenant.Config.ApplicationApiUrl;
            if (string.IsNullOrEmpty(applicationApiUrl))
                return false;

            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get,
                    $"{applicationApiUrl}/tenants/app/Unity/permissions/my?api-version=2");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

                using HttpResponseMessage response = await httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EditorSessionManager] Could not check the TenantManager role: {ex.Message}");
                return false;
            }
        }
    }
}
