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
    /// The connections an environment project works with, and the deploy targets it keeps (ADR 0037). The connections are
    /// every one in the user's store (<see cref="All"/>): the project keeps no list of its own. The targets are kept per
    /// user in <c>UserSettings/Virtuademy/connections.json</c>, Unity's folder for per-user, unversioned project settings.
    /// The file names connections by key and holds no token and no secret.
    /// </summary>
    /// <remarks>
    /// There is no "current login" any more, only the connection an operation names. An editor request or a command
    /// that names no connection uses the only usable connection of the computer, <see cref="DefaultKey"/>; with none, or
    /// two or more, there is none, and such a request is refused: it must name its connection. Nobody chooses a default
    /// connection (that choice was dropped on 2026-10-27, before 2026.7.0 was released). It decides neither where a deploy
    /// goes, which names its own connections, nor play mode (the tenant switch writes <c>PlatformCredentials.asset</c> from
    /// the selected file).
    /// <para>
    /// The file keeps the project's deploy targets (<see cref="TargetsOf"/>): per connection, the worlds and the tenant
    /// level ticked in the Publish Environments window. They replace the world ticks that lived in one machine-wide
    /// EditorPrefs value and were ticked again in any tenant and project. A target whose connection is no longer in the
    /// store is dropped the next time the file is read, when the store itself was read from its file.
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
            public List<DeployTarget> Targets { get; set; } = new();

            // The files written while a default connection could be chosen carry "Default" (earlier "Active", then
            // "PlayMode"), and those written while a project kept a list of its connections carry "Used": nothing reads
            // them, and the next write drops them.
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

        /// <summary>
        /// Every connection in the store, which every environment project uses. Sorted by tenant label, then environment,
        /// then key (ordinal, case-insensitive), so that the order does not change when a record is rewritten.
        /// </summary>
        public static IReadOnlyList<ConnectionRecord> All() =>
            ConnectionStore.All()
                .OrderBy(c => c.TenantLabel ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Environment ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Key ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// <see cref="All"/> without the connections whose app key is not a CreatorKit app
        /// (<see cref="ConnectionRecord.PlatformMismatch"/>): they stay listed, but nothing deploys through them.
        /// </summary>
        public static IReadOnlyList<ConnectionRecord> Usable() => All().Where(c => !c.PlatformMismatch).ToList();

        /// <summary>
        /// The connection an editor request or a command that names no connection uses: the only usable connection in the
        /// store. Null when there is none, or two or more: a request must then name its connection.
        /// </summary>
        public static string DefaultKey
        {
            get
            {
                IReadOnlyList<ConnectionRecord> usable = Usable();
                return usable.Count == 1 ? usable[0].Key : null;
            }
        }

        /// <summary>How many connections of the store are usable (<see cref="Usable"/>).</summary>
        public static int UsableCount => Usable().Count;

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

        /// <summary>
        /// Drops this project's deploy targets of one connection, which was removed from the computer
        /// (<see cref="ConnectionLoginService.RemoveFromComputerAsync"/>).
        /// </summary>
        internal static void DropTargets(string connectionKey) => SetTargets(connectionKey, null, false);

        private static Selection Read()
        {
            string path = FilePath;
            if (!File.Exists(path))
                return cache = new Selection();

            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            if (cache != null && stamp == cacheStamp)
            {
                Selection current = cache;
                DropOrphanTargets(current);
                return current;
            }

            try
            {
                cache = JsonConvert.DeserializeObject<Selection>(File.ReadAllText(path, Encoding.UTF8)) ?? new Selection();
                cache.Targets ??= new List<DeployTarget>();
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                Debug.LogWarning($"[ProjectConnections] {path} cannot be read ({ex.GetType().Name}); the project's deploy targets are chosen again.");
                cache = new Selection();
            }

            cacheStamp = stamp;
            Selection read = cache;
            DropOrphanTargets(read);
            return read;
        }

        /// <summary>
        /// Drops the targets of a connection that is no longer in the store (it was removed from the computer, possibly from
        /// another project). Only when the store was actually read from its file (<see cref="ConnectionStore.WasRead"/>): an
        /// asset-import worker, a memory-only store (empty after every restart), a missing file and a file that cannot be read
        /// at the moment say nothing about which connections exist, and must not wipe the targets.
        /// </summary>
        private static void DropOrphanTargets(Selection selection)
        {
            if (selection.Targets.Count == 0 || ConnectionStore.IsWorkerProcess || !ConnectionStore.WasRead)
                return;

            HashSet<string> keys = new(ConnectionStore.All().Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
            if (selection.Targets.RemoveAll(t => !keys.Contains(t.Connection ?? "")) == 0)
                return;

            try
            {
                Write(selection);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The cached copy is already right; the file is cleaned by the next write.
                Debug.LogWarning($"[ProjectConnections] The targets of connections that are gone could not be removed from {FilePath}: {ex.Message}");
            }
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
