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
    /// calls <see cref="ActiveKey"/> is what the window's login, the deploy and the other editor requests
    /// that name no connection use: the one chosen in the window (a login there makes it active), or the first one this
    /// project used. A login from the command line or a request file makes its connection active only when the
    /// project has none (<see cref="Use"/>).
    /// The deploy targets, and the world ticks that used to live in one machine-wide EditorPrefs value, join
    /// this file in a later step.
    /// </remarks>
    public static class ProjectConnections
    {
        private sealed class Selection
        {
            public List<string> Used { get; set; } = new();
            public string Active { get; set; }
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
        /// The connection the project's editor requests go to when they name none. Null when the project has
        /// none, or the one it had was removed from the store.
        /// </summary>
        public static string ActiveKey
        {
            get
            {
                lock (gate)
                {
                    Selection selection = Read();
                    string key = selection.Active;

                    if (string.IsNullOrEmpty(key))
                    {
                        List<string> existing = selection.Used.Where(k => ConnectionStore.Get(k) != null).ToList();
                        key = existing.Count == 1 ? existing[0] : null;
                    }

                    return ConnectionStore.Get(key)?.Key;
                }
            }
        }

        /// <summary>The connection play mode reads its tenant from. Not consumed yet.</summary>
        public static string PlayModeKey
        {
            get
            {
                lock (gate)
                    return Read().PlayMode;
            }
        }

        /// <summary>
        /// Records that the project uses <paramref name="key"/>, and makes it the active one when asked to or when the
        /// project has no active connection yet.
        /// </summary>
        public static void Use(string key, bool makeActive = true)
        {
            if (string.IsNullOrEmpty(key) || ConnectionStore.IsWorkerProcess)
                return;

            lock (gate)
            {
                Selection selection = Read();
                if (!selection.Used.Contains(key, StringComparer.OrdinalIgnoreCase))
                    selection.Used.Add(key);

                if (makeActive || string.IsNullOrEmpty(selection.Active))
                    selection.Active = key;

                selection.PlayMode ??= key;
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
            Directory.CreateDirectory(Folder);
            ConnectionStore.WriteAtomic(FilePath, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(selection, Formatting.Indented)));
            cache = selection;
            cacheStamp = File.GetLastWriteTimeUtc(FilePath).Ticks;
        }
    }
}
