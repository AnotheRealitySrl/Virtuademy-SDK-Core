using Virtuademy.SDK.TenantConfiguration;
using Microsoft.Identity.Client;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using Debug = UnityEngine.Debug;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// What an Azure login returned. The account id is the MSAL home account id a connection is pinned to;
    /// it is not shown anywhere.
    /// </summary>
    public sealed class AzureTokenResult
    {
        public string AccessToken;

        /// <summary>The display name from the ID token, or the account's username (typically the email).</summary>
        public string Username;

        public string AccountId;
    }

    /// <summary>How an interactive login behaves: where the URL goes and how long each phase may take.</summary>
    public sealed class InteractiveLoginOptions
    {
        /// <summary>Called with the authorization URL as soon as MSAL has it. Anything that wants to show it does it here.</summary>
        public Action<string> OnAuthUrl;

        /// <summary>Open the system browser on the URL. Off for a caller that shows the URL to somebody else.</summary>
        public bool OpenBrowser = true;

        /// <summary>
        /// How long MSAL may take to issue the URL. Its first HTTP call (the authority's metadata) sometimes
        /// takes 15 to 20 seconds, and that wait is not the user's, so it has a budget of its own.
        /// </summary>
        public TimeSpan UrlBudget = TimeSpan.FromSeconds(60);

        /// <summary>How long the user has, counted from the moment the URL is issued.</summary>
        public TimeSpan UserBudget = TimeSpan.FromMinutes(3);
    }

    /// <summary>
    /// The browser logged in as another account than the connection is pinned to (single sign-on in the browser can
    /// answer with whoever is signed in there). A connection has one account: log out of it first to change it.
    /// </summary>
    public sealed class LoginAccountMismatchException : Exception
    {
        public LoginAccountMismatchException()
            : base("The browser signed in as another account than the one this connection is pinned to. " +
                   "Log out of the connection first if you mean to change its account.")
        {
        }
    }

    /// <summary>The interactive login ran out of time: <see cref="UrlIssued"/> says in which phase.</summary>
    public sealed class LoginTimeoutException : Exception
    {
        public bool UrlIssued { get; }

        public LoginTimeoutException(bool urlIssued, TimeSpan budget)
            : base(urlIssued
                ? $"Nobody finished the login in the browser within {budget.TotalSeconds:0} seconds of the URL being issued."
                : $"MSAL did not issue the login URL within {budget.TotalSeconds:0} seconds.")
        {
            UrlIssued = urlIssued;
        }
    }

    /// <summary>
    /// Azure authentication service using MSAL.
    /// Handles interactive login and token acquisition for both B2C and Entra ID.
    ///
    /// One MSAL client per app registration and authority, all of them on the one encrypted token cache of
    /// the connection store (see <see cref="EncryptedMsalCache"/>): the cache holds the accounts of every
    /// connection, so a domain reload no longer wipes silent renewal and no project has a cache of its own.
    /// A connection is pinned to its account: silent renewal asks for that account, never for the first one
    /// in the cache.
    /// </summary>
    public static class AzureAuthService
    {
        private static readonly Dictionary<string, IPublicClientApplication> clients = new();

        /// <summary>
        /// Runs an MSAL call on the thread pool. MSAL calls the cache hooks inline until its first real await, and the hooks
        /// wait for other processes' locks: that wait must not be the editor's main thread's.
        /// </summary>
        private static Task<T> OffMain<T>(Func<Task<T>> call) => Task.Run(call);

        /// <summary>
        /// The MSAL client for this app registration and authority, built once per process.
        /// </summary>
        public static IPublicClientApplication GetClient(string clientId, AzureB2CConfig authConfig)
        {
            if (string.IsNullOrEmpty(clientId))
                throw new ArgumentException("Client id is missing.", nameof(clientId));
            if (authConfig == null)
                throw new ArgumentNullException(nameof(authConfig));

            string authority = AuthorityOf(authConfig);
            string cacheKey = $"{clientId}|{authority}|{authConfig.RedirectUri}";

            lock (clients)
            {
                if (clients.TryGetValue(cacheKey, out IPublicClientApplication existing))
                    return existing;

                PublicClientApplicationBuilder builder = PublicClientApplicationBuilder
                    .Create(clientId)
                    .WithRedirectUri(authConfig.RedirectUri);

                // authConfig.RedirectUri is the one registered in Azure for this client ID (e.g. "http://localhost",
                // which MSAL turns into a free loopback port); it must match one of the URIs registered under
                // "Mobile and desktop applications" in the Azure portal.
                builder = authConfig.IsEntraId ? builder.WithAuthority(authority) : builder.WithB2CAuthority(authority);

                IPublicClientApplication client = builder.Build();
                EncryptedMsalCache.ForStore().Attach(client);

                clients[cacheKey] = client;
                return client;
            }
        }

        /// <summary>
        /// The authority URL of a config. Two connections that share a client id, an authority and an
        /// account share an MSAL account (see <see cref="ConnectionLoginService.LogoutAsync"/>).
        /// </summary>
        public static string AuthorityOf(AzureB2CConfig authConfig)
        {
            if (authConfig == null)
                throw new ArgumentNullException(nameof(authConfig));

            return authConfig.IsEntraId
                ? $"https://login.microsoftonline.com/{authConfig.Tenant}"
                : $"https://{authConfig.Tenant}.b2clogin.com/tfp/{authConfig.Tenant}.onmicrosoft.com/{authConfig.Policy}";
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
        /// Acquires an access token for the pinned account silently if it can, and with a login in the
        /// system browser when it cannot (or when the connection has no account yet).
        /// </summary>
        /// <param name="accountId">The MSAL home account id the connection is pinned to, or null for a first login.</param>
        /// <param name="cancellation">Cancels the login without it counting as a timeout.</param>
        /// <exception cref="LoginTimeoutException">The user (or MSAL, before the URL) took longer than the budget.</exception>
        public static async Task<AzureTokenResult> LoginInteractive(IPublicClientApplication client, string[] scopes, string accountId,
                                                                    InteractiveLoginOptions options, CancellationToken cancellation = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            options ??= new InteractiveLoginOptions();
            AuthenticationResult result = null;
            IAccount pinned = null;

            try
            {
                AzureTokenResult silent = await TryAcquireTokenSilent(client, scopes, accountId);
                if (silent != null)
                    return silent;

                if (!string.IsNullOrEmpty(accountId))
                    pinned = await OffMain(() => client.GetAccountAsync(accountId));

                bool urlIssued = false;
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(options.UrlBudget);

                SystemWebViewOptions webView = new()
                {
                    OpenBrowserAsync = url =>
                    {
                        // The user's time starts here, not at the call: see InteractiveLoginOptions.UrlBudget.
                        urlIssued = true;
                        timeout.CancelAfter(options.UserBudget);

                        string text = url.ToString();
                        options.OnAuthUrl?.Invoke(text);

                        if (options.OpenBrowser)
                            OpenInBrowser(text);

                        return Task.CompletedTask;
                    }
                };

                try
                {
                    AcquireTokenInteractiveParameterBuilder builder = client.AcquireTokenInteractive(scopes)
                        .WithUseEmbeddedWebView(false)
                        .WithSystemWebViewOptions(webView)
                        .WithExtraQueryParameters(new Dictionary<string, string>
                        {
                            { "nonce", Guid.NewGuid().ToString() }
                        });

                    // The pinned account goes to the browser as a hint, so that its single sign-on does not offer another one.
                    if (pinned != null)
                        builder = builder.WithAccount(pinned);

                    result = await OffMain(() => builder.ExecuteAsync(timeout.Token));
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    throw new LoginTimeoutException(urlIssued, urlIssued ? options.UserBudget : options.UrlBudget);
                }
            }
            catch (MsalException ex)
            {
                Debug.LogError($"MSAL Error: {ex.Message}");
                throw;
            }

            // The hint is only a hint: whoever the browser signed in is checked against the pinned account.
            if (!string.IsNullOrEmpty(accountId) && result.Account?.HomeAccountId?.Identifier != accountId)
                throw new LoginAccountMismatchException();

            if (!string.IsNullOrEmpty(result.AccessToken) && result.ExpiresOn <= DateTimeOffset.Now.AddMinutes(5))
            {
                IAccount signedIn = result.Account;
                result = await OffMain(() => client.AcquireTokenSilent(scopes, signedIn).ExecuteAsync());
            }

            return ToResult(result);
        }

        /// <summary>
        /// Acquires an access token for the pinned account only, never showing UI.
        /// Returns null when there is no such account or the refresh token is gone: the caller decides
        /// whether to escalate to an interactive login.
        /// </summary>
        public static async Task<AzureTokenResult> TryAcquireTokenSilent(IPublicClientApplication client, string[] scopes, string accountId)
        {
            if (client == null || string.IsNullOrEmpty(accountId))
                return null;

            try
            {
                IAccount account = await OffMain(() => client.GetAccountAsync(accountId));
                if (account == null)
                    return null;

                AuthenticationResult result = await OffMain(() => client.AcquireTokenSilent(scopes, account).ExecuteAsync());

                // A cached ID-only token has no access token to use.
                return string.IsNullOrEmpty(result.AccessToken) ? null : ToResult(result);
            }
            catch (MsalUiRequiredException)
            {
                // Expected whenever the refresh token expired or was revoked.
                return null;
            }
            catch (MsalException ex)
            {
                Debug.LogWarning($"[AzureAuthService] Silent token acquisition failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Removes the account from the cache, so the next login really prompts. Without this a
        /// "logout" would leave a usable refresh token behind.
        /// </summary>
        public static async Task RemoveAccountAsync(IPublicClientApplication client, string accountId)
        {
            if (client == null || string.IsNullOrEmpty(accountId))
                return;

            try
            {
                IAccount account = await OffMain(() => client.GetAccountAsync(accountId));
                if (account != null)
                    await OffMain(async () => { await client.RemoveAsync(account); return true; });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AzureAuthService] Could not remove the cached account: {ex.Message}");
            }
        }

        private static AzureTokenResult ToResult(AuthenticationResult result) => new()
        {
            AccessToken = result.AccessToken,
            Username = ExtractUsername(result),
            AccountId = result.Account?.HomeAccountId?.Identifier,
        };

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

        /// <summary>
        /// Opens the URL the way MSAL's own default does. Not <c>Application.OpenURL</c>: MSAL calls this on a
        /// thread of its own, and the engine's API is the main thread's.
        /// </summary>
        private static void OpenInBrowser(string url)
        {
            try
            {
#if UNITY_EDITOR_OSX
                Process.Start(new ProcessStartInfo("open", $"\"{url}\"") { UseShellExecute = false });
#elif UNITY_EDITOR_LINUX
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{url}\"") { UseShellExecute = false });
#else
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
#endif
            }
            catch (Exception ex)
            {
                // The URL was already handed to OnAuthUrl: whoever shows it can still finish the login.
                Debug.LogWarning($"[AzureAuthService] Could not open the browser: {ex.Message}");
            }
        }

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
