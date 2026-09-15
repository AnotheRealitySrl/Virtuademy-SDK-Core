using Newtonsoft.Json.Linq;

using Virtuademy.SDK.Core.ApiSystem;

using System.Collections.Generic;

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
        [SerializeField] private BuildScriptBase buildScript;

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
        [CreateProperty] public BuildScriptBase BuildScript { get => buildScript; set => buildScript = value; }


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
