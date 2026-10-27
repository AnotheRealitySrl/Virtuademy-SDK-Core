using Newtonsoft.Json;

using Virtuademy.SDK.Core.ApiSystem;
using Virtuademy.SDK.Core.Utilities;
using Virtuademy.SDK.Http;

using Microsoft.Identity.Client;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>How a login (or the resolution before it) is carried out.</summary>
    public sealed class LoginOptions
    {
        /// <summary>
        /// Allow the system browser. False renews silently or not at all, which is all a batch run
        /// without a person at it can do unless it is the login command itself.
        /// </summary>
        public bool Interactive = true;

        /// <summary>Where the authorization URL goes and how long each phase of the browser login may take.</summary>
        public InteractiveLoginOptions Browser = new();

        /// <summary>Record the connection as one this project uses.</summary>
        public bool BindToProject = true;

        public CancellationToken Cancellation;

        /// <summary>Told what the login is doing ("Fetching tenant data...", "Logging in..."), for a status label.</summary>
        public Action<string> OnStatus;

        internal LoginOptions Clone() => new()
        {
            Interactive = Interactive,
            BindToProject = BindToProject,
            Cancellation = Cancellation,
            OnStatus = OnStatus,
            Browser = new InteractiveLoginOptions
            {
                OnAuthUrl = Browser.OnAuthUrl,
                OpenBrowser = Browser.OpenBrowser,
                UrlBudget = Browser.UrlBudget,
                UserBudget = Browser.UserBudget,
            },
        };
    }

    /// <summary>What a login, a logout or an import did. Never carries a token or a secret.</summary>
    public sealed class LoginOutcome
    {
        public bool Success;

        /// <summary>A short, stable name for what went wrong: <c>ok</c>, <c>tenant-data</c>, <c>auth-config</c>, <c>no-tenant-key</c>,
        /// <c>login-timeout</c>, <c>login-failed</c>, <c>account-mismatch</c>, <c>login-required</c>, <c>profile-api</c>, <c>cancelled</c>.</summary>
        public string Code;

        public string Message;

        /// <summary>The connection as it stands after the call, when there is one.</summary>
        public ConnectionRecord Connection;

        public static LoginOutcome Ok(ConnectionRecord connection) => new() { Success = true, Code = "ok", Connection = connection };

        public static LoginOutcome Fail(string code, string message, ConnectionRecord connection = null) =>
            new() { Success = false, Code = code, Message = message, Connection = connection };
    }

    /// <summary>
    /// Logs into a tenant without any UI: what <c>TenantSelectionWindow.OnLoginClicked</c> used to do inside
    /// the window (read the tenant over HMAC, resolve the auth config and the client id, run the Azure login,
    /// exchange the token) is here, and the window, the command line and the editor's request pump all call
    /// it. The result is a connection in the <see cref="ConnectionStore"/> (ADR 0037).
    /// </summary>
    /// <remarks>
    /// The connection's key needs the tenant key from <c>config-public</c>, so a credential is turned into a
    /// connection by asking the platform; that is why importing the project's configuration files needs the
    /// network. A credential whose <c>config-public</c> gives no tenant key (a platform-admin app) is refused.
    /// </remarks>
    public static class ConnectionLoginService
    {
        // Suffix appended to the tenant label to form the Application API token label
        // (tenant "Simbi" -> "SimbiApplication"). Same convention as
        // AuthenticationBridge.ManageTokens in the WebGL bridge — the match rule is
        // in FetchTenantTokenAsync.
        private const string application_api_label_suffix = "Application";

        private static readonly HttpClient httpClient = new();

        // One login per credential at a time: a second request for it joins the first, and is told the same URL.
        private static readonly Dictionary<string, LoginFlight> loginsInFlight = new();

        private sealed class LoginFlight
        {
            public Task<LoginOutcome> Task;
            public string AuthUrl;
            public readonly List<Action<string>> Listeners = new();
        }

        /// <summary>What reading the platform about a credential produced.</summary>
        private sealed class Resolution
        {
            /// <summary>Set when the credential could not be resolved; everything else is then empty.</summary>
            public LoginOutcome Failure;
            public Tenant Tenant;
            public TenantPublicConfig Public;
            public AzureB2CConfig Auth;
            public string ClientId;
            public string Key;
        }

        #region Login

        /// <summary>
        /// Logs in the tenant a Back Office credential belongs to, adding the connection to the store when it is
        /// not there yet. The renewal is silent while the pinned account still has a refresh token.
        /// </summary>
        /// <param name="app">The app name of the imported file, kept on the connection for display.</param>
        /// <param name="env">The environment key inside that file.</param>
        public static Task<LoginOutcome> LoginAsync(AppIdentification identification, string app, string env, LoginOptions options = null)
        {
            if (identification?.Credential == null)
                return Task.FromResult(LoginOutcome.Fail("tenant-data", "No credential to log in with."));

            options = options == null ? new LoginOptions() : options.Clone();
            string flightKey = $"{ConnectionRecord.NormalizeBaseUrl(identification.ApiBaseUrl)}|{identification.Credential.AppId}";

            lock (loginsInFlight)
            {
                if (loginsInFlight.TryGetValue(flightKey, out LoginFlight running) && !running.Task.IsCompleted)
                {
                    // The browser flow is the first caller's; this one hears the same URL, now or when it is issued.
                    Action<string> joiner = options.Browser.OnAuthUrl;
                    if (joiner != null)
                    {
                        if (running.AuthUrl != null)
                            joiner(running.AuthUrl);
                        else
                            running.Listeners.Add(joiner);
                    }

                    return running.Task;
                }

                LoginFlight flight = new();
                Action<string> first = options.Browser.OnAuthUrl;
                options.Browser.OnAuthUrl = url =>
                {
                    Action<string>[] listeners;
                    lock (loginsInFlight)
                    {
                        flight.AuthUrl = url;
                        listeners = flight.Listeners.ToArray();
                    }

                    first?.Invoke(url);
                    foreach (Action<string> listener in listeners)
                        listener(url);
                };

                flight.Task = LoginCoreAsync(identification, app, env, options);
                loginsInFlight[flightKey] = flight;
                return flight.Task;
            }
        }

        /// <summary>Logs in a connection that is already in the store, with the credential it holds.</summary>
        public static Task<LoginOutcome> LoginAsync(string connectionKey, LoginOptions options = null)
        {
            ConnectionRecord record = ConnectionStore.Get(connectionKey);
            if (record == null)
                return Task.FromResult(LoginOutcome.Fail("tenant-data", $"There is no connection '{connectionKey}'."));

            return LoginAsync(record.ToAppIdentification(), record.App, record.Env, options);
        }

        private static async Task<LoginOutcome> LoginCoreAsync(AppIdentification identification, string app, string env, LoginOptions options)
        {
            try
            {
                LoginOutcome added = await AddConnectionCoreAsync(identification, app, env, options);
                if (!added.Success)
                    return added;

                ConnectionRecord record = added.Connection;
                string key = record.Key;

                options.OnStatus?.Invoke("Logging in...");

                AzureTokenResult azure;
                try
                {
                    IPublicClientApplication client = AzureAuthService.GetClient(record.AuthClientId, record.AuthConfig);
                    string[] scopes = AzureAuthService.BuildScopes(record.AuthConfig);

                    if (options.Interactive)
                    {
                        azure = await AzureAuthService.LoginInteractive(client, scopes, record.AccountId, options.Browser, options.Cancellation);
                    }
                    else
                    {
                        azure = await AzureAuthService.TryAcquireTokenSilent(client, scopes, record.AccountId);
                        if (azure == null)
                            return LoginOutcome.Fail("login-required", "The session cannot be renewed without the browser.", record);
                    }
                }
                catch (LoginTimeoutException ex)
                {
                    Debug.LogError($"[ConnectionLoginService] Login timed out: {ex.Message}");
                    return LoginOutcome.Fail("login-timeout", ex.Message, record);
                }
                catch (OperationCanceledException)
                {
                    return LoginOutcome.Fail("cancelled", "The login was cancelled.", record);
                }
                catch (LoginAccountMismatchException ex)
                {
                    Debug.LogError($"[ConnectionLoginService] {ex.Message}");
                    return LoginOutcome.Fail("account-mismatch", ex.Message, record);
                }
                catch (MsalException ex)
                {
                    return LoginOutcome.Fail("login-failed", ex.Message, record);
                }

                if (string.IsNullOrEmpty(azure?.AccessToken))
                {
                    Debug.LogError("[ConnectionLoginService] Login failed: Azure returned no access token.");
                    return LoginOutcome.Fail("login-failed", "Azure returned no access token.", record);
                }

                ConnectionRecord stored = await ExchangeAndStoreAsync(key, azure, checkTenantManager: true);
                if (stored == null)
                    return LoginOutcome.Fail("profile-api", "The platform did not give a token for this tenant (see the Console).", record);

                if (options.BindToProject)
                    ProjectConnections.Use(key);

                EditorLoginState.NotifyLoginChanged();

                Debug.Log($"[ConnectionLoginService] Login successful for tenant: {stored.TenantLabel}, user: {stored.Username}, " +
                          $"isTenantManager: {stored.IsTenantManager}, token expires at {Describe(stored.ExpiryUtc)}.");
                return LoginOutcome.Ok(stored);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConnectionLoginService] Login error: {ex.Message}");
                return LoginOutcome.Fail("login-failed", ex.Message);
            }
        }

        /// <summary>
        /// Completes a login or a renewal: exchanges the Azure token for the tenant's Virtuademy token and stores
        /// the session on the connection. Null when the platform gave no token (the Console says why).
        /// </summary>
        internal static async Task<ConnectionRecord> ExchangeAndStoreAsync(string key, AzureTokenResult azure, bool checkTenantManager)
        {
            ConnectionRecord record = ConnectionStore.Get(key);
            if (record?.Tenant?.Config == null)
                return null;

            JwtToken apiToken = await FetchTenantTokenAsync(record.Tenant, azure.AccessToken);
            if (apiToken == null)
                return null;

            bool isTenantManager = checkTenantManager
                ? await CheckTenantManagerAsync(record.Tenant, apiToken.Bearer)
                : record.IsTenantManager;

            return await ConnectionStore.UpdateAsync(key, r =>
            {
                if (r == null)
                    return null;

                r.Token = apiToken.Bearer;
                r.TokenExpiryUtc = apiToken.Expiry;
                r.TokenRejected = false;
                r.IsTenantManager = isTenantManager;

                // A null or empty name keeps the stored one.
                if (!string.IsNullOrEmpty(azure.Username))
                    r.Username = azure.Username;
                if (!string.IsNullOrEmpty(azure.AccountId))
                    r.AccountId = azure.AccountId;

                return r;
            });
        }

        #endregion

        #region Adding a connection

        /// <summary>
        /// Adds the connection a credential belongs to, with no login, and records it as one the project uses
        /// (<see cref="LoginOptions.BindToProject"/>). Idempotent: it refreshes what the platform says.
        /// </summary>
        public static async Task<LoginOutcome> AddConnectionAsync(AppIdentification identification, string app, string env, LoginOptions options = null)
        {
            options ??= new LoginOptions();

            try
            {
                LoginOutcome outcome = await AddConnectionCoreAsync(identification, app, env, options);

                // Here and not in the core, which a login runs first: a login that then fails must not bind the connection.
                if (outcome.Success && options.BindToProject)
                    ProjectConnections.Use(outcome.Connection.Key);

                return outcome;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConnectionLoginService] Could not add the connection: {ex.Message}");
                return LoginOutcome.Fail("tenant-data", ex.Message);
            }
        }

        /// <summary>
        /// Reads a stored connection's tenant again over HMAC, with no login: its label, environment and platform version
        /// (which an operator can change at any time, an upgrade included), and the auth configuration. Changes nothing
        /// else of the record, its session included, and does not bind it to the project. The deploy targets check the
        /// version rule against what this stores.
        /// </summary>
        public static async Task<LoginOutcome> RefreshConnectionAsync(string connectionKey)
        {
            ConnectionRecord record = ConnectionStore.Get(connectionKey);
            if (record == null)
                return LoginOutcome.Fail("tenant-data", $"There is no connection '{connectionKey}'.");

            try
            {
                return await AddConnectionCoreAsync(record.ToAppIdentification(), record.App, record.Env, new LoginOptions());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ConnectionLoginService] Could not read the tenant of {record.TenantLabel} again: {ex.Message}");
                return LoginOutcome.Fail("tenant-data", ex.Message, record);
            }
        }

        private static async Task<LoginOutcome> AddConnectionCoreAsync(AppIdentification identification, string app, string env, LoginOptions options)
        {
            options.OnStatus?.Invoke("Fetching tenant data...");

            Resolution resolved = await ResolveAsync(identification);
            if (resolved.Failure != null)
                return resolved.Failure;

            ConnectionRecord record = await ConnectionStore.UpdateAsync(resolved.Key, r =>
            {
                r ??= new ConnectionRecord();

                // Another app registration is another client: what the old one held does not carry over.
                if (r.AuthClientId != null && r.AuthClientId != resolved.ClientId)
                    ClearSession(r);

                r.ConfigApiBaseUrl = ConnectionRecord.NormalizeBaseUrl(identification.ApiBaseUrl);
                r.TenantKey = resolved.Public.TenantKey.Value;
                r.TenantLabel = resolved.Tenant.Label;
                r.Environment = resolved.Public.Environment ?? resolved.Tenant.Env.ToString();
                r.PlatformVersion = resolved.Public.PlatformVersion;
                r.BackofficeUrl = resolved.Public.BackofficeUrl;
                r.App = app ?? r.App;
                r.Env = env ?? r.Env;
                r.AppId = identification.Credential.AppId.ToString();
                r.AppSecret = identification.Credential.AppSecret;
                r.ApiVersion = identification.ApiVersion;
                r.Tenant = resolved.Tenant;
                r.AuthConfig = resolved.Auth;
                r.AuthClientId = resolved.ClientId;
                return r;
            });

            if (record == null)
                return LoginOutcome.Fail("tenant-data", "The connection store is not available in this process.");

            return LoginOutcome.Ok(record);
        }

        /// <summary>
        /// Reads the platform about a credential, over HMAC: the tenant, its public configuration (the tenant key
        /// the connection is named by) and the auth configuration the login needs.
        /// </summary>
        private static async Task<Resolution> ResolveAsync(AppIdentification identification)
        {
            // 1. The tenant
            ApiResponse<Tenant> tenantResponse = await TenantConfigurationClient.GetTenantData(identification);
            if (!tenantResponse.IsSuccess || tenantResponse.Content?.Config == null)
            {
                string reason = tenantResponse.ReasonPhrase;
                Debug.LogError($"[ConnectionLoginService] Failed to get tenant data: {reason}");
                return new Resolution { Failure = LoginOutcome.Fail("tenant-data", $"Failed to get tenant data: {reason}") };
            }

            Tenant tenant = tenantResponse.Content;

            // 2. The public configuration: the tenant key that names the connection
            ApiResponse<TenantPublicConfig> publicResponse = await TenantConfigurationClient.GetTenantPublicConfig(identification);
            if (!publicResponse.IsSuccess || publicResponse.Content?.TenantKey == null)
            {
                string reason = publicResponse.IsSuccess
                    ? "the platform gave no tenant key (a platform-admin app belongs to no tenant)"
                    : publicResponse.ReasonPhrase;
                reason += "; a Configuration-API older than v2026.6.0 does not answer with a tenant key either";
                Debug.LogError($"[ConnectionLoginService] No connection can be made from this credential: {reason}");
                return new Resolution { Failure = LoginOutcome.Fail("no-tenant-key", $"No connection can be made from this credential: {reason}") };
            }

            // 3. The auth config. Falls back to the legacy custom config if the tenant's authConfig is not populated yet.
            AzureB2CConfig auth = tenant.Config.AuthConfig;
            if (auth == null)
            {
                ApiResponse<Newtonsoft.Json.Linq.JObject> customConfig = await TenantConfigurationClient.GetAppCustomConfig(identification);
                if (customConfig.IsSuccess)
                    auth = AzureB2CConfig.FromAppCustomConfig(customConfig.Content);
            }

            if (auth == null)
            {
                Debug.LogError("[ConnectionLoginService] Failed to get auth config from tenant or custom config.");
                return new Resolution { Failure = LoginOutcome.Fail("auth-config", "Failed to get the auth config from the tenant or its custom config.") };
            }

            // 4. Validate it for the kind of login it names
            string invalid = null;
            if (auth.IsEntraId)
            {
                if (string.IsNullOrEmpty(auth.Tenant))
                    invalid = "Invalid Entra ID config: Tenant (tenant ID) is empty.";
            }
            else if (auth.IsB2C)
            {
                if (string.IsNullOrEmpty(auth.Tenant) || string.IsNullOrEmpty(auth.Policy))
                    invalid = $"Invalid B2C config: Tenant '{auth.Tenant}', Policy '{auth.Policy}'.";
            }
            else
            {
                invalid = $"Unrecognized auth policy '{auth.Policy}'. Expected 'EntraID' or a value starting with 'B2C_'.";
            }

            if (invalid != null)
            {
                Debug.LogError($"[ConnectionLoginService] {invalid}");
                return new Resolution { Failure = LoginOutcome.Fail("auth-config", invalid) };
            }

            // 5. clientId = AppIdentification.Credential.AppId
            return new Resolution
            {
                Tenant = tenant,
                Public = publicResponse.Content,
                Auth = auth,
                ClientId = identification.Credential.AppId.ToString(),
                Key = ConnectionRecord.MakeKey(identification.ApiBaseUrl, publicResponse.Content.TenantKey.Value),
            };
        }

        /// <summary>
        /// Looks up the credential of the project's own configuration files (the App Assets of the
        /// <see cref="AppConfigurationSettings"/>) by the app and environment names the file carries.
        /// </summary>
        public static bool TryFindProjectCredential(string app, string env, out AppIdentification identification,
                                                    out string appName, out string envKey, out string error)
        {
            identification = null;
            appName = null;
            envKey = null;
            error = null;

            AppConfigurationSettings settings = AppConfigurationSettings.FindSelected();
            List<(string, Dictionary<string, AppIdentification>)> apps = settings == null
                ? new List<(string, Dictionary<string, AppIdentification>)>()
                : settings.GetAppIdentification(settings.AppAssets);

            if (apps.Count == 0)
            {
                error = "The project holds no tenant configuration file. Import the one the Back Office gives (tenant-config), or name a connection that is already in the store.";
                return false;
            }

            List<(string, Dictionary<string, AppIdentification>)> candidates = apps
                .Where(a => string.IsNullOrEmpty(app) || string.Equals(a.Item1, app, StringComparison.OrdinalIgnoreCase)).ToList();

            if (candidates.Count != 1)
            {
                error = candidates.Count == 0
                    ? $"The project has no configuration file for the app '{app}'. It has: {string.Join(", ", apps.Select(a => a.Item1))}."
                    : $"The project has {candidates.Count} configuration files: name one with the app. Apps: {string.Join(", ", candidates.Select(a => a.Item1))}.";
                return false;
            }

            Dictionary<string, AppIdentification> envs = candidates[0].Item2;
            KeyValuePair<string, AppIdentification> match = default;
            int found = 0;

            foreach (KeyValuePair<string, AppIdentification> entry in envs)
            {
                if (!string.IsNullOrEmpty(env) && !string.Equals(entry.Key, env, StringComparison.OrdinalIgnoreCase))
                    continue;

                match = entry;
                found++;
            }

            if (found != 1)
            {
                error = found == 0
                    ? $"The file of '{candidates[0].Item1}' has no environment '{env}'. It has: {string.Join(", ", envs.Keys)}."
                    : $"The file of '{candidates[0].Item1}' has {found} environments: name one. Environments: {string.Join(", ", envs.Keys)}.";
                return false;
            }

            identification = match.Value;
            appName = candidates[0].Item1;
            envKey = match.Key;
            return true;
        }

        /// <summary>
        /// Adds to the store every credential of the project's configuration files that it does not hold yet:
        /// the migration of the old per-project JSON credentials (ADR 0037). Asks the platform once per
        /// credential, so it needs the network; a credential that cannot be resolved is reported, not fatal.
        /// Every connection a file of the project stands for, new or already stored, is recorded as one the project
        /// uses, so the Publish Environments window offers it as a deploy target.
        /// </summary>
        public static async Task<(int Added, int AlreadyKnown, List<string> Failures)> ImportProjectCredentialsAsync(AppConfigurationSettings settings)
        {
            int added = 0;
            int known = 0;
            List<string> failures = new();

            if (settings == null || ConnectionStore.IsWorkerProcess)
                return (added, known, failures);

            foreach ((string app, Dictionary<string, AppIdentification> envs) in settings.GetAppIdentification(settings.AppAssets))
            {
                foreach (KeyValuePair<string, AppIdentification> entry in envs)
                {
                    AppIdentification identification = entry.Value;
                    ConnectionRecord stored = identification?.Credential == null ? null : ConnectionStore.FindByAppId(identification.Credential.AppId);
                    if (identification?.Credential == null || stored != null)
                    {
                        // A file of the project is a connection the project uses, even when another project added it to the store.
                        if (stored != null)
                            ProjectConnections.Use(stored.Key);

                        known++;
                        continue;
                    }

                    LoginOutcome outcome = await AddConnectionAsync(identification, app, entry.Key);
                    if (outcome.Success)
                        added++;
                    else
                        failures.Add($"{app}/{entry.Key}: {outcome.Message}");
                }
            }

            return (added, known, failures);
        }

        #endregion

        #region Logout

        /// <summary>
        /// Ends the session of one connection: its platform token goes. The MSAL account goes only when no other
        /// connection uses the same authority and account, so logging out of one tenant never logs the user out of
        /// another that shares the Azure directory. (The ADR names the client id as a third part of that test; it
        /// is left out here, because removing an account can take the other app registrations' tokens of the same
        /// account with it.)
        /// </summary>
        /// <returns>False when the connection is not in the store.</returns>
        public static async Task<bool> LogoutAsync(string connectionKey)
        {
            ConnectionRecord record = ConnectionStore.Get(connectionKey);
            if (record == null)
                return false;

            try
            {
                if (!string.IsNullOrEmpty(record.AccountId) && record.AuthConfig != null && !string.IsNullOrEmpty(record.AuthClientId))
                {
                    string authority = AzureAuthService.AuthorityOf(record.AuthConfig);
                    bool shared = ConnectionStore.All().Any(other =>
                        !string.Equals(other.Key, record.Key, StringComparison.OrdinalIgnoreCase)
                        && other.AccountId == record.AccountId
                        && other.AuthConfig != null
                        && AzureAuthService.AuthorityOf(other.AuthConfig) == authority);

                    if (!shared)
                        await AzureAuthService.RemoveAccountAsync(AzureAuthService.GetClient(record.AuthClientId, record.AuthConfig), record.AccountId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ConnectionLoginService] Could not remove the cached account: {ex.Message}");
            }

            await ConnectionStore.UpdateAsync(record.Key, r =>
            {
                if (r != null)
                    ClearSession(r);
                return r;
            });

            EditorLoginState.NotifyLoginChanged();
            Debug.Log($"[ConnectionLoginService] Logged out of {record.TenantLabel} ({record.Key}).");
            return true;
        }

        private static void ClearSession(ConnectionRecord record)
        {
            record.Token = null;
            record.TokenExpiryUtc = null;
            record.TokenRejected = false;
            record.Username = null;
            record.IsTenantManager = false;
            record.AccountId = null;
        }

        #endregion

        #region Platform token

        /// <summary>
        /// Exchanges an Azure access token for the Virtuademy token of this tenant's API label.
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
                Debug.LogError($"[ConnectionLoginService] Failed to call the profile API: {ex.Message}");
                return null;
            }

            if (string.IsNullOrEmpty(tokensJson))
            {
                Debug.LogError("[ConnectionLoginService] The profile API returned no tokens.");
                return null;
            }

            JwtToken[] tokens;
            try
            {
                tokens = JsonConvert.DeserializeObject<JwtToken[]>(tokensJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConnectionLoginService] Could not parse the profile API tokens: {ex.Message}");
                return null;
            }

            tokens ??= Array.Empty<JwtToken>();

            // Tenants provisioned under the current convention label the Application API
            // "<TenantLabel>Application", mirroring the AI/Realtime scheme ("SimbiAI",
            // "SimbiRealtime"). Match the suffixed label first, then fall back to the bare tenant
            // label for tenants provisioned before it (e.g. Marangoni).
            // TODO: drop the legacy bare-label fallback once every tenant carries the suffix.
            string apiLabel = tenant.Label + application_api_label_suffix;
            JwtToken matchingToken = tokens.FirstOrDefault(t => t.ApiLabel == apiLabel)
                                     ?? tokens.FirstOrDefault(t => t.ApiLabel == tenant.Label);
            if (matchingToken == null)
            {
                // Listing what came back separates "no token was minted at all" — an empty list
                // means the profile is not Enabled, or the app's role definition does not request
                // the API — from "minted under a label we do not match".
                string received = tokens.Length == 0
                                  ? "<none>"
                                  : string.Join(", ", tokens.Select(t => t.ApiLabel));
                Debug.LogError($"[ConnectionLoginService] No token found for API label: {apiLabel} " +
                               $"(or {tenant.Label}). Received: {received}");
            }

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
                Debug.LogWarning($"[ConnectionLoginService] Could not check the TenantManager role: {ex.Message}");
                return false;
            }
        }

        private static string Describe(DateTime? expiry) =>
            EditorSessionManager.FormatLocal(expiry, seconds: true);

        #endregion
    }
}
