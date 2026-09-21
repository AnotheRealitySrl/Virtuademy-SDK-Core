using Virtuademy.SDK.TenantConfiguration;
using Microsoft.Identity.Client;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Azure authentication service using MSAL.
    /// Handles interactive login and token acquisition for both B2C and Entra ID.
    ///
    /// The MSAL token cache is persisted to disk (see <see cref="AttachPersistentCache"/>).
    /// Without that, the cache would live only in the static <c>_pca</c> field and every domain
    /// reload — a script recompile, entering play mode — would wipe it, making silent renewal
    /// impossible and turning every expired token into a browser prompt.
    /// </summary>
    public static class AzureAuthService
    {
        private static IPublicClientApplication _pca;
        private static string _currentClientId;
        private static string _currentTenant;
        private static string _currentPolicy;
        private static string _currentAuthType;

        private static string _currentRedirectUri;

        /// <summary>
        /// Initialize the MSAL client. Re-initializes if parameters change.
        /// </summary>
        /// <param name="redirectUri">
        /// The redirect URI registered in Azure B2C for this client ID (e.g. "http://localhost:10717/").
        /// Must exactly match one of the URIs registered under "Mobile and desktop applications" in the Azure portal.
        /// </param>
        public static void Init(string clientId, string tenant, string policy, string redirectUri)
        {
            // Re-initialize if parameters changed
            if (_pca != null
                && _currentAuthType == "B2C"
                && _currentClientId == clientId
                && _currentTenant == tenant
                && _currentPolicy == policy
                && _currentRedirectUri == redirectUri)
            {
                return;
            }

            _currentClientId = clientId;
            _currentTenant = tenant;
            _currentPolicy = policy;
            _currentAuthType = "B2C";
            _currentRedirectUri = redirectUri;

            string authority = $"https://{tenant}.b2clogin.com/tfp/{tenant}.onmicrosoft.com/{policy}";

            _pca = PublicClientApplicationBuilder
                .Create(clientId)
                .WithB2CAuthority(authority)
                .WithRedirectUri(redirectUri)
                .Build();

            AttachPersistentCache(_pca);
        }

        /// <summary>
        /// Initialize the MSAL client for Microsoft Entra ID authentication.
        /// </summary>
        /// <param name="tenantId">
        /// The Entra ID tenant identifier (GUID or domain, e.g. "contoso.onmicrosoft.com").
        /// </param>
        public static void InitEntraId(string clientId, string tenantId, string redirectUri)
        {
            if (_pca != null
                && _currentAuthType == "EntraID"
                && _currentClientId == clientId
                && _currentTenant == tenantId
                && _currentRedirectUri == redirectUri)
            {
                return;
            }

            _currentClientId = clientId;
            _currentTenant = tenantId;
            _currentPolicy = null;
            _currentAuthType = "EntraID";
            _currentRedirectUri = redirectUri;

            string authority = $"https://login.microsoftonline.com/{tenantId}";

            _pca = PublicClientApplicationBuilder
                .Create(clientId)
                .WithAuthority(authority)
                .WithRedirectUri(redirectUri)
                .Build();

            AttachPersistentCache(_pca);
        }

        /// <summary>
        /// Resets the MSAL client. Call before Init() to force re-initialization.
        /// Does not touch the on-disk cache — a later Init() picks it back up, which is what
        /// makes silent renewal survive domain reloads. Use <see cref="SignOutAsync"/> to
        /// actually end the Azure session.
        /// </summary>
        public static void Reset()
        {
            _pca = null;
            _currentClientId = null;
            _currentTenant = null;
            _currentPolicy = null;
            _currentAuthType = null;
            _currentRedirectUri = null;
        }

        /// <summary>
        /// The scopes the editor needs: an id token, a refresh token (offline_access, without
        /// which nothing can ever be renewed silently) and access to the profile API that
        /// exchanges the Azure token for the Virtuademy API tokens.
        /// </summary>
        public static string[] BuildScopes(AzureB2CConfig authConfig)
        {
            if (authConfig == null)
                throw new ArgumentNullException(nameof(authConfig));

            return new[]
            {
                "openid",
                "offline_access",
                $"https://{authConfig.Tenant}.onmicrosoft.com/{authConfig.ProfileApiId}/access"
            };
        }

        /// <summary>
        /// Acquires an access token, using silent acquisition if possible, falling back to interactive login.
        /// </summary>
        /// <returns>A tuple of (accessToken, username) where username is the display name from the ID token,
        /// falling back to the account username (typically email).</returns>
        public static async Task<(string AccessToken, string Username)> LoginInteractive(string[] scopes)
        {
            if (_pca == null)
            {
                throw new InvalidOperationException("AzureAuthService not initialized. Call Init() first.");
            }

            AuthenticationResult result = null;
            try
            {
                try
                {
                    var accounts = await _pca.GetAccountsAsync();
                    var firstAccount = accounts.FirstOrDefault();
                    if (firstAccount != null)
                    {
                        result = await _pca.AcquireTokenSilent(scopes, firstAccount)
                                          .ExecuteAsync();

                        // If silent returned no access token (cached ID-only token), force interactive
                        if (string.IsNullOrEmpty(result.AccessToken))
                        {
                            throw new MsalUiRequiredException("no_access_token", "Silent token has no access token");
                        }
                    }
                    else
                    {
                        throw new MsalUiRequiredException("no_account", "No cached account found");
                    }
                }
                catch (MsalUiRequiredException)
                {
                    result = await _pca.AcquireTokenInteractive(scopes)
                        .WithUseEmbeddedWebView(false)
                        .WithExtraQueryParameters(new Dictionary<string, string>
                        {
                            { "nonce", Guid.NewGuid().ToString() }
                        })
                        .ExecuteAsync();
                }
            }
            catch (MsalException ex)
            {
                Debug.LogError($"MSAL Error: {ex.Message}");
                throw;
            }

            if (!string.IsNullOrEmpty(result.AccessToken) && result.ExpiresOn <= DateTimeOffset.Now.AddMinutes(5))
            {
                result = await _pca.AcquireTokenSilent(scopes, result.Account).ExecuteAsync();
            }

            return (result.AccessToken, ExtractUsername(result));
        }

        /// <summary>
        /// Acquires an access token from the cached account only, never showing UI.
        /// Returns (null, null) when there is no usable cached account or the refresh token is
        /// gone — the caller decides whether to escalate to an interactive login.
        /// </summary>
        public static async Task<(string AccessToken, string Username)> TryAcquireTokenSilent(string[] scopes)
        {
            if (_pca == null)
                return (null, null);

            try
            {
                var accounts = await _pca.GetAccountsAsync();
                var firstAccount = accounts.FirstOrDefault();
                if (firstAccount == null)
                    return (null, null);

                AuthenticationResult result = await _pca.AcquireTokenSilent(scopes, firstAccount).ExecuteAsync();

                if (string.IsNullOrEmpty(result.AccessToken))
                    return (null, null);

                return (result.AccessToken, ExtractUsername(result));
            }
            catch (MsalUiRequiredException)
            {
                // Expected whenever the refresh token expired or was revoked.
                return (null, null);
            }
            catch (MsalException ex)
            {
                Debug.LogWarning($"[AzureAuthService] Silent token acquisition failed: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// Removes the cached accounts and deletes the on-disk cache, so the next login really
        /// prompts. Without this a "logout" would leave a usable refresh token behind.
        /// </summary>
        public static async Task SignOutAsync()
        {
            try
            {
                if (_pca != null)
                {
                    foreach (IAccount account in await _pca.GetAccountsAsync())
                        await _pca.RemoveAsync(account);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AzureAuthService] Could not remove cached accounts: {ex.Message}");
            }
            finally
            {
                DeleteCacheFiles();
                Reset();
            }
        }

        /// <summary>
        /// Prefer the "name" claim from the ID token, fall back to account username (email).
        /// Entra ID tokens commonly use "preferred_username" instead of "name".
        /// </summary>
        private static string ExtractUsername(AuthenticationResult result)
        {
            return result.ClaimsPrincipal?.FindFirst("name")?.Value
                ?? result.ClaimsPrincipal?.FindFirst("preferred_username")?.Value
                ?? result.ClaimsPrincipal?.FindFirst("given_name")?.Value
                ?? result.Account?.Username;
        }

        #region Token cache persistence

        // Project-local and outside version control, so the cache never follows the project
        // around. Losing it (a Library wipe) costs exactly one interactive login.
        private static string CacheDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "Virtuademy", "Auth"));

        /// <summary>
        /// Wires MSAL's cache to a file under Library/. Keyed by client id and authority so
        /// switching app or environment does not read another tenant's cache.
        ///
        /// NOTE: the blob holds the refresh token unencrypted. Unity's .NET Standard profile has
        /// no DPAPI (System.Security.Cryptography.ProtectedData), so this matches what the editor
        /// already does with the bearer token in EditorPrefs. It is per-user and per-project, but
        /// it is not a secret store.
        /// </summary>
        private static void AttachPersistentCache(IPublicClientApplication app)
        {
            string cacheFilePath = Path.Combine(CacheDirectory, $"msal_{Sanitize(_currentAuthType)}_{Sanitize(_currentClientId)}_{Sanitize(_currentTenant)}.bin");

            app.UserTokenCache.SetBeforeAccess(args =>
            {
                try
                {
                    if (File.Exists(cacheFilePath))
                        args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(cacheFilePath), shouldClearExistingCache: true);
                }
                catch (Exception ex)
                {
                    // A corrupt or unreadable cache must not break login: it just means the user
                    // authenticates interactively once more.
                    Debug.LogWarning($"[AzureAuthService] Could not read the token cache: {ex.Message}");
                }
            });

            app.UserTokenCache.SetAfterAccess(args =>
            {
                if (!args.HasStateChanged)
                    return;

                try
                {
                    Directory.CreateDirectory(CacheDirectory);
                    File.WriteAllBytes(cacheFilePath, args.TokenCache.SerializeMsalV3());
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AzureAuthService] Could not write the token cache: {ex.Message}");
                }
            });
        }

        private static void DeleteCacheFiles()
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                    return;

                foreach (string file in Directory.GetFiles(CacheDirectory, "msal_*.bin"))
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AzureAuthService] Could not delete the token cache: {ex.Message}");
            }
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "none";

            StringBuilder sb = new(value.Length);
            foreach (char c in value)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');

            return sb.ToString();
        }

        #endregion

        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Calls the profile API to get JWT tokens for all API labels.
        /// </summary>
        public static async Task<string> GetUserDataAsync(string apiBaseUrl, string accessToken)
        {
            // Ensure the base URL always has a trailing slash so the relative path appends correctly
            if (!apiBaseUrl.EndsWith("/"))
                apiBaseUrl += "/";

            string apiUrl = $"{apiBaseUrl}my/tokens";

            // Use a per-request HttpRequestMessage to avoid mutating shared DefaultRequestHeaders
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request);
            }
            catch (HttpRequestException ex)
            {
                string inner = ex.InnerException != null ? $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : string.Empty;
                Debug.LogError($"[AzureAuthService] Network error calling {apiUrl}: {ex.Message}{inner}");
                throw;
            }

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                Debug.LogError($"[AzureAuthService] API Error {(int)response.StatusCode} ({response.ReasonPhrase}) from {apiUrl}: {body}");
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
    }
}
