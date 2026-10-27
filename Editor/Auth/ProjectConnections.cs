using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Which connections of the user's store this project uses (ADR 0037), kept per user in
    /// <c>UserSettings/Virtuademy/connections.json</c>: Unity's folder for per-user, unversioned project
    /// settings. The file names connections by key and holds no token and no secret.
    /// </summary>
    /// <remarks>
    /// There is no "current login" any more, only the connection an operation names. The one the project
    /// calls <see cref="DefaultKey"/> is what an editor request or a command that names no connection falls back to,
    /// and the one the Available Tenants window shows and "Configure App" uses. It decides neither where a deploy goes,
    /// which names its own connections, nor play mode (the tenant switch writes <c>PlatformCredentials.asset</c> from
    /// the selected file). It is chosen in the Available Tenants window (a selection
    /// or a login there makes it the default one). A login from the command line or a request file makes its
    /// connection the default one only when the project has none (<see cref="Use"/>).
    /// <para>
    /// The file also keeps the project's deploy targets (<see cref="TargetsOf"/>): per connection, the worlds and the
    /// tenant level ticked in the Addressables management window. They replace the world ticks that lived in one
    /// machine-wide EditorPrefs value and were ticked again in any tenant and project.
    /// </para>
    /// </remarks>
    public static class ProjectConnections
    {
        /// <summary>
        /// One deploy target of the project: a world of a connection, or the connection's tenant level. More per-target
        /// choices can join it without changing the file's shape.
        /// </summary>
        public sealed class DeployTarget
        {
            public string Connection { get; set; }

            /// <summary>The world, or null for the tenant level.</summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public int? WorldId { get; set; }

            [JsonIgnore]
            public bool TenantLevel => WorldId == null;
        }

        private sealed class Selection
        {
            public List<string> Used { get; set; } = new();
            public string Default { get; set; }
            public List<DeployTarget> Targets { get; set; } = new();

            /// <summary>
            /// Read only: what the first implementation called the "active connection" and wrote here. It was the one the
            /// window chose, so a file that has it takes it as its default connection, and the next write drops it.
            /// </summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string Active { get; set; }

            /// <summary>Read only: the name the default connection had in this file while it was called the play-mode connection.</summary>
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string PlayMode { get; set; }
        }

        private static readonly object gate = new();
        private static string projectFolder;
        private static Selection cache;
        private static long cacheStamp;

        /// <summary>The project's folder (the one with <c>Assets/</c>).</summary>
        internal static string ProjectFolder
        {
            get
            {
                // Application.dataPath is a main-thread call; read it once.
                projectFolder ??= Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return projectFolder;
            }
        }

        /// <summary><c>UserSettings/Virtuademy/</c> of this project.</summary>
        public static string Folder => Path.Combine(ProjectFolder, "UserSettings", "Virtuademy");

        /// <summary>Where an open editor looks for requests (<see cref="EditorRequestPump"/>).</summary>
        public static string RequestsFolder => Path.Combine(Folder, "requests");

        private static string FilePath => Path.Combine(Folder, "connections.json");

        /// <summary>The connections this project uses, in the order they were first used.</summary>
        public static IReadOnlyList<string> Used
        {
            get
            {
                lock (gate)
                    return Read().Used.ToList();
            }
        }

        /// <summary>
        /// The project's default connection: the one an editor request or a command that names no connection falls back
        /// to, and the one the Available Tenants window shows and "Configure App" uses. When none was chosen and the project uses exactly one connection of
        /// the store, that one. Null when the project has none, or the one it had was removed from the store.
        /// </summary>
        public static string DefaultKey
        {
            get
            {
                lock (gate)
                {
                    Selection selection = Read();
                    string key = selection.Default;

                    if (string.IsNullOrEmpty(key))
                    {
                        List<string> existing = selection.Used.Where(k => ConnectionStore.Get(k) != null).ToList();
                        key = existing.Count == 1 ? existing[0] : null;
                    }

                    return ConnectionStore.Get(key)?.Key;
                }
            }
        }

        /// <summary>
        /// Records that the project uses <paramref name="key"/>, and makes it the default connection when asked to or when
        /// the project has none yet.
        /// </summary>
        public static void Use(string key, bool makeDefault = true)
        {
            if (string.IsNullOrEmpty(key) || ConnectionStore.IsWorkerProcess)
                return;

            lock (gate)
            {
                Selection selection = Read();
                if (!selection.Used.Contains(key, StringComparer.OrdinalIgnoreCase))
                    selection.Used.Add(key);

                if (makeDefault || string.IsNullOrEmpty(selection.Default))
                    selection.Default = key;

                Write(selection);
            }
        }

        /// <summary>The deploy targets the project keeps for one connection: its ticked worlds and, when ticked, its tenant level.</summary>
        public static IReadOnlyList<DeployTarget> TargetsOf(string connectionKey)
        {
            if (string.IsNullOrEmpty(connectionKey))
                return Array.Empty<DeployTarget>();

            lock (gate)
            {
                return Read().Targets
                    .Where(t => string.Equals(t.Connection, connectionKey, StringComparison.OrdinalIgnoreCase))
                    .Select(t => new DeployTarget { Connection = t.Connection, WorldId = t.WorldId })
                    .ToList();
            }
        }

        /// <summary>Replaces the deploy targets of one connection; the other connections' targets are kept as they are.</summary>
        public static void SetTargets(string connectionKey, IEnumerable<int> worldIds, bool tenantLevel)
        {
            if (string.IsNullOrEmpty(connectionKey) || ConnectionStore.IsWorkerProcess)
                return;

            lock (gate)
            {
                Selection selection = Read();
                selection.Targets.RemoveAll(t => string.Equals(t.Connection, connectionKey, StringComparison.OrdinalIgnoreCase));

                foreach (int id in (worldIds ?? Enumerable.Empty<int>()).Distinct().OrderBy(id => id))
                    selection.Targets.Add(new DeployTarget { Connection = connectionKey, WorldId = id });

                if (tenantLevel)
                    selection.Targets.Add(new DeployTarget { Connection = connectionKey });

                Write(selection);
            }
        }

        private static Selection Read()
        {
            string path = FilePath;
            if (!File.Exists(path))
                return cache = new Selection();

            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            if (cache != null && stamp == cacheStamp)
                return cache;

            try
            {
                cache = JsonConvert.DeserializeObject<Selection>(File.ReadAllText(path, Encoding.UTF8)) ?? new Selection();
                cache.Used ??= new List<string>();
                cache.Targets ??= new List<DeployTarget>();

                // Older files of this change: the window's choice was Active, later PlayMode.
                if (string.IsNullOrEmpty(cache.Default))
                    cache.Default = !string.IsNullOrEmpty(cache.Active) ? cache.Active : cache.PlayMode;
                cache.Active = null;
                cache.PlayMode = null;
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                Debug.LogWarning($"[ProjectConnections] {path} cannot be read ({ex.GetType().Name}); the project's connections are chosen again.");
                cache = new Selection();
            }

            cacheStamp = stamp;
            return cache;
        }

        private static void Write(Selection selection)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                ConnectionStore.WriteAtomic(FilePath, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(selection, Formatting.Indented)));
            }
            catch
            {
                // The cached copy was changed in place: read the file again next time rather than trust it.
                cache = null;
                throw;
            }

            cache = selection;
            cacheStamp = File.GetLastWriteTimeUtc(FilePath).Ticks;
        }
    }
}
