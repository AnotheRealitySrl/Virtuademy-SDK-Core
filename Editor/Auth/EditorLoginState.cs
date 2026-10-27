using Virtuademy.SDK.TenantConfiguration;

using System;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// What the editor knows about the login of the project's default connection
    /// (<see cref="ProjectConnections.DefaultKey"/>), read from the per-user <see cref="ConnectionStore"/>.
    /// Nothing is kept in SessionState or EditorPrefs any more, and the token is never held here: every
    /// read goes to the store, so a renewal or a logout done by another process shows at once.
    /// </summary>
    /// <remarks>
    /// This is the view the editor tools of the single-login SDK were written against, kept so they
    /// keep working with the project's default connection. It does not say where a deploy goes: a deploy names its
    /// connections. New code names its connection and reads <see cref="ConnectionStore.Get"/>. Who writes the session
    /// is <see cref="ConnectionLoginService"/> and <see cref="EditorSessionManager"/>, nothing else.
    /// </remarks>
    public static class EditorLoginState
    {
        /// <summary>
        /// How long before the real expiry a token is already treated as stale. A single deploy
        /// step (zip upload, DLL verification) can take a while between the check and the request
        /// reaching the server, so the token must still be good on arrival, not just on departure.
        /// </summary>
        public static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(2);

        /// <summary>The key of the connection this view reads, the default one, or null when the project has none.</summary>
        public static string DefaultConnectionKey => ProjectConnections.DefaultKey;

        private static ConnectionRecord Active => ConnectionStore.Get(ProjectConnections.DefaultKey);

        public static string BearerToken => Active?.Token ?? "";

        public static Tenant CurrentTenant => Active?.Tenant;

        public static string Username => Active?.Username ?? "";

        public static bool IsTenantManager => Active?.IsTenantManager ?? false;

        /// <summary>The app name of the configuration file the connection was made from.</summary>
        public static string LoggedInApp => Active?.App ?? "";

        /// <summary>The environment key of that file.</summary>
        public static string LoggedInEnv => Active?.Env ?? "";

        /// <summary>
        /// Azure client id (the app registration) the session was obtained with. Needed to
        /// rebuild the MSAL client when renewing the token.
        /// </summary>
        public static string AuthClientId => Active?.AuthClientId ?? "";

        /// <summary>
        /// The auth config as resolved at login time. Stored rather than re-read from the tenant
        /// because login falls back to the legacy app custom config when the tenant has no
        /// authConfig yet: a renewal must use whatever actually worked.
        /// </summary>
        public static AzureB2CConfig AuthConfig => Active?.AuthConfig;

        /// <summary>
        /// When the current token stops being accepted, or null if it could not be determined.
        /// </summary>
        public static DateTime? TokenExpiryUtc => Active?.ExpiryUtc;

        /// <summary>
        /// The moment <see cref="IsTokenValid"/> turns false on its own: the expiry minus
        /// <see cref="ExpiryMargin"/>. Status labels show this one rather than the expiry, so the
        /// time they announce is the time they change. Null when the expiry is unknown.
        /// </summary>
        public static DateTime? TokenUsableUntilUtc => Active?.UsableUntilUtc;

        /// <summary>
        /// True when the server answered 401 to the current token before its expiry.
        /// </summary>
        public static bool IsTokenRejected => Active?.TokenRejected ?? false;

        /// <summary>
        /// True when a login happened at some point, regardless of whether the token is still
        /// good. A session that only needs renewing is still a session.
        /// </summary>
        public static bool HasSession => Active?.HasSession ?? false;

        /// <summary>
        /// True when the stored token can still be used: present, not rejected by the server,
        /// and not within <see cref="ExpiryMargin"/> of its expiry.
        /// </summary>
        public static bool IsTokenValid => Active?.IsTokenValid ?? false;

        /// <summary>
        /// True when the token can be renewed without a fresh interactive login being the only
        /// option: everything the renewal needs was stored at login time.
        /// </summary>
        public static bool CanRenewSession => Active?.CanRenew ?? false;

        /// <summary>
        /// A login is on record. This stays true while the token is merely expired, because every
        /// authenticated call renews it transparently — check <see cref="IsTokenValid"/> to know
        /// whether the next call will need a round trip to Azure first.
        /// </summary>
        public static bool IsLoggedIn => HasSession;

        /// <summary>
        /// Checks whether the given app/env pair is the one the default connection was made from and is logged in.
        /// </summary>
        public static bool IsLoggedInto(string app, string env)
        {
            ConnectionRecord active = Active;
            return active != null
                && active.HasSession
                && !string.IsNullOrEmpty(active.App)
                && active.App == app
                && active.Env == env;
        }

        /// <summary>The user logged in or out of a connection, or the project's default connection changed.</summary>
        public static event Action OnLoginStateChanged;

        /// <summary>
        /// The token behind the session changed — renewed, or rejected by the server — while who
        /// is logged in did not. Raised from inside the API call that renewed it, so subscribers
        /// must only re-render (status labels), never reload data or start requests: that is what
        /// <see cref="OnLoginStateChanged"/> is for, and why it is not raised here.
        /// </summary>
        public static event Action OnTokenChanged;

        internal static void NotifyLoginChanged() => OnLoginStateChanged?.Invoke();

        internal static void NotifyTokenChanged() => OnTokenChanged?.Invoke();
    }
}
