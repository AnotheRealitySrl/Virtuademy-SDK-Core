using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEditor;

using Debug = UnityEngine.Debug;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// Lets a script (the <c>create-environment</c> skill, a terminal) ask an editor that has the project
    /// open to log in, log out or report the connections: it polls <c>UserSettings/Virtuademy/requests/</c>
    /// from <c>EditorApplication.update</c> and runs what it finds through <see cref="ConnectionCommands"/>.
    /// A batch run cannot do this while the editor has the project open (Unity refuses a second instance of
    /// a project), which is what this is for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The request</b> is a JSON file, written under another name and renamed to <c>*.json</c> so the
    /// editor never reads half of it: <c>{ "command": "login" | "logout" | "status", "connection": "...",
    /// "app": "...", "env": "...", "noBrowser": false, "loginTimeoutSeconds": 180, "resultFile": "path" }</c>.
    /// Only those three commands are accepted, never a build or a deploy, and the answer carries no token and
    /// no secret.
    /// </para>
    /// <para>
    /// <b>The answer</b> is written to <c>resultFile</c> (which must be inside the requests folder), or to
    /// <c>&lt;request&gt;.result.json</c> beside the request. A login writes it twice: first with <c>state: awaiting-browser</c> and the authorization URL,
    /// then with <c>state: done</c> and the outcome. The request file is renamed to <c>.working</c> when it is
    /// picked up and to <c>.done</c> when it is handled.
    /// </para>
    /// <para>
    /// A polling loop on the update, not a <c>FileSystemWatcher</c>: measured on Unity 6000.3 it picks a request
    /// up in 30 to 320 ms, also when the editor is unfocused, and the watcher is slower in Unity's Mono. It
    /// never runs in batch mode or in an asset-import worker process.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class EditorRequestPump
    {
        private const double poll_interval_seconds = 0.25;

        private static double nextPoll;

        static EditorRequestPump()
        {
            if (ConnectionStore.IsWorkerProcess || UnityEngine.Application.isBatchMode)
                return;

            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll)
                return;

            nextPoll = now + poll_interval_seconds;

            // Nothing is read or created while there is no request folder: the store is not touched at load.
            string folder = ProjectConnections.RequestsFolder;
            if (!Directory.Exists(folder))
                return;

            string[] requests;
            try
            {
                requests = Directory.GetFiles(folder, "*.json")
                    .Where(f => !f.EndsWith(".result.json", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(File.GetCreationTimeUtc)
                    .ToArray();
            }
            catch (IOException)
            {
                return;
            }

            foreach (string file in requests)
                _ = HandleAsync(file);
        }

        private static async Task HandleAsync(string file)
        {
            // Claim the request before anything else: a rename is atomic, so one editor handles it once.
            string working = file + ".working";
            try
            {
                File.Move(file, working);
            }
            catch (IOException)
            {
                return;
            }

            string id = Path.GetFileNameWithoutExtension(file);
            string resultFile = Path.Combine(Path.GetDirectoryName(file), id + ".result.json");
            Stopwatch clock = Stopwatch.StartNew();
            ConnectionCommandResult result;

            // A recompilation in the middle of a login would end it, and nobody would know.
            EditorApplication.LockReloadAssemblies();
            try
            {
                ConnectionCommandRequest request = Parse(working, out string error, out string requestedResult);

                // The answer goes where the request says, but only inside the requests folder: a request file is
                // somebody else's input, and it must not make the editor write wherever it names.
                if (!string.IsNullOrEmpty(requestedResult))
                {
                    string folder = Path.GetFullPath(ProjectConnections.RequestsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string wanted = Path.GetFullPath(requestedResult);

                    if (wanted.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    {
                        resultFile = wanted;
                    }
                    else if (request != null)
                    {
                        request = null;
                        error = "resultFile must be inside UserSettings/Virtuademy/requests/ of the project.";
                    }
                }

                if (request == null)
                {
                    result = new ConnectionCommandResult { Status = "bad-request", Error = error };
                }
                else if (!ConnectionCommands.RequestCommands.Contains(request.Command))
                {
                    result = new ConnectionCommandResult
                    {
                        Status = "bad-request",
                        Error = $"A request file may ask for {string.Join(", ", ConnectionCommands.RequestCommands)} only: '{request.Command}' is not one of them.",
                    };
                }
                else
                {
                    Debug.Log($"[EditorRequestPump] Request {id}: {request.Command}.");
                    result = await ConnectionCommands.ExecuteAsync(request, progress =>
                    {
                        Debug.Log($"[EditorRequestPump] Request {id}: AUTH_URL {progress["authUrl"]}");
                        WriteResult(resultFile, id, null, progress, null);
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EditorRequestPump] Request {id} failed: {ex}");
                result = new ConnectionCommandResult { Status = "login-failed", Error = ex.Message };
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
            }

            WriteResult(resultFile, id, result, null, clock.ElapsedMilliseconds);

            try
            {
                File.Move(working, file + ".done");
            }
            catch (IOException)
            {
                // The answer is written; a request file left as .working is only clutter.
            }
        }

        private static ConnectionCommandRequest Parse(string path, out string error, out string resultFile)
        {
            error = null;
            resultFile = null;

            try
            {
                JObject json = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                resultFile = (string)json["resultFile"];

                return new ConnectionCommandRequest
                {
                    Command = ((string)json["command"])?.Trim().ToLowerInvariant(),
                    Connection = (string)json["connection"],
                    App = (string)json["app"],
                    Env = (string)json["env"],
                    NoBrowser = (bool?)json["noBrowser"] ?? false,
                    LoginTimeoutSeconds = (int?)json["loginTimeoutSeconds"],
                };
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is InvalidCastException)
            {
                error = $"The request file is not the JSON of a request: {ex.GetType().Name}.";
                return null;
            }
        }

        /// <summary>Writes the answer beside the request, or where it asked, atomically.</summary>
        private static void WriteResult(string path, string id, ConnectionCommandResult result, Dictionary<string, object> progress, long? elapsedMs)
        {
            try
            {
                Dictionary<string, object> answer = new() { { "id", id } };

                if (result != null)
                {
                    answer["state"] = "done";
                    answer["status"] = result.Status;
                    answer["error"] = result.Error;
                    answer["elapsedMs"] = elapsedMs;
                    foreach (KeyValuePair<string, object> field in result.Fields)
                        answer[field.Key] = field.Value;
                }
                else
                {
                    foreach (KeyValuePair<string, object> field in progress)
                        answer[field.Key] = field.Value;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                ConnectionStore.WriteAtomic(path, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(answer, Formatting.Indented)));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[EditorRequestPump] Could not write the answer to {path}: {ex.Message}");
            }
        }
    }
}
