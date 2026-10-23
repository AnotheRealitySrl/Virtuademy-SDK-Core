using Microsoft.Identity.Client;

using System;
using System.IO;
using System.Threading;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The one MSAL token cache of the store: every account of every connection in one file, encrypted like
    /// the records, serialized through MSAL's own hooks (<c>SetBeforeAccess</c>, <c>SetAfterAccess</c>).
    /// <c>Microsoft.Identity.Client.Extensions.Msal</c> is not used: it needs a newer MSAL than the one this
    /// package ships and brings assemblies that clash with Unity's Mono.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before MSAL touches the cache the lock file is taken (<see cref="FileLock"/>, not thread-bound:
    /// MSAL may call the two hooks on different threads) and the file is read; after it, the cache is written
    /// if it changed and the lock is released. A static semaphore orders the clients of this process, one per
    /// client id, which share the file.
    /// </para>
    /// <para>
    /// Where nothing can be encrypted the cache is a byte array in memory, for the process only.
    /// </para>
    /// </remarks>
    internal sealed class EncryptedMsalCache
    {
        private const string purpose = "msal";
        private const int lock_timeout_ms = 10000;

        private static readonly SemaphoreSlim processGate = new(1, 1);

        // Where nothing can be encrypted: the cache lives in the editor process, and SessionState carries it over a
        // domain reload (see MemoryStoreSession).
        private const string memory_session_key = "Virtuademy_SDK_MemoryMsalCache";
        private static byte[] memoryBlob;
        private static bool memoryRestored;

        private readonly string path;
        private readonly string lockPath;
        private FileLock fileLock;

        public EncryptedMsalCache(string path)
        {
            this.path = path;
            lockPath = path + ".lock";
        }

        /// <summary>The store's cache, or the memory one when the store is not persistent.</summary>
        public static EncryptedMsalCache ForStore() => new(ConnectionStore.MsalCachePath);

        public void Attach(IPublicClientApplication app)
        {
            app.UserTokenCache.SetBeforeAccess(OnBeforeAccess);
            app.UserTokenCache.SetAfterAccess(OnAfterAccess);
        }

        private void OnBeforeAccess(TokenCacheNotificationArgs args)
        {
            if (!processGate.Wait(lock_timeout_ms))
                throw new TimeoutException("The token cache is busy in this process.");

            try
            {
                if (StoreCrypto.IsAvailable)
                {
                    fileLock = FileLock.TryAcquire(lockPath, lock_timeout_ms)
                        ?? throw new TimeoutException("The token cache is locked by another process.");
                }
            }
            catch
            {
                processGate.Release();
                throw;
            }

            try
            {
                byte[] plain = Read();
                if (plain != null && plain.Length > 0)
                    args.TokenCache.DeserializeMsalV3(plain, shouldClearExistingCache: true);
            }
            catch (Exception ex)
            {
                // A cache nobody can read (another OS user, a damaged copy) costs one login, not a broken one.
                Debug.LogWarning($"[EncryptedMsalCache] The token cache is unreadable and is treated as empty ({ex.GetType().Name}).");
            }
        }

        private void OnAfterAccess(TokenCacheNotificationArgs args)
        {
            try
            {
                if (args.HasStateChanged)
                    Write(args.TokenCache.SerializeMsalV3());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EncryptedMsalCache] The token cache could not be written: {ex.Message}");
            }
            finally
            {
                fileLock?.Dispose();
                fileLock = null;
                processGate.Release();
            }
        }

        /// <summary>Memory-only mode, before a domain reload: keeps the cache in process memory (SessionState).</summary>
        internal static void SaveMemoryToSession()
        {
            byte[] blob = memoryBlob;
            if (StoreCrypto.IsAvailable || blob == null)
                return;

            UnityEditor.SessionState.SetString(memory_session_key, Convert.ToBase64String(blob));
        }

        private static void RestoreMemory()
        {
            memoryRestored = true;

            // SessionState is the main thread's: a cache first read from another thread starts empty (one login).
            try
            {
                string text = UnityEditor.SessionState.GetString(memory_session_key, "");
                if (!string.IsNullOrEmpty(text))
                    memoryBlob = Convert.FromBase64String(text);
            }
            catch (Exception ex) when (ex is FormatException || ex is UnityException)
            {
                Debug.LogWarning($"[EncryptedMsalCache] The token cache kept in memory across the reload could not be read ({ex.GetType().Name}).");
            }
        }

        private byte[] Read()
        {
            if (!StoreCrypto.IsAvailable)
            {
                if (!memoryRestored)
                    RestoreMemory();

                return memoryBlob;
            }

            if (!File.Exists(path))
                return null;

            byte[] blob = File.ReadAllBytes(path);
            return blob.Length == 0 ? null : StoreCrypto.Unprotect(blob, purpose);
        }

        private void Write(byte[] plain)
        {
            if (!StoreCrypto.IsAvailable)
            {
                memoryRestored = true;
                memoryBlob = plain;
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ConnectionStore.WriteAtomic(path, StoreCrypto.Protect(plain, purpose));
        }
    }
}
