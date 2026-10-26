using Virtuademy.SDK.Core.ApiSystem;
using Virtuademy.SDK.Core.Utilities;

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
        // Set once the project's configuration files were offered to the connection store in this editor session.
        private const string credentials_imported_key = "Virtuademy_TenantSelection_CredentialsImported";

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
        private IVisualElementScheduledItem sessionExpiryRefresh;

        private ScrollView scrollView;
        private List<Toggle> toggles;
        private VisualElement selectedAppConfigSection;

        [MenuItem("Virtuademy/Available Tenants")]
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
            EditorLoginState.OnTokenChanged += UpdateLoginUI;
            ObjectChangeEvents.changesPublished += OnObjectChanged;

            // Initial UI state
            RefreshButtonsVisibility();
            UpdateLoginUI();
            UpdateMismatchWarning();

            ImportProjectCredentials();
        }

        private void OnDestroy()
        {
            EditorLoginState.OnLoginStateChanged -= OnLoginStateChanged;
            EditorLoginState.OnTokenChanged -= UpdateLoginUI;
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

                            UseConnectionOfSelection();
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

                string sessionPart = EditorSessionManager.DescribeSession(out bool tokenValid);

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

            ScheduleSessionExpiryRefresh();
        }

        /// <summary>
        /// The label goes stale on its own when the token expires with nothing else happening:
        /// re-render it once, right after that moment. One run per token, re-armed on every
        /// render, so a renewal moves it and a logout drops it.
        /// </summary>
        private void ScheduleSessionExpiryRefresh()
        {
            sessionExpiryRefresh?.Pause();
            sessionExpiryRefresh = null;

            DateTime? usableUntil = EditorLoginState.TokenUsableUntilUtc;
            if (!EditorLoginState.IsTokenValid || usableUntil == null)
                return;

            // One second past the moment, so IsTokenValid has already flipped when it runs.
            long delayMs = (long)Math.Max(0, (usableUntil.Value - DateTime.UtcNow).TotalMilliseconds) + 1000;
            sessionExpiryRefresh = loginStatusLabel.schedule.Execute(UpdateLoginUI).StartingIn(delayMs);
        }

        private void UpdateMismatchWarning()
        {
            if (tenantMismatchLabel == null) return;

            if (EditorLoginState.IsLoggedIn
                && !string.IsNullOrEmpty(appConfigurationSettings.SelectedApp)
                && !EditorLoginState.IsLoggedInto(appConfigurationSettings.SelectedApp, appConfigurationSettings.SelectedEnv))
            {
                tenantMismatchLabel.text = $"Warning: this project's connection is {EditorLoginState.LoggedInApp}/{EditorLoginState.LoggedInEnv}. Logging in here adds a connection and makes it the project's; the other stays logged in.";
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

            if (selectedConfig == null)
            {
                Debug.LogError("[TenantSelectionWindow] No tenant configuration selected.");
                return;
            }

            // Resolving the tenant and the auth config, the Azure login, the token exchange and the role check
            // are ConnectionLoginService's: the command line runs the same steps. The login ends as a
            // connection in the per-user store, and this project uses it from now on.
            LoginOptions options = new()
            {
                MakeActive = true,   // a click in this window is the user's choice of connection
                OnStatus = status => { if (loginStatusLabel != null) loginStatusLabel.text = status; },
            };

            LoginOutcome outcome = await ConnectionLoginService.LoginAsync(
                selectedConfig, appConfigurationSettings.SelectedApp, appConfigurationSettings.SelectedEnv, options);

            if (!outcome.Success && loginStatusLabel != null)
                loginStatusLabel.text = outcome.Code == "login-timeout" ? "Login timed out" : "Login failed (see Console)";
        }

        /// <summary>
        /// Moves the project onto the stored connection of the config just selected, when there is one: its login
        /// then shows, and the deploy and the other editor requests go to its tenant.
        /// </summary>
        private void UseConnectionOfSelection()
        {
            AppIdentification selected = appConfigurationSettings.SelectedConfig;
            ConnectionRecord stored = selected?.Credential == null ? null : ConnectionStore.FindByAppId(selected.Credential.AppId);

            if (stored == null || string.Equals(stored.Key, ProjectConnections.ActiveKey, StringComparison.OrdinalIgnoreCase))
                return;

            ProjectConnections.Use(stored.Key);
            // Every window that shows the active connection (this one, the Addressables window) reloads, not only this one.
            EditorLoginState.NotifyLoginChanged();
        }

        /// <summary>
        /// The old per-project credentials of this project go into the connection store, once per editor session
        /// (ADR 0037, "Migration"). It asks the platform about each credential the store does not hold yet.
        /// </summary>
        private async void ImportProjectCredentials()
        {
            if (SessionState.GetBool(credentials_imported_key, false) || appConfigurationSettings == null)
                return;

            SessionState.SetBool(credentials_imported_key, true);

            var (added, known, failures) = await ConnectionLoginService.ImportProjectCredentialsAsync(appConfigurationSettings);
            if (added > 0 || failures.Count > 0)
            {
                Debug.Log($"[TenantSelectionWindow] The project's configuration files: {added} connection(s) added to the store, {known} already there" +
                          (failures.Count > 0 ? $", {failures.Count} not resolved: {string.Join("; ", failures)}." : ".") +
                          " Each needs one login.");
            }

            UseConnectionOfSelection();
        }

        #endregion

        #region Import of the configuration file

        /// <summary>
        /// The Back Office's "Download SDK Environments Config" file into the project and onto the
        /// settings asset, through <see cref="AppConfigurationImporter"/> — the same call the
        /// command line makes.
        /// </summary>
        private void OnImportConfigurationClicked()
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string file = EditorUtility.OpenFilePanel("Import the tenant configuration file", Directory.Exists(downloads) ? downloads : "", "json");
            if (string.IsNullOrEmpty(file))
                return;

            ImportConfigurationFile(file);
        }

        /// <summary>
        /// The import behind the button. When git does not ignore where the file would land, the import is
        /// refused and a dialog names the risk; only an explicit "Import anyway" repeats it with the override. That
        /// button is the dialog's third one, so Enter, Escape and a dialog nobody can answer (batch mode) all end
        /// in "not imported".
        /// </summary>
        private void ImportConfigurationFile(string file, bool allowTracked = false)
        {
            try
            {
                AppConfigurationImporter.ImportResult result = AppConfigurationImporter.Import(
                    file, settings: appConfigurationSettings, allowTracked: allowTracked);
                RefreshAvailableApps();
                Debug.Log($"[TenantSelectionWindow] Imported {result.AssetPath}: {result.App} / {result.Env} is selected.");
            }
            catch (AppConfigurationImporter.SecretNotIgnoredException ex)
            {
                Debug.LogError($"[TenantSelectionWindow] The configuration file was not imported: {ex.Message}");

                int choice = EditorUtility.DisplayDialogComplex("The configuration file would be committed",
                    $"The file holds the app's secret, and git does not ignore '{ex.AssetPath}'. " +
                    "Importing it there puts the secret one commit away from the repository (and from everyone who clones it).\n\n" +
                    $"Safer: add {AppConfigurationSettings.DefaultFolder}/ to the project's .gitignore, then import again.",
                    "Don't import", "Cancel", "Import anyway");

                if (choice == 2)
                    ImportConfigurationFile(file, allowTracked: true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TenantSelectionWindow] The configuration file was not imported: {ex.Message}");
                EditorUtility.DisplayDialog("Import failed", ex.Message, "OK");
            }
        }

        #endregion
    }
}
