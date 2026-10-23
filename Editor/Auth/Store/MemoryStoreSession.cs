using UnityEditor;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Where the store cannot be encrypted (see <see cref="StoreCrypto"/>) the connections and the token cache live in the
    /// editor process. A domain reload (a script compilation, entering play mode) drops every static, so just before it the
    /// two are written to SessionState, which is process memory: it survives the reload, never the editor, and is
    /// never EditorPrefs or a file. <see cref="ConnectionStore"/> and <see cref="EncryptedMsalCache"/> read them back on
    /// first use. Does nothing where the store is encrypted, and nothing is touched when the assembly loads.
    /// </summary>
    [InitializeOnLoad]
    internal static class MemoryStoreSession
    {
        static MemoryStoreSession()
        {
            if (ConnectionStore.IsWorkerProcess)
                return;

            AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                ConnectionStore.SaveMemoryToSession();
                EncryptedMsalCache.SaveMemoryToSession();
            };
        }
    }
}
