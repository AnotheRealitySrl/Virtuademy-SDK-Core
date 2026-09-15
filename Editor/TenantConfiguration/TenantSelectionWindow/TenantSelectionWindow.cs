using Virtuademy.SDK.Core.ApiSystem;
using Virtuademy.SDK.Core.Utilities;
using Virtuademy.SDK.Http;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Unity.Properties;

using UnityEditor;
using UnityEditor.UIElements;

using UnityEngine;
using UnityEngine.UIElements;

using SPACS.Utilities;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    public class TenantSelectionWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset m_VisualTreeAsset = default;
        [SerializeField] private VisualTreeAsset appVisualTree = default;
        [SerializeField] private VisualTreeAsset envVisualTree = default;
        [SerializeField] private VisualTreeAsset appConfigurationVisualTree = default;

        private AppConfigurationSettings appConfigurationSettings;

        private Button configureAppButton;
        private Button changeConfigButton;
        private Button buildButton;
        private Label loginStatusLabel;
        private Label tenantMismatchLabel;
        private Button loginButton;
        private Button logoutButton;

        private ScrollView scrollView;
        private List<Toggle> toggles;
        private VisualElement selectedAppConfigSection;

        private const string settings_folder_path = "Assets/Editor/TenantConfiguration";
        private const string settings_configuration_path = "TenantConfiguration.asset";

        [MenuItem("Virtuademy/Show available tenants")]
        public static void ShowExample()
        {
            TenantSelectionWindow wnd = GetWindow<TenantSelectionWindow>();
            wnd.titleContent = new GUIContent("Show available tenants");
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            VisualElement labelFromUXML = m_VisualTreeAsset.Instantiate();
            root.Add(labelFromUXML);

            appConfigurationSettings = FindOrCreateAppConfigurationSettings();

            // AppConfigurationSettings ObjectField
            ObjectField appConfigSettingsField = root.Q<ObjectField>("AppConfigurationSettingsField");
            appConfigSettingsField.objectType = typeof(AppConfigurationSettings);
            appConfigSettingsField.value = appConfigurationSettings;
            appConfigSettingsField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is AppConfigurationSettings newSettings && newSettings != null)
                {
                    appConfigurationSettings = newSettings;
                    Selection.activeObject = newSettings;
                    EditorGUIUtility.PingObject(newSettings);
                    RefreshAvailableApps();
                }
            });

            selectedAppConfigSection = root.Q<VisualElement>("SelectedAppConfig");
            scrollView = root.Q<ScrollView>();
            toggles = new();

            RefreshAvailableApps();

            // Selected config data bindings
            selectedAppConfigSection.dataSource = appConfigurationSettings.SelectedConfig;

            Label appIdLabel = selectedAppConfigSection.Q<VisualElement>("AppId").Q<Label>("Value");
            appIdLabel.SetBinding(nameof(appIdLabel.text), new DataBinding()
            {
                dataSourcePath = new PropertyPath($"{nameof(AppIdentification.Credential)}.{nameof(HmacCredential.AppId)}"),
                bindingMode = BindingMode.ToTarget
            });

            Label appSecretLabel = selectedAppConfigSection.Q<VisualElement>("AppSecret").Q<Label>("Value");
            appSecretLabel.SetBinding(nameof(appSecretLabel.text), new DataBinding()
            {
                dataSourcePath = new PropertyPath($"{nameof(AppIdentification.Credential)}.{nameof(HmacCredential.AppSecret)}"),
                bindingMode = BindingMode.ToTarget
            });

            Label appConfigurationUrlLabel = selectedAppConfigSection.Q<VisualElement>("ApiBaseUrl").Q<Label>("Value");
            appConfigurationUrlLabel.SetBinding(nameof(appConfigurationUrlLabel.text), new DataBinding()
            {
                dataSourcePath = PropertyPath.FromName(nameof(AppIdentification.ApiBaseUrl)),
                bindingMode = BindingMode.ToTarget
            });

            Label appConfigurationVersionLabel = selectedAppConfigSection.Q<VisualElement>("ApiVersion").Q<Label>("Value");
            appConfigurationVersionLabel.SetBinding(nameof(appConfigurationVersionLabel.text), new DataBinding()
            {
                dataSourcePath = PropertyPath.FromName(nameof(AppIdentification.ApiVersion)),
                bindingMode = BindingMode.ToTarget
            });

            // Login section
            tenantMismatchLabel = root.Q<Label>("TenantMismatchLabel");
            loginStatusLabel = root.Q<Label>("LoginStatusLabel");
            loginButton = root.Q<Button>("LoginButton");
            logoutButton = root.Q<Button>("LogoutButton");

            loginButton.clicked += OnLoginClicked;
            logoutButton.clicked += OnLogoutClicked;

            // Buttons container
            VisualElement buttonsContainer = root.Q<VisualElement>("ButtonsContainer");
            buttonsContainer.dataSource = appConfigurationSettings;

            changeConfigButton = buttonsContainer.Q<Button>("ChangeConfigButton");
            changeConfigButton.clicked += async () =>
            {
                // An application with its own configurator does more than switch tenant — it reads
                // a product name, a theme, a set of localized assets out of the app's custom
                // config — and that configurator writes the generated assets on its way through.
                // A project without one still needs them written, which is what TenantSwitch is.
                if (appConfigurationSettings.ConfigurationScript != null)
                {
                    await appConfigurationSettings.ConfigurationScript.ConfigureApp(appConfigurationSettings);
                }
                else
                {
                    await TenantSwitch.Apply(appConfigurationSettings);
                }
            };

            buildButton = buttonsContainer.Q<Button>("BuildButton");
            buildButton.clicked += () =>
            {
                appConfigurationSettings.BuildScript.Build(appConfigurationSettings.SelectedEnv, appConfigurationSettings.SelectedConfig, appConfigurationSettings);
            };

            configureAppButton = buttonsContainer.Q<Button>("ConfigureAppButton");
            configureAppButton.clicked += () =>
            {
                AppConfigurationWindow.ShowWindow();
                GetWindow<AppConfigurationWindow>().ShowAppConfigurationWindow(appConfigurationSettings.SelectedConfig, appConfigurationSettings);
            };

            // Subscribe to login state changes and asset changes
            EditorLoginState.OnLoginStateChanged += OnLoginStateChanged;
            ObjectChangeEvents.changesPublished += OnObjectChanged;

            // Initial UI state
            RefreshButtonsVisibility();
            UpdateLoginUI();
            UpdateMismatchWarning();
        }

        private void OnDestroy()
        {
            EditorLoginState.OnLoginStateChanged -= OnLoginStateChanged;
            ObjectChangeEvents.changesPublished -= OnObjectChanged;
        }

        private void OnFocus()
        {
            if (appConfigurationSettings != null && scrollView != null)
                RefreshAvailableApps();
        }

        private void OnLoginStateChanged()
        {
            RefreshButtonsVisibility();
            UpdateLoginUI();
            UpdateMismatchWarning();
        }

        private void OnObjectChanged(ref ObjectChangeEventStream stream)
        {
            if (appConfigurationSettings == null || scrollView == null) return;

            int settingsId = appConfigurationSettings.GetInstanceID();
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) == ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var data);
                    if (data.instanceId == settingsId)
                    {
                        RefreshAvailableApps();
                        break;
                    }
                }
            }
        }

        #region Available apps

        private void RefreshAvailableApps()
        {
            scrollView.Clear();
            toggles.Clear();

            var allApps = appConfigurationSettings.GetAppIdentification(appConfigurationSettings.AppAssets);

            bool hasValidSelection = appConfigurationSettings.SelectedConfig != null
                && !string.IsNullOrEmpty(appConfigurationSettings.SelectedConfig.ApiBaseUrl)
                && !string.IsNullOrEmpty(appConfigurationSettings.SelectedApp);

            if (!hasValidSelection && allApps.Count > 0)
            {
                var firstApp = allApps[0];
                if (firstApp.Item2.Count > 0)
                {
                    var firstEnv = firstApp.Item2.First();
                    appConfigurationSettings.SelectedApp = firstApp.Item1;
                    appConfigurationSettings.SelectedEnv = firstEnv.Key;
                    appConfigurationSettings.SelectedConfig = firstEnv.Value;
                }
            }

            foreach (var app in allApps)
            {
                VisualElement appElement = appVisualTree.Instantiate();
                appElement.Q<Label>().text = app.Item1;

                GroupBox togglesContainer = appElement.Q<GroupBox>();

                foreach (var envConfig in app.Item2)
                {
                    VisualElement envElement = envVisualTree.Instantiate();

                    Toggle toggle = envElement.Q<Toggle>();
                    toggles.Add(toggle);
                    toggle.dataSource = (app.Item1, envConfig.Key);
                    toggle.text = envConfig.Key;
                    toggle.RegisterCallback<ChangeEvent<bool>>(evt =>
                    {
                        if (evt.newValue)
                        {
                            appConfigurationSettings.SelectedConfig = envConfig.Value;
                            selectedAppConfigSection.dataSource = appConfigurationSettings.SelectedConfig;

                            appConfigurationSettings.SelectedEnv = envConfig.Key;
                            appConfigurationSettings.SelectedApp = app.Item1;

                            RefreshButtonsVisibility();
                            UpdateMismatchWarning();
                        }
                    });

                    DataBinding toggleBinding = new()
                    {
                        dataSourcePath = new(),
                        bindingMode = BindingMode.ToTarget
                    };
                    toggleBinding.sourceToUiConverters.AddConverter(
                        (ref (string, string) value) => value.Item1 == appConfigurationSettings.SelectedApp && value.Item2 == appConfigurationSettings.SelectedEnv);
                    toggle.SetBinding(nameof(toggle.value), toggleBinding);

                    togglesContainer.Add(envElement);
                }
                scrollView.Add(appElement);
            }

            if (selectedAppConfigSection != null)
                selectedAppConfigSection.dataSource = appConfigurationSettings.SelectedConfig;

            RefreshButtonsVisibility();
            UpdateMismatchWarning();
        }

        #endregion

        #region Button visibility

        private void RefreshButtonsVisibility()
        {
            if (changeConfigButton != null)
            {
                // Shown unconditionally: with a configurator the button runs it, without one it
                // applies the tenant, and there is no project for which applying a tenant is
                // meaningless. Hiding it when no configurator existed is what left an external
                // application able to pick a tenant and unable to apply it.
                changeConfigButton.style.display = DisplayStyle.Flex;
            }

            if (buildButton != null)
            {
                bool showBuild = appConfigurationSettings.BuildScript != null;
                buildButton.style.display = showBuild ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (configureAppButton != null)
            {
                bool showAppConfig = EditorLoginState.IsLoggedIn
                    && EditorLoginState.IsTenantManager
                    && EditorLoginState.IsLoggedInto(appConfigurationSettings.SelectedApp, appConfigurationSettings.SelectedEnv);

                configureAppButton.style.display = showAppConfig ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        #endregion

        #region Login

        private void UpdateLoginUI()
        {
            if (loginStatusLabel == null) return;

            bool loggedIn = EditorLoginState.IsLoggedIn;

            if (loggedIn)
            {
                string tenantLabel = EditorLoginState.CurrentTenant?.Label ?? "Unknown";
                string username = EditorLoginState.Username;
                string userPart = !string.IsNullOrEmpty(username) ? $" - {username}" : string.Empty;
                string rolePart = EditorLoginState.IsTenantManager ? " [TenantManager]" : "";

                // An expired token is not an error state: the next operation renews it. Say so,
                // instead of showing a green "logged in" that hides a round trip to Azure.
                bool tokenValid = EditorLoginState.IsTokenValid;
                string sessionPart = tokenValid
                    ? $" (session until {EditorSessionManager.DescribeExpiry()})"
                    : " - session expired, will be renewed on the next operation";

                loginStatusLabel.text = $"Logged in: {tenantLabel}{userPart}{rolePart}{sessionPart}";
                loginStatusLabel.style.color = tokenValid
                    ? new Color(0.2f, 0.8f, 0.2f)
                    : new Color(0.9f, 0.7f, 0.2f);
            }
            else
            {
                loginStatusLabel.text = "Not logged in";
                loginStatusLabel.style.color = new Color(0.8f, 0.2f, 0.2f);
            }

            if (loginButton != null)
                loginButton.style.display = loggedIn ? DisplayStyle.None : DisplayStyle.Flex;
            if (logoutButton != null)
                logoutButton.style.display = loggedIn ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateMismatchWarning()
        {
            if (tenantMismatchLabel == null) return;

            if (EditorLoginState.IsLoggedIn
                && !string.IsNullOrEmpty(appConfigurationSettings.SelectedApp)
                && !EditorLoginState.IsLoggedInto(appConfigurationSettings.SelectedApp, appConfigurationSettings.SelectedEnv))
            {
                tenantMismatchLabel.text = $"Warning: you are logged into {EditorLoginState.LoggedInApp}/{EditorLoginState.LoggedInEnv}. Logging in here will switch your session.";
                tenantMismatchLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                tenantMismatchLabel.style.display = DisplayStyle.None;
            }
        }

        private async void OnLogoutClicked()
        {
            await EditorSessionManager.LogoutAsync();
        }

        private async void OnLoginClicked()
        {
            AppIdentification selectedConfig = appConfigurationSettings.SelectedConfig;
            string selectedApp = appConfigurationSettings.SelectedApp;
            string selectedEnv = appConfigurationSettings.SelectedEnv;

            if (selectedConfig == null)
            {
                Debug.LogError("[TenantSelectionWindow] No tenant configuration selected.");
                return;
            }

            try
            {
                loginStatusLabel.text = "Fetching tenant data...";

                // 1. Get tenant data
                ApiResponse<Tenant> tenantResp = await TenantConfigurationClient.GetTenantData(selectedConfig);
                if (!tenantResp.IsSuccess)
                {
                    Debug.LogError($"[TenantSelectionWindow] Failed to get tenant data: {tenantResp.ReasonPhrase}");
                    loginStatusLabel.text = "Login failed (tenant data)";
                    return;
                }

                Tenant tenant = tenantResp.Content;

                // 2. Read auth config from tenant configuration
                //    Falls back to legacy custom config if tenant authConfig is not populated yet
                AzureB2CConfig b2cConfig = tenant.Config.AuthConfig;
                if (b2cConfig == null)
                {
                    ApiResponse<Newtonsoft.Json.Linq.JObject> customConfigResp = await TenantConfigurationClient.GetAppCustomConfig(selectedConfig);
                    if (customConfigResp.IsSuccess)
                    {
                        b2cConfig = AzureB2CConfig.FromAppCustomConfig(customConfigResp.Content);
                    }
                }

                if (b2cConfig == null)
                {
                    Debug.LogError("[TenantSelectionWindow] Failed to get auth config from tenant or custom config.");
                    loginStatusLabel.text = "Login failed (auth config)";
                    return;
                }

                // 3. Validate config based on auth type
                if (b2cConfig.IsEntraId)
                {
                    if (string.IsNullOrEmpty(b2cConfig.Tenant))
                    {
                        Debug.LogError($"[TenantSelectionWindow] Invalid Entra ID config — Tenant (tenant ID) is empty.");
                        loginStatusLabel.text = "Login failed (Entra ID config invalid)";
                        return;
                    }
                }
                else if (b2cConfig.IsB2C)
                {
                    if (string.IsNullOrEmpty(b2cConfig.Tenant) || string.IsNullOrEmpty(b2cConfig.Policy))
                    {
                        Debug.LogError($"[TenantSelectionWindow] Invalid B2C config — Tenant: '{b2cConfig.Tenant}', Policy: '{b2cConfig.Policy}'.");
                        loginStatusLabel.text = "Login failed (B2C config invalid)";
                        return;
                    }
                }
                else
                {
                    Debug.LogError($"[TenantSelectionWindow] Unrecognized auth policy: '{b2cConfig.Policy}'. Expected 'EntraID' or a value starting with 'B2C_'.");
                    loginStatusLabel.text = "Login failed (unknown auth policy)";
                    return;
                }

                // 4. clientId = AppIdentification.Credential.AppId
                string clientId = selectedConfig.Credential.AppId.ToString();

                // 5. Azure login, token exchange, role check and session storage — including the
                //    auth config, which the session manager needs to renew the token later on.
                loginStatusLabel.text = "Logging in...";

                if (!await EditorSessionManager.LoginAsync(tenant, b2cConfig, clientId, selectedApp, selectedEnv))
                {
                    loginStatusLabel.text = "Login failed (see Console)";
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TenantSelectionWindow] Login error: {ex.Message}");
                loginStatusLabel.text = "Login failed";
            }
        }

        #endregion

        #region AppConfigurationSettings resolution

        private AppConfigurationSettings FindOrCreateAppConfigurationSettings()
        {
            AppConfigurationSettings existing = AppConfigurationSettings.FindSelected();

            if (existing != null)
            {
                return existing;
            }

            EnsureFolderExists(settings_folder_path);
            AppConfigurationSettings newSettings = CreateInstance<AppConfigurationSettings>();
            string assetPath = $"{settings_folder_path}/{settings_configuration_path}";
            AssetDatabase.CreateAsset(newSettings, assetPath);
            AssetDatabase.SaveAssets();
            return newSettings;
        }

        private void EnsureFolderExists(string folderPath)
        {
            string[] folders = folderPath.Split('/');
            string currentPath = "";

            foreach (string folder in folders)
            {
                currentPath = Path.Combine(currentPath, folder);
                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(currentPath), Path.GetFileName(currentPath));
                }
            }
        }

        #endregion
    }
}
