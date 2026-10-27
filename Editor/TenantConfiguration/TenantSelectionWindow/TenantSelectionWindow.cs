using Virtuademy.SDK.Core.ApiSystem;
using Virtuademy.SDK.Core.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;

using Unity.Properties;

using UnityEditor;
using UnityEditor.UIElements;

using UnityEngine;
using UnityEngine.UIElements;

using SPACS.Utilities;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The tenant switch: which tenant and environment the project is built for (meta-repo ADR 0037, "Windows per kind of
    /// project"). It lists the configuration files the Back Office gives, selects one, and "Change config" applies it. It
    /// does no login: the editor logs in only to publish environments, which the Publish Environments window of
    /// Virtuademy-SDK-Environments does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The menu is compiled where the window has a job: an application, the platform's own or an external one. A project
    /// with Virtuademy-SDK-Environments installed is an environment project, whose play mode does not call the platform,
    /// unless it defines <c>VIRTUADEMY_APPLICATION</c>, as the Virtuademy application does.
    /// </para>
    /// <para>
    /// An application adds its own actions beside "Change config" through <see cref="ActionsCreated"/>: the Virtuademy
    /// application adds its Build button there. An external application is built with its developer's own tools.
    /// </para>
    /// </remarks>
    public class TenantSelectionWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset m_VisualTreeAsset = default;
        [SerializeField] private VisualTreeAsset appVisualTree = default;
        [SerializeField] private VisualTreeAsset envVisualTree = default;

        private AppConfigurationSettings appConfigurationSettings;

        private Button changeConfigButton;

        private ScrollView scrollView;
        private List<Toggle> toggles;
        private VisualElement selectedAppConfigSection;

        /// <summary>
        /// Raised once the window has built its buttons, with the row that holds "Change config". An application adds its
        /// own buttons to that row; a handler reads <see cref="Settings"/> when its button is clicked, because the settings
        /// asset can change while the window is open.
        /// </summary>
        public static event Action<TenantSelectionWindow, VisualElement> ActionsCreated;

        /// <summary>The settings asset the window shows, and the one a selection is written to.</summary>
        public AppConfigurationSettings Settings => appConfigurationSettings;

#if !VIRTUADEMY_SDK_ENVIRONMENTS || VIRTUADEMY_APPLICATION
        [MenuItem("Virtuademy/Available Tenants")]
#endif
        public static void ShowExample()
        {
            TenantSelectionWindow wnd = GetWindow<TenantSelectionWindow>();
            wnd.titleContent = new GUIContent("Available Tenants");
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            VisualElement labelFromUXML = m_VisualTreeAsset.Instantiate();
            root.Add(labelFromUXML);

            appConfigurationSettings = AppConfigurationSettings.FindOrCreate();

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

            Button importConfigurationButton = new(OnImportConfigurationClicked)
            {
                text = "Import configuration file...",
                tooltip = "Copies the file downloaded from the Back Office (Download SDK Environments Config) into " +
                          AppConfigurationImporter.DefaultFolder + ", adds it to the App Assets and selects it."
            };
            importConfigurationButton.style.alignSelf = Align.FlexStart;
            appConfigSettingsField.parent.Add(importConfigurationButton);

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

            ObjectChangeEvents.changesPublished += OnObjectChanged;

            try
            {
                ActionsCreated?.Invoke(this, buttonsContainer);
            }
            catch (Exception ex)
            {
                // An application's handler must not cost the window its own buttons.
                Debug.LogException(ex);
            }
        }

        private void OnDestroy()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChanged;
        }

        private void OnFocus()
        {
            if (appConfigurationSettings != null && scrollView != null)
                RefreshAvailableApps();
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
        }

        #endregion

        #region Import of the configuration file

        /// <summary>
        /// The Back Office's "Download SDK Environments Config" file into the project and onto the settings asset, through
        /// <see cref="ConfigurationFileImport"/>: the tenant switch writes <c>PlatformCredentials.asset</c> from it.
        /// </summary>
        private void OnImportConfigurationClicked()
        {
            string file = ConfigurationFileImport.PickFile();
            if (string.IsNullOrEmpty(file))
                return;

            if (ConfigurationFileImport.ImportIntoProject(file, appConfigurationSettings) != null)
                RefreshAvailableApps();
        }

        #endregion
    }
}
