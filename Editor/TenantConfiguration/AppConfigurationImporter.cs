using Newtonsoft.Json;

using Virtuademy.SDK.Core.ApiSystem;

using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

using UnityEditor;

using UnityEngine;

using Debug = UnityEngine.Debug;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Connects the project to a tenant the way "Connect the SDK to your tenant" describes it: the
    /// configuration file downloaded from the Back Office (<b>Download SDK Environments Config</b>)
    /// goes into the project, into the <b>App Assets</b> of the <see cref="AppConfigurationSettings"/>
    /// asset, and is selected. The Available Tenants window and the command line
    /// (<c>EnvironmentsCli tenant-config</c>) both call <see cref="Import"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file holds the app's credential (<c>appId</c> and <c>appSecret</c>): nothing here logs its
    /// content. The default folder, <see cref="DefaultFolder"/>, is the one the App Configuration window
    /// already keeps its settings asset in (which carries the secret too) and the one a creator
    /// project's <c>.gitignore</c> is meant to ignore. When the project is inside a git work tree, the
    /// import asks git (<c>git check-ignore</c>) whether the file's destination is ignored and
    /// <b>refuses</b> when it is not (<see cref="SecretNotIgnoredException"/>), unless the caller passes
    /// <c>allowTracked</c>. Outside a git work tree, or with git missing, it proceeds with a warning.
    /// </para>
    /// <para>
    /// The file's shape is <c>{ "&lt;env&gt;": { "credential": { "appId", "appSecret" }, "apiBaseUrl",
    /// "apiVersion" } }</c>, named <c>&lt;TenantLabel&gt;-&lt;env&gt;.json</c> by the Back Office. The
    /// "app" the settings list is the file name up to its first dot, as
    /// <see cref="AppConfigurationSettings.GetAppIdentification"/> reads it.
    /// </para>
    /// </remarks>
    public static class AppConfigurationImporter
    {
        /// <summary>
        /// Where an imported file is copied to unless it already is inside the project's Assets: the
        /// folder of the settings asset (<see cref="AppConfigurationSettings.DefaultFolder"/>).
        /// </summary>
        public const string DefaultFolder = AppConfigurationSettings.DefaultFolder;

        /// <summary>
        /// The import was refused: the file would sit where git does not ignore it, and it holds the
        /// app's secret. <see cref="AssetPath"/> is the project-relative path git does not ignore.
        /// </summary>
        public sealed class SecretNotIgnoredException : InvalidOperationException
        {
            public string AssetPath { get; }

            public SecretNotIgnoredException(string path)
                : base($"The configuration file holds the app's secret and git does not ignore '{path}': it would be committed. " +
                       $"Add {AppConfigurationSettings.DefaultFolder}/ to the project's .gitignore, import to a folder it ignores, " +
                       "or allow it explicitly if you mean to track the file.")
            {
                AssetPath = path;
            }
        }

        /// <summary>What an import did, without any of the file's content.</summary>
        public sealed class ImportResult
        {
            /// <summary>The asset the file is in the project, e.g. <c>Assets/_Local/Acme-Sandbox.json</c>.</summary>
            public string AssetPath;

            /// <summary>The settings asset the file was added to.</summary>
            public string SettingsPath;

            /// <summary>The app name the settings list the file under: the file name up to its first dot.</summary>
            public string App;

            /// <summary>The environment key selected.</summary>
            public string Env;

            /// <summary>Every environment key the file carries.</summary>
            public List<string> Envs;

            /// <summary>False when the settings already listed this file.</summary>
            public bool AddedToAppAssets;

            /// <summary>True when the project already had this file with other content, and it was replaced.</summary>
            public bool ReplacedExisting;
        }

        /// <summary>
        /// Imports <paramref name="sourceFile"/> and selects it. Idempotent: a file already imported
        /// and listed changes nothing but the selection.
        /// </summary>
        /// <param name="sourceFile">The downloaded file. When it is already under the project's
        /// <c>Assets/</c> it is used where it is; otherwise it is copied into
        /// <paramref name="destinationFolder"/>.</param>
        /// <param name="env">The environment key to select. Null selects the file's first one.</param>
        /// <param name="destinationFolder">A project-relative folder, <see cref="DefaultFolder"/> by default.</param>
        /// <param name="settings">The settings asset to add it to; null takes the project's own
        /// (<see cref="AppConfigurationSettings.FindOrCreate"/>).</param>
        /// <param name="allowTracked">Import even when git does not ignore where the file (or the settings
        /// asset, which carries the secret as well) lands. Off by default: the caller has to mean it.</param>
        /// <exception cref="SecretNotIgnoredException">The project is in a git work tree and git does not
        /// ignore the destination (or the source, when it already is under <c>Assets/</c>). Nothing was
        /// written.</exception>
        /// <exception cref="InvalidOperationException">The file is missing, is not a tenant
        /// configuration, or has no such environment. The message never quotes the file.</exception>
        public static ImportResult Import(string sourceFile, string env = null, string destinationFolder = DefaultFolder,
                                          AppConfigurationSettings settings = null, bool allowTracked = false)
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
                throw new InvalidOperationException($"There is no configuration file at '{sourceFile}'.");

            string text = File.ReadAllText(sourceFile);
            Dictionary<string, AppIdentification> parsed = Parse(text, sourceFile);

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string assetPath = AssetPathInsideProject(sourceFile, projectRoot, out bool alreadyInProject);
            bool replaced = false;
            string folder = null;
            string fileName = null;

            if (!alreadyInProject)
            {
                fileName = Path.GetFileName(sourceFile);

                // A TextAsset needs a text extension: the app name comes from what precedes the first dot.
                if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    fileName += ".json";

                // A browser saves a second download of the same file as "Name (1).json" (Firefox "Name(1).json"). The copy
                // keeps the name the Back Office gave, so the app keeps its name and a newer download replaces the older one.
                fileName = Regex.Replace(fileName, @"\s?\(\d+\)(?=\.[^.]+$)", "");

                folder = (destinationFolder ?? DefaultFolder).Replace('\\', '/').Trim('/');
                if (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException($"The destination folder '{destinationFolder}' is not under Assets/.");

                assetPath = $"{folder}/{fileName}";
            }

            // Before anything is written: the secret must not touch a folder git would pick up, not even for
            // a moment. The settings asset keeps a copy of the selected credential, so it is held to the same rule.
            settings ??= AppConfigurationSettings.FindSelected();
            string settingsPath = settings != null ? AssetDatabase.GetAssetPath(settings)
                                                   : $"{AppConfigurationSettings.DefaultFolder}/{AppConfigurationSettings.DefaultFileName}";
            RequireIgnored(projectRoot, new[] { assetPath, settingsPath }, allowTracked);

            if (!alreadyInProject)
            {
                AppConfigurationSettings.EnsureFolderExists(folder);

                string destination = Path.Combine(projectRoot, assetPath);
                if (File.Exists(destination))
                {
                    replaced = File.ReadAllText(destination) != text;
                    if (replaced)
                        File.Copy(sourceFile, destination, true);
                }
                else
                {
                    File.Copy(sourceFile, destination);
                }
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset == null)
                throw new InvalidOperationException($"Unity did not import {assetPath} as a text asset.");

            string app = Path.GetFileName(assetPath).Split('.')[0];
            List<string> envs = parsed.Keys.ToList();
            string selectedEnv = env ?? envs[0];
            if (!parsed.TryGetValue(selectedEnv, out AppIdentification identification))
                throw new InvalidOperationException($"The file has no environment '{selectedEnv}'. It has: {string.Join(", ", envs)}.");

            settings ??= AppConfigurationSettings.FindOrCreate();

            bool added = !settings.AppAssets.Contains(asset);
            if (added)
                settings.AppAssets.Add(asset);

            settings.SelectedApp = app;
            settings.SelectedEnv = selectedEnv;
            settings.SelectedConfig = identification;

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log($"[AppConfigurationImporter] {assetPath}: {(added ? "added to" : "already in")} the App Assets of " +
                      $"{AssetDatabase.GetAssetPath(settings)}; {app} / {selectedEnv} selected.");

            return new ImportResult
            {
                AssetPath = assetPath,
                SettingsPath = AssetDatabase.GetAssetPath(settings),
                App = app,
                Env = selectedEnv,
                Envs = envs,
                AddedToAppAssets = added,
                ReplacedExisting = replaced
            };
        }

        /// <summary>
        /// The file's environments, checked for what a login needs. Throws without quoting the text:
        /// a parse error message can carry a fragment of it.
        /// </summary>
        private static Dictionary<string, AppIdentification> Parse(string text, string path)
        {
            Dictionary<string, AppIdentification> parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<Dictionary<string, AppIdentification>>(text);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"'{Path.GetFileName(path)}' is not a tenant configuration file (it is not the JSON the Back Office downloads).");
            }

            if (parsed == null || parsed.Count == 0)
                throw new InvalidOperationException($"'{Path.GetFileName(path)}' holds no environment.");

            foreach (KeyValuePair<string, AppIdentification> entry in parsed)
            {
                AppIdentification app = entry.Value;
                if (app?.Credential == null || app.Credential.AppId == Guid.Empty
                    || string.IsNullOrEmpty(app.Credential.AppSecret) || string.IsNullOrEmpty(app.ApiBaseUrl))
                {
                    throw new InvalidOperationException(
                        $"The environment '{entry.Key}' of '{Path.GetFileName(path)}' lacks its credential or its API base URL.");
                }
            }

            return parsed;
        }

        private static string AssetPathInsideProject(string file, string projectRoot, out bool inside)
        {
            string assets = Path.GetFullPath(Path.Combine(projectRoot, "Assets")).Replace('\\', '/').TrimEnd('/') + "/";
            string full = Path.GetFullPath(file).Replace('\\', '/');

            inside = full.StartsWith(assets, StringComparison.OrdinalIgnoreCase);
            return inside ? "Assets/" + full.Substring(assets.Length) : null;
        }

        /// <summary>
        /// Asks git whether each path is ignored (<c>git check-ignore</c>, which applies every
        /// <c>.gitignore</c>, the global one and the exclude file; a tracked file is never "ignored").
        /// Throws <see cref="SecretNotIgnoredException"/> for the first one that is not, unless
        /// <paramref name="allowTracked"/>. Not a git work tree, git missing or an answer it cannot read:
        /// a warning, and the import goes on.
        /// </summary>
        private static void RequireIgnored(string projectRoot, IEnumerable<string> assetPaths, bool allowTracked)
        {
            foreach (string path in assetPaths.Distinct())
            {
                GitIgnoreState state = CheckIgnored(projectRoot, path, out string why);

                if (state == GitIgnoreState.Ignored)
                    continue;

                if (state == GitIgnoreState.Unknown)
                {
                    Debug.LogWarning($"[AppConfigurationImporter] Could not tell whether git ignores {path} ({why}). " +
                                     "The configuration file holds the app's secret: keep it out of version control.");
                    continue;
                }

                if (!allowTracked)
                    throw new SecretNotIgnoredException(path);

                Debug.LogWarning($"[AppConfigurationImporter] git does not ignore {path}, and it holds the app's secret. " +
                                 "Imported anyway, as asked.");
            }
        }

        private enum GitIgnoreState { Ignored, NotIgnored, Unknown }

        private static GitIgnoreState CheckIgnored(string projectRoot, string assetPath, out string why)
        {
            why = null;

            if (!RunGit(projectRoot, out int code, out string output, "rev-parse", "--is-inside-work-tree"))
            {
                why = "git is not available";
                return GitIgnoreState.Unknown;
            }

            if (code != 0 || !output.Trim().StartsWith("true", StringComparison.Ordinal))
            {
                why = "the project is not inside a git work tree";
                return GitIgnoreState.Unknown;
            }

            if (!RunGit(projectRoot, out code, out _, "check-ignore", "-q", "--", assetPath))
            {
                why = "git is not available";
                return GitIgnoreState.Unknown;
            }

            // 0: ignored. 1: not ignored. Anything else is git failing (a safe.directory refusal, say).
            if (code == 0)
                return GitIgnoreState.Ignored;
            if (code == 1)
                return GitIgnoreState.NotIgnored;

            why = $"git check-ignore exited with {code}";
            return GitIgnoreState.Unknown;
        }

        /// <summary>Runs git in <paramref name="workingDirectory"/>. False when it cannot be started or does not finish.</summary>
        private static bool RunGit(string workingDirectory, out int exitCode, out string output, params string[] arguments)
        {
            exitCode = -1;
            output = "";

            ProcessStartInfo info = new("git")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in arguments)
                info.ArgumentList.Add(argument);

            try
            {
                using Process process = Process.Start(info);
                if (process == null)
                    return false;

                // Both streams are drained, or a chatty git could fill a pipe and never exit.
                process.ErrorDataReceived += (_, _) => { };
                process.BeginErrorReadLine();
                output = process.StandardOutput.ReadToEnd();

                if (!process.WaitForExit(15000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    return false;
                }

                process.WaitForExit();
                exitCode = process.ExitCode;
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
