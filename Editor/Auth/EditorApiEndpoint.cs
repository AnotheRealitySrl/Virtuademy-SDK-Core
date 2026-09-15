using Virtuademy.SDK.Core.ApiSystem;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The APIs the editor tooling talks to: addressables deploy, interpreted-script verification
    /// and environment DLL import all resolve them here.
    ///
    /// One named place on purpose. Reading the tenant inline is a two-line expression, and while
    /// this feature was being built that expression got replaced with a hardcoded localhost in
    /// four separate files during debugging — with the ones nobody remembered still pointing at
    /// the tenant, so half the flow talked to one API and half to the other. A single accessor is
    /// what makes that mismatch impossible to introduce by accident.
    /// </summary>
    /// <remarks>
    /// Since 2026-09-08 it resolves through the **same generated asset the build reads**
    /// (<see cref="PlatformConfig"/>, ADR 0025), falling back to the logged-in tenant's config.
    /// That order is the point rather than a detail: an editor that resolved endpoints differently
    /// from the player could publish an environment to one platform and have the world load
    /// against another, and the two would have to disagree for a while before anybody noticed.
    /// <para>
    /// The fallback stays because the asset needs a tenant switch to exist and the login state does
    /// not — a freshly logged-in project with no generated asset still publishes, which is what
    /// keeps this change from being a new precondition on the whole editor flow.
    /// </para>
    /// </remarks>
    public static class EditorApiEndpoint
    {
        /// <summary>Canonical <c>cpi_type</c> values, as the platform reports them.</summary>
        private const string application_api_type = "Application";

        /// <summary>
        /// Base URL of the Application API, or null when neither the generated asset nor a login
        /// can supply one — callers already treat that as "cannot reach the platform".
        /// </summary>
        public static string ApplicationApiUrl => Resolve(application_api_type,
                                                          EditorLoginState.CurrentTenant?.Config?.ApplicationApiUrl);

        /// <summary>
        /// The generated asset first, then the tenant the editor is logged into.
        /// </summary>
        private static string Resolve(string apiType, string tenantFallback)
        {
            if (PlatformConfig.TryGetEndpoint(apiType, out PlatformEndpoint endpoint)
                && !string.IsNullOrEmpty(endpoint.BaseUrl))
            {
                return endpoint.BaseUrl;
            }

            return tenantFallback;
        }
    }
}
