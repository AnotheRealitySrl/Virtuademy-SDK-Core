using Newtonsoft.Json.Linq;

using Virtuademy.SDK.Core.ApiSystem;

using System.Collections.Generic;
using System.Linq;

using Unity.Properties;

using UnityEditor;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    [CreateAssetMenu(fileName = "AppConfigurationSettings", menuName = "Virtuademy/SDK-TenantConfiguration/AppConfigurationSettings")]
    public class AppConfigurationSettings : ScriptableObject
    {
        [SerializeField] private bool isSelected = true;

        [SerializeField] private List<TextAsset> appAssets = new();

        [SerializeField] private AbstractAppConfigurator configurationScript;
        // The application's build script. Nothing in this package reads it: the Virtuademy application builds from it
        // (its BuildScriptBase), an external application with its own tools. Typed as a ScriptableObject since the build
        // moved out of this package (meta-repo ADR 0037): the reference serialized in existing assets is kept as it is.
        [SerializeField, Tooltip("The application's build script, read by the application's own build. The SDK does not use it.")]
        private ScriptableObject buildScript;

        [SerializeField] private Tenant cachedTenant;
        [SerializeField] private string cachedAppConfigJson;

        public bool IsSelected { get => isSelected; set => isSelected = value; }

        public List<TextAsset> AppAssets => appAssets;

        [SerializeField] private string selectedApp;
        [SerializeField] private string selectedEnv;
        [SerializeField] private AppIdentification selectedConfig;

        [CreateProperty]
        public string SelectedApp
        {
            get => selectedApp;
            set { selectedApp = value; EditorUtility.SetDirty(this); }
        }

        [CreateProperty]
        public string SelectedEnv
        {
            get => selectedEnv;
            set { selectedEnv = value; EditorUtility.SetDirty(this); }
        }

        [CreateProperty]
        public AppIdentification SelectedConfig
        {
            get => selectedConfig;
            set { selectedConfig = value; EditorUtility.SetDirty(this); }
        }

        [CreateProperty] public Tenant CachedTenant { get => cachedTenant; set => cachedTenant = value; }
        [CreateProperty] public string CachedAppConfigJson { get => cachedAppConfigJson; set => cachedAppConfigJson = value; }
        public JObject CachedAppConfig => string.IsNullOrEmpty(cachedAppConfigJson) ? null : JObject.Parse(cachedAppConfigJson);

        [CreateProperty] public AbstractAppConfigurator ConfigurationScript { get => configurationScript; set => configurationScript = value; }
        [CreateProperty] public ScriptableObject BuildScript { get => buildScript; set => buildScript = value; }


        /// <summary>
        /// The settings asset this project works from: the one marked selected, or the only one
        /// there is. Null when the project has none.
        /// </summary>
        /// <remarks>
        /// One rule, in one place, because the tenant window and the Android build step both need
        /// it and a project with two settings assets where they disagreed would build for a tenant
        /// nobody chose. The window adds creation on top; a build must never create anything.
        /// </remarks>
        public static AppConfigurationSettings FindSelected()
        {
            List<AppConfigurationSettings> all = AssetDatabase
                .FindAssets("t:" + nameof(AppConfigurationSettings))
                .Select(guid => AssetDatabase.LoadAssetAtPath<AppConfigurationSettings>(
                    AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null)
                .ToList();

            return all.FirstOrDefault(x => x.IsSelected) ?? all.FirstOrDefault();
        }

        /// <summary>The folder a settings asset is created in when the project has none.</summary>
        public const string DefaultFolder = "Assets/Editor/TenantConfiguration";

        /// <summary>The file name of a settings asset created in <see cref="DefaultFolder"/>.</summary>
        public const string DefaultFileName = "TenantConfiguration.asset";

        /// <summary>
        /// <see cref="FindSelected"/>, and when the project has no settings asset, a new one at
        /// <see cref="DefaultFolder"/>. What the Available Tenants window opens with; the
        /// configuration import calls it too, so a project that never opened the window can still be
        /// connected from the command line.
        /// </summary>
        public static AppConfigurationSettings FindOrCreate()
        {
            AppConfigurationSettings existing = FindSelected();
            if (existing != null)
                return existing;

            EnsureFolderExists(DefaultFolder);
            AppConfigurationSettings created = CreateInstance<AppConfigurationSettings>();
            AssetDatabase.CreateAsset(created, $"{DefaultFolder}/{DefaultFileName}");
            AssetDatabase.SaveAssets();
            return created;
        }

        internal static void EnsureFolderExists(string folderPath)
        {
            string[] folders = folderPath.Split('/');
            string current = folders[0];

            for (int i = 1; i < folders.Length; i++)
            {
                string next = current + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, folders[i]);
                current = next;
            }
        }

        public List<(string, Dictionary<string, AppIdentification>)> GetAppIdentification(List<TextAsset> configurationAssets)
        {
            List<(string, Dictionary<string, AppIdentification>)> apiConfigs = new();
            foreach (var config in configurationAssets)
            {
                if (config == null)
                    continue;

                Dictionary<string, AppIdentification> mergedConfigs = new();
                string configName = AssetDatabase.GetAssetPath(config).Split("/")[^1].Split(".")[0];
                var configDict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, AppIdentification>>(config.text);
                if (configDict != null)
                {
                    foreach (var kvp in configDict)
                    {
                        mergedConfigs[kvp.Key] = kvp.Value;
                    }
                }
                apiConfigs.Add((configName, mergedConfigs));
            }
            return apiConfigs;
        }
    }
}
