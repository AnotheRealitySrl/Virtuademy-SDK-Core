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
    /// Every request takes its base URLs from the <b>connection it names</b> (ADR 0037): the addresses its
    /// tenant reported when the connection logged in. The project's generated <c>PlatformEndpoints</c> asset
    /// no longer decides where an editor request goes: it is what the <i>build</i> falls back to, and one
    /// project's asset cannot be the answer for a connection that is not the project's play-mode tenant. A
    /// token is sent to the host of the connection it belongs to, and to no other.
    /// <para>
    /// The members without a connection key mean the default connection
    /// (<see cref="ProjectConnections.DefaultKey"/>), for the callers written before every request named its
    /// connection.
    /// </para>
    /// </remarks>
    public static class EditorApiEndpoint
    {
        /// <summary>
        /// Base URL of the default connection's Application API, or null when the computer has no usable connection or
        /// its tenant reported none — callers already treat that as "cannot reach the platform".
        /// </summary>
        public static string ApplicationApiUrl => ApplicationApiUrlOf(ProjectConnections.DefaultKey);

        /// <summary>Base URL of the Application API of this connection's tenant.</summary>
        public static string ApplicationApiUrlOf(string connectionKey) =>
            ConnectionStore.Get(connectionKey)?.Tenant?.Config?.ApplicationApiUrl;
    }
}
