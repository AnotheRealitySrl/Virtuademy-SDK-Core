using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The per-user store of the editor's connections (ADR 0037): one file under
    /// <c>%USERPROFILE%\.virtuademy\sdk\</c>, outside every project, encrypted with DPAPI, shared by
    /// every Unity project and process of the OS user.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the profile folder.</b> A packaged (MSIX) app can redirect <c>%LOCALAPPDATA%</c>, <c>%APPDATA%</c> and
    /// <c>HKCU</c> for the processes it starts: a command line started from such an app (the Claude desktop app, for
    /// one) would then keep a store of its own beside the editor's, and neither would see the other's logins. The
    /// profile folder is not redirected, and its access rights are the same.
    /// </para>
    /// <para>
    /// <b>Writers.</b> A change takes the lock file (<see cref="FileShare.None"/>, released by the operating
    /// system if its process dies), reads the file again, changes only its own connection, raises that
    /// record's revision and replaces the file atomically. Readers do not lock: they see the old file or the
    /// new one. The status file beside it (<c>status.json</c>, no secret) is written in the same step, so
    /// the <c>connections</c> and <c>whoami</c> commands can be answered without starting Unity.
    /// </para>
    /// <para>
    /// <b>Where it cannot be encrypted</b> (<see cref="StoreCrypto.IsAvailable"/>: everything but Windows
    /// for now) the connections stay in the editor process and nothing is written to disk: they are carried over a
    /// domain reload through SessionState (process memory; never EditorPrefs), so a script recompilation does not log
    /// the creator out, and they are gone when the editor closes. A batch run therefore cannot leave a login behind.
    /// </para>
    /// <para>
    /// <b>Asset-import worker processes</b> run editor code too. They never read or write the store, renew a
    /// token or run the migration: the store looks empty to them and refuses every change.
    /// </para>
    /// <para>
    /// <b>Locks do not block the main thread.</b> <see cref="UpdateAsync"/> and <see cref="LockRenewalAsync"/> wait for
    /// another process with <c>Task.Delay</c>; the synchronous <see cref="Update"/> is for code that is not on the
    /// editor's main thread, and for tests.
    /// </para>
    /// </remarks>
    public static class ConnectionStore
    {
        private const string records_purpose = "connections";
        private const int lock_timeout_ms = 10000;
        private const int store_version = 1;

        private static readonly object gate = new();
        private static readonly Dictionary<string, ConnectionRecord> memory = new();
        private static readonly bool isWorker;
        private static bool warnedMemoryOnly;
        private static bool prepared;
        private static long lastUnreadableStamp = -2;

        private const string memory_session_key = "Virtuademy_SDK_MemoryConnections";

        // The last file read, keyed by where it was and by what it looked like.
        private static List<ConnectionRecord> cache;
        private static string cachePath;
        private static long cacheStamp;

        // True when the last look at the store found its file and read it. False when there is no file (nothing written yet, or
        // it vanished for a moment), and while the last attempt to read it failed: what All() returns is then no statement
        // about which connections exist.
        private static bool readFromFile;

        /// <summary>For tests: moves the whole store (records, MSAL cache, locks, status) to another folder.</summary>
        internal static string RootOverride;

        static ConnectionStore()
        {
            // Read once, here: the first use of the store is on the main thread, and the API is not
            // callable from the threads MSAL's cache hooks run on.
            isWorker = AssetDatabase.IsAssetImportWorkerProcess();
        }

        private sealed class StoreFile
        {
            public int Version { get; set; } = store_version;
            public List<ConnectionRecord> Connections { get; set; } = new();
        }

        #region Where

        public static string Root => RootOverride
                                     ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".virtuademy", "sdk");

        private static string RecordsPath => Path.Combine(Root, "connections.bin");
        private static string LockPath => Path.Combine(Root, "connections.lock");

        /// <summary>The records' file without secrets: connections, account names, expiry. Readable without Unity.</summary>
        public static string StatusPath => Path.Combine(Root, "status.json");

        /// <summary>The one MSAL token cache, every account of every connection, encrypted like the records.</summary>
        public static string MsalCachePath => Path.Combine(Root, "msal.bin");

        /// <summary>False where nothing can be encrypted yet: the connections then live in memory only.</summary>
        public static bool IsPersistent => StoreCrypto.IsAvailable;

        /// <summary>True in an asset-import worker process, which never touches the store.</summary>
        public static bool IsWorkerProcess => isWorker;

        /// <summary>
        /// True when the connections are the content of the store's file, read now: only then does a connection missing from
        /// <see cref="All"/> mean it is not in the store. False in a worker process, which reads nothing; in memory-only mode,
        /// where the store is empty after every restart; when there is no file (it was never written, or it is out of reach for
        /// a moment, such as inside <see cref="WriteAtomic"/>'s swap); and while the file cannot be read (swapped by another
        /// process, damaged).
        /// </summary>
        internal static bool WasRead
        {
            get
            {
                if (isWorker || !IsPersistent)
                    return false;

                All();
                lock (gate)
                    return readFromFile;
            }
        }

        #endregion

        /// <summary>
        /// The first use of the store in a process runs the one-time migration of the old per-machine login
        /// (<see cref="LegacyLoginMigration"/>). Nothing touches the store when an assembly loads, so merely
        /// opening a project does not.
        /// </summary>
        private static void Prepare()
        {
            if (prepared)
                return;

            prepared = true;

            if (!IsPersistent)
                RestoreMemory();

            LegacyLoginMigration.Run();
            LegacyLoginMigration.DeleteProjectMsalCaches();
        }

        /// <summary>Memory-only mode: puts the connections back after a domain reload (<see cref="SaveMemoryToSession"/>).</summary>
        private static void RestoreMemory()
        {
            try
            {
                string json = SessionState.GetString(memory_session_key, "");
                if (string.IsNullOrEmpty(json))
                    return;

                lock (gate)
                    foreach (ConnectionRecord record in JsonConvert.DeserializeObject<StoreFile>(json)?.Connections ?? new List<ConnectionRecord>())
                        memory[record.Key] = record;
            }
            catch (Exception ex) when (ex is JsonException || ex is UnityException)
            {
                Debug.LogWarning($"[ConnectionStore] The connections kept in memory across the reload could not be read: {ex.Message}");
            }
        }

        /// <summary>Memory-only mode: keeps the connections through a domain reload, in process memory (SessionState).</summary>
        internal static void SaveMemoryToSession()
        {
            if (isWorker || IsPersistent || !prepared)
                return;

            lock (gate)
                SessionState.SetString(memory_session_key, JsonConvert.SerializeObject(new StoreFile { Connections = memory.Values.ToList() }));
        }

        #region Reading

        /// <summary>Every connection. Shared snapshots: do not change them.</summary>
        public static IReadOnlyList<ConnectionRecord> All()
        {
            if (isWorker)
                return Array.Empty<ConnectionRecord>();

            Prepare();

            if (!IsPersistent)
            {
                lock (gate)
                    return memory.Values.ToList();
            }

            lock (gate)
                return LoadCached();
        }

        public static ConnectionRecord Get(string key) =>
            string.IsNullOrEmpty(key) ? null : All().FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>The connection whose imported credential has this app id, or null.</summary>
        public static ConnectionRecord FindByAppId(Guid appId) =>
            appId == Guid.Empty ? null : All().FirstOrDefault(c => Guid.TryParse(c.AppId, out Guid id) && id == appId);

        /// <summary>
        /// A connection named on a command line: its key, or the tenant key, the tenant label or
        /// <c>app/env</c> when exactly one connection answers to it.
        /// </summary>
        public static ConnectionRecord Find(string selector, out string error)
        {
            error = null;
            IReadOnlyList<ConnectionRecord> all = All();

            if (string.IsNullOrWhiteSpace(selector))
            {
                error = "No connection was named.";
                return null;
            }

            selector = selector.Trim();
            ConnectionRecord exact = all.FirstOrDefault(c => string.Equals(c.Key, selector, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            List<ConnectionRecord> matches = all.Where(c =>
                string.Equals(c.TenantLabel, selector, StringComparison.OrdinalIgnoreCase)
                || c.TenantKey.ToString() == selector
                || string.Equals($"{c.App}/{c.Env}", selector, StringComparison.OrdinalIgnoreCase)
                || (c.Key ?? "").EndsWith("/" + selector, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 1)
                return matches[0];

            error = matches.Count == 0
                ? $"No connection answers to '{selector}'. Known: {(all.Count == 0 ? "none" : string.Join(", ", all.Select(c => c.Key)))}."
                : $"'{selector}' names {matches.Count} connections; use one of their keys: {string.Join(", ", matches.Select(c => c.Key))}.";
            return null;
        }

        #endregion

        #region Writing

        /// <summary>
        /// Changes one connection. <paramref name="change"/> gets a copy of the current record (null when there
        /// is none) and returns the record to keep, or null to remove it. Returns what was kept. Waits for another
        /// process's lock with the calling thread blocked: use <see cref="UpdateAsync"/> on the editor's main thread.
        /// </summary>
        /// <exception cref="TimeoutException">Another process held the lock for longer than ten seconds.</exception>
        public static ConnectionRecord Update(string key, Func<ConnectionRecord, ConnectionRecord> change)
        {
            if (!BeginUpdate(key, change, out ConnectionRecord memoryResult))
                return memoryResult;

            using FileLock fileLock = FileLock.TryAcquire(LockPath, lock_timeout_ms)
                ?? throw new TimeoutException("The connection store is locked by another process.");

            return UpdateLocked(key, change);
        }

        /// <summary>
        /// <see cref="Update"/> that waits for another process's lock without blocking the calling thread.
        /// </summary>
        public static async Task<ConnectionRecord> UpdateAsync(string key, Func<ConnectionRecord, ConnectionRecord> change)
        {
            if (!BeginUpdate(key, change, out ConnectionRecord memoryResult))
                return memoryResult;

            using FileLock fileLock = await FileLock.TryAcquireAsync(LockPath, lock_timeout_ms)
                ?? throw new TimeoutException("The connection store is locked by another process.");

            return UpdateLocked(key, change);
        }

        /// <summary>Checks the arguments and does the whole change where there is no file (worker, memory mode). True when a file has to be changed.</summary>
        private static bool BeginUpdate(string key, Func<ConnectionRecord, ConnectionRecord> change, out ConnectionRecord result)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("A connection key is required.", nameof(key));
            if (change == null)
                throw new ArgumentNullException(nameof(change));

            result = null;

            if (isWorker)
                return false;

            Prepare();

            if (IsPersistent)
                return true;

            WarnMemoryOnly();

            lock (gate)
            {
                memory.TryGetValue(key, out ConnectionRecord current);
                result = Apply(key, current, change);

                if (result == null)
                    memory.Remove(key);
                else
                    memory[key] = result;
            }

            return false;
        }

        private static ConnectionRecord UpdateLocked(string key, Func<ConnectionRecord, ConnectionRecord> change)
        {
            lock (gate)
            {
                List<ConnectionRecord> records = ReadFresh(underLock: true);
                ConnectionRecord current = records.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
                ConnectionRecord kept = Apply(key, current, change);

                records.RemoveAll(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
                if (kept != null)
                    records.Add(kept);

                Write(records);
                return kept;
            }
        }

        private static ConnectionRecord Apply(string key, ConnectionRecord current, Func<ConnectionRecord, ConnectionRecord> change)
        {
            ConnectionRecord kept = change(current == null ? null : Clone(current));
            if (kept == null)
                return null;

            kept.Key = key;
            kept.Revision = (current?.Revision ?? 0) + 1;
            kept.UpdatedUtc = DateTime.UtcNow;
            return kept;
        }

        /// <summary>
        /// One process renews a connection and the others read the result: the lock a renewal holds around its
        /// silent token call. Null when another process still holds it after <paramref name="timeoutMs"/>. Waits without
        /// blocking the calling thread. In memory-only mode there is no other process and it is a no-op.
        /// </summary>
        public static async Task<IDisposable> LockRenewalAsync(string key, int timeoutMs)
        {
            if (isWorker || !IsPersistent)
                return new NoLock();

            using SHA1 sha = SHA1.Create();
            string name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToLowerInvariant()))).Replace("-", "").Substring(0, 12);
            return await FileLock.TryAcquireAsync(Path.Combine(Root, $"renew-{name}.lock"), timeoutMs);
        }

        private sealed class NoLock : IDisposable
        {
            public void Dispose() { }
        }

        #endregion

        #region File

        private static List<ConnectionRecord> LoadCached()
        {
            string path = RecordsPath;
            if (!File.Exists(path))
            {
                cache = new List<ConnectionRecord>();
                cachePath = path;
                cacheStamp = 0;
                readFromFile = false;
                return cache;
            }

            long stamp = Stamp(path);
            if (cache != null && cachePath == path && stamp == cacheStamp)
                return cache;

            try
            {
                List<ConnectionRecord> read = ReadFresh(underLock: false);
                cache = read;
                cachePath = path;
                cacheStamp = Stamp(path);
                readFromFile = true;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is System.ComponentModel.Win32Exception || ex is CryptographicException)
            {
                // Another process has the file in the middle of a swap, or it is damaged. A reader without the lock
                // never touches the file: it keeps what it last read, and the next change (under the lock) sets a
                // damaged file aside.
                readFromFile = false;

                if (!(ex is IOException) && stamp != lastUnreadableStamp)
                {
                    lastUnreadableStamp = stamp;
                    Debug.LogWarning($"[ConnectionStore] The connection store cannot be read ({ex.GetType().Name}); the last copy read is used.");
                }

                return cache ?? new List<ConnectionRecord>();
            }

            return cache;
        }

        private static long Stamp(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path).Ticks ^ new FileInfo(path).Length;
            }
            catch (IOException)
            {
                return -1;
            }
        }

        /// <summary>
        /// The records as the file holds them now. Under the lock, a file nobody can decrypt or parse (another OS user,
        /// another machine, a damaged copy) is set aside and the store starts empty: it costs a login, it must not
        /// break one. Without the lock it is never set aside, and an <see cref="IOException"/> (the file is being
        /// swapped, or held by another process) is never a reason to set anything aside: it propagates.
        /// </summary>
        private static List<ConnectionRecord> ReadFresh(bool underLock)
        {
            string path = RecordsPath;
            if (!File.Exists(path))
                return new List<ConnectionRecord>();

            try
            {
                byte[] blob = ReadBytes(path);
                if (blob.Length == 0)
                    return new List<ConnectionRecord>();

                string json = Encoding.UTF8.GetString(StoreCrypto.Unprotect(blob, records_purpose));
                return JsonConvert.DeserializeObject<StoreFile>(json)?.Connections ?? new List<ConnectionRecord>();
            }
            catch (Exception ex) when (underLock && (ex is JsonException || ex is System.ComponentModel.Win32Exception || ex is CryptographicException))
            {
                Debug.LogWarning($"[ConnectionStore] The connection store cannot be read ({ex.GetType().Name}); it is set aside and the connections must be logged in again.");

                try
                {
                    string aside = path + ".unreadable";
                    if (File.Exists(aside))
                        File.Delete(aside);
                    File.Move(path, aside);
                }
                catch (IOException)
                {
                }

                return new List<ConnectionRecord>();
            }
        }

        /// <summary>
        /// Opened to share with everyone, deletion included: a reader that does not share delete makes another
        /// process's <see cref="File.Replace(string, string, string)"/> fail. Retried a few times, for the instant
        /// the file is swapped.
        /// </summary>
        private static byte[] ReadBytes(string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    byte[] bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = stream.Read(bytes, read, bytes.Length - read);
                        if (n <= 0)
                            break;
                        read += n;
                    }

                    return bytes;
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(20);
                }
            }
        }

        private static void Write(List<ConnectionRecord> records)
        {
            Directory.CreateDirectory(Root);

            string json = JsonConvert.SerializeObject(new StoreFile { Connections = records });
            WriteAtomic(RecordsPath, StoreCrypto.Protect(Encoding.UTF8.GetBytes(json), records_purpose));

            cache = records;
            cachePath = RecordsPath;
            cacheStamp = Stamp(RecordsPath);
            readFromFile = true;

            WriteStatus(records);
        }

        private static void WriteStatus(List<ConnectionRecord> records)
        {
            try
            {
                var status = new Dictionary<string, object>
                {
                    { "updatedUtc", DateTime.UtcNow.ToString("o") },
                    { "connections", records.Select(c => c.ToSummary()).ToList() },
                };

                WriteAtomic(StatusPath, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(status, Formatting.Indented)));
            }
            catch (IOException ex)
            {
                // The records are written; the status file is a convenience.
                Debug.LogWarning($"[ConnectionStore] Could not write the status file: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes beside the target and swaps it in, so a reader never sees half a file. The swap is retried for about a
        /// second: it fails while another process holds the target without sharing deletion. When it fails half-way
        /// (Windows error 1177: the target is gone and the new file is still beside it) the new file is moved into place,
        /// never deleted; and it is deleted only when the target exists.
        /// </summary>
        internal static void WriteAtomic(string path, byte[] bytes)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, bytes);

            Exception last = null;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                        File.Replace(temp, path, null);
                    else
                        File.Move(temp, path);

                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    last = ex;

                    if (!File.Exists(path) && File.Exists(temp))
                    {
                        try
                        {
                            File.Move(temp, path);
                            return;
                        }
                        catch (IOException)
                        {
                        }
                    }

                    Thread.Sleep(20);
                }
            }

            if (File.Exists(path))
            {
                try { File.Delete(temp); } catch (IOException) { }
            }

            throw last;
        }

        private static ConnectionRecord Clone(ConnectionRecord record) =>
            JsonConvert.DeserializeObject<ConnectionRecord>(JsonConvert.SerializeObject(record));

        private static void WarnMemoryOnly()
        {
            if (warnedMemoryOnly)
                return;

            warnedMemoryOnly = true;
            Debug.LogWarning("[ConnectionStore] There is no encrypted store on this platform yet (macOS Keychain support is pending): " +
                             "connections stay in memory and are lost when scripts recompile or the editor closes. Nothing is written to disk.");
        }

        #endregion
    }
}
