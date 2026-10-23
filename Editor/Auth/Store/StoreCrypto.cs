using System;
using System.Text;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// What encrypts the files of the connection store: DPAPI on Windows. There is no other
    /// implementation yet (the macOS Keychain one is pending), and where there is none the store does
    /// not write anything to disk: <see cref="IsAvailable"/> is false and <see cref="ConnectionStore"/> keeps
    /// the connections in the editor process (SessionState, which survives a domain reload and not the editor).
    /// Nothing is ever written in clear text.
    /// </summary>
    internal static class StoreCrypto
    {
        /// <summary>For tests: behave as an OS with no encryption (memory-only store) on any OS.</summary>
        internal static bool ForceMemoryOnly;

#if UNITY_EDITOR_WIN
        public static bool IsAvailable => !ForceMemoryOnly;
#else
        public static bool IsAvailable => false;
#endif

        /// <summary>
        /// <paramref name="purpose"/> goes into DPAPI's entropy, so a file written for one use does not
        /// decrypt as another.
        /// </summary>
        public static byte[] Protect(byte[] plain, string purpose)
        {
#if UNITY_EDITOR_WIN
            return Dpapi.Protect(plain, Entropy(purpose));
#else
            throw new PlatformNotSupportedException("The connection store has no encryption on this platform yet.");
#endif
        }

        public static byte[] Unprotect(byte[] cipher, string purpose)
        {
#if UNITY_EDITOR_WIN
            return Dpapi.Unprotect(cipher, Entropy(purpose));
#else
            throw new PlatformNotSupportedException("The connection store has no encryption on this platform yet.");
#endif
        }

        private static byte[] Entropy(string purpose) => Encoding.UTF8.GetBytes("Virtuademy.SDK." + purpose + ".v1");
    }
}
