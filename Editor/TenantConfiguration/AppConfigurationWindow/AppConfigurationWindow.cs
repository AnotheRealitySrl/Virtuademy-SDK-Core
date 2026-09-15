using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Virtuademy.SDK.Core.ApiSystem;
using Virtuademy.SDK.Http;

using System;
using System.Collections.Generic;

using Unity.Properties;

using UnityEditor;

using UnityEngine;
using UnityEngine.UIElements;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    // Wrapper class to make configuration items editable via data binding
    [System.Serializable]
    public class EditableConfigItem
    {
        [CreateProperty] public string Key { get; set; }
        [CreateProperty] public object Value { get; set; }

        public EditableConfigItem(string key, object value)
        {
            Key = key;
            Value = value;
        }
    }

    public class AppConfigurationWindow : EditorWindow
    {
        [SerializeField] private VisualTreeAsset m_VisualTreeAsset = default;

        [SerializeField] private VisualTreeAsset configurationItemTextField = default;
        [SerializeField] private VisualTreeAsset configurationItemNumericField = default;
        [SerializeField] private VisualTreeAsset configurationItemCheckBox = default;

        private VisualElement root;

        private List<EditableConfigItem> editableAppConfigurationItems;

        private VisualElement appConfigContainerTyped;
        private TextField rawTextField;

        [CreateProperty] private bool isRawEditMode = false;

        public static AppConfigurationWindow ShowWindow()
        {
            AppConfigurationWindow wnd = GetWindow<AppConfigurationWindow>();
            wnd.titleContent = new GUIContent("AppConfigurationEditorWindow");

            return wnd;
        }

        public void CreateGUI()
        {
            root = rootVisualElement;
            VisualElement labelFromUXML = m_VisualTreeAsset.Instantiate();
            root.Add(labelFromUXML);
        }

        public async void ShowAppConfigurationWindow(AppIdentification app, AppConfigurationSettings appConfigurationSettings)
        {
            AppIdentification appConfig = new(app.Credential, app.ApiBaseUrl, app.ApiVersion);

            var tenantDataResponse = await TenantConfigurationClient.GetTenantData(appConfig);
            if (!tenantDataResponse.IsSuccess)
            {
                Debug.LogError($"Failed to get tenant data: {tenantDataResponse.ReasonPhrase}");
                return;
            }

            Label appName = root.Q<VisualElement>("AppName").Q<Label>();
            appName.text = tenantDataResponse.Content.Label;

            var appCustomConfigResponse = await TenantConfigurationClient.GetAppCustomConfig(appConfig);
            JObject customAppConfig = appCustomConfigResponse.IsSuccess ? appCustomConfigResponse.Content : null;

            editableAppConfigurationItems = new List<EditableConfigItem>();
            if (customAppConfig != null)
            {
                foreach (var el in customAppConfig)
                {
                    // Normalizza i JValue in primitivi .NET per evitare che vengano trattati come stringhe
                    editableAppConfigurationItems.Add(new EditableConfigItem(el.Key, NormalizeJToken(el.Value)));
                }
            }

            VisualElement appConfigContainer = root.Q<VisualElement>("AppPropertiesContainer");
            appConfigContainer.dataSource = this;


            appConfigContainerTyped = appConfigContainer.Q<VisualElement>("AppPropertiesContainerTyped");
            appConfigContainerTyped.Clear();
            PopolateContainer(editableAppConfigurationItems, appConfigContainerTyped);

            DataBinding appConfigContainerBinding = new()
            {
                dataSourcePath = PropertyPath.FromName(nameof(isRawEditMode)),
                bindingMode = BindingMode.ToTarget
            };
            appConfigContainerBinding.sourceToUiConverters.AddConverter((ref bool value) =>
            {
                appConfigContainerTyped.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
                return true;
            });
            appConfigContainerTyped.SetBinding($"{nameof(VisualElement.visible)}", appConfigContainerBinding);


            VisualElement rawConfigContainer = appConfigContainer.Q<VisualElement>("AppPropertiesContainerRaw");
            TextField rawTextField = rawConfigContainer.Q<TextField>();
            rawTextField.value = customAppConfig != null ? customAppConfig.ToString(Formatting.Indented) : "";

            DataBinding rawConfigContainerBinding = new()
            {
                dataSourcePath = PropertyPath.FromName(nameof(isRawEditMode)),
                bindingMode = BindingMode.ToTarget
            };
            rawConfigContainerBinding.sourceToUiConverters.AddConverter((ref bool value) =>
            {
                rawConfigContainer.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                return true;
            });
            rawConfigContainer.SetBinding($"{nameof(VisualElement.visible)}", rawConfigContainerBinding);



            Button updateAppbutton = root.Q<Button>("UpdateAppConfigurationButton");
            updateAppbutton.clicked -= OnUpdateClicked; // evita duplicazioni se riaperto
            updateAppbutton.clicked += OnUpdateClicked;

            Toggle editModeClicked = root.Q<Toggle>("EditModeToggle");
            editModeClicked.RegisterCallback<ChangeEvent<bool>>(evt =>
            {
                isRawEditMode = !isRawEditMode;
            });

            async void OnUpdateClicked()
            {
                string updatedConfig = !isRawEditMode ? BuildUpdatedConfigJObject().ToString(Formatting.Indented) : rawTextField.text;
                ApiResponse appCustomConfigUpdateReq = await TenantConfigurationClient.UpdateAppCustomConfig(appConfig, updatedConfig);
                if (appCustomConfigUpdateReq.IsSuccess)
                {
                    Debug.Log($"App configuration updated successfully. New config: {updatedConfig}");
                }
                else
                {
                    Debug.LogError($"Failed to update app configuration: {appCustomConfigUpdateReq.StatusCode} {appCustomConfigUpdateReq.ReasonPhrase}");
                }

            }
        }

        private JObject BuildUpdatedConfigJObject()
        {
            var jobj = new JObject();
            foreach (var item in editableAppConfigurationItems)
            {
                if (item.Value is JToken jt)
                {
                    jobj[item.Key] = jt;
                }
                else
                {
                    // Usa FromObject per preservare tipo numerico/bool
                    jobj[item.Key] = JToken.FromObject(item.Value ?? JValue.CreateNull());
                }
            }
            return jobj;
        }

        private object NormalizeJToken(JToken token)
        {
            if (token is JValue jv)
            {
                switch (jv.Type)
                {
                    case JTokenType.Integer:
                        // Usa Int64 per compatibilit� JSON numerica generica
                        return jv.Value<long>();
                    case JTokenType.Float:
                        return jv.Value<double>();
                    case JTokenType.Boolean:
                        return jv.Value<bool>();
                    case JTokenType.String:
                        return jv.Value<string>();
                    case JTokenType.Null:
                        return null;
                    default:
                        return jv.Value<object>();
                }
            }
            if (token is JObject || token is JArray)
                return token; // mantieni come JToken complesso
            return token;
        }

        private void PopolateContainer(IEnumerable<EditableConfigItem> editableConfigItems, VisualElement container)
        {
            foreach (var editableItem in editableConfigItems)
            {

                // Gestione JValue rimasti (nel caso di caricamenti futuri)
                if (editableItem.Value is JValue jv)
                {
                    editableItem.Value = NormalizeJToken(jv);
                }

                VisualElement visualElement;
                switch (editableItem.Value)
                {
                    case string _:
                        visualElement = CreateTextFieldItem(editableItem);
                        break;
                    case long _:
                    case int _:
                        visualElement = CreateNumericFieldItem(editableItem);
                        break;
                    case double _:
                    case float _:
                    case decimal _:
                        // Per semplicit�, numeri floating usano un TextField (potresti creare un DoubleField separato)
                        visualElement = CreateTextFieldItem(editableItem);
                        break;
                    case bool _:
                        visualElement = CreateCheckBoxItem(editableItem);
                        break;
                    case JObject _:
                    case JArray _:
                        visualElement = CreateObjectFieldItem(editableItem);
                        break;
                    case null:
                        visualElement = CreateTextFieldItem(editableItem);
                        break;
                    default:
                        visualElement = CreateTextFieldItem(editableItem);
                        break;
                }
                container.Add(visualElement);
            }
        }

        // CreateObjectFieldItem:
        // 1. Instantiate same VisualTreeAsset used for text fields.
        // 2. Configure TextField as multiline to display JSON.
        // 3. Initialize value with pretty printed JSON (using JToken.ToString or JsonConvert).
        // 4. On change, try parse JSON; if success update EditableConfigItem.Value with parsed JToken.
        // 5. If parsing fails, add a CSS class "error"; keep previous valid JToken.
        private VisualElement CreateObjectFieldItem(EditableConfigItem editableItem)
        {
            VisualElement configItem = configurationItemTextField.Instantiate();

            TextField textField = configItem.Q<TextField>();
            if (textField == null)
                return configItem;

            textField.multiline = true;
            textField.label = editableItem.Key;

            if (editableItem.Value is JToken token)
            {
                textField.value = token.ToString(Formatting.Indented);
            }
            else
            {
                try
                {
                    textField.value = JsonConvert.SerializeObject(editableItem.Value, Formatting.Indented);
                    editableItem.Value = JToken.Parse(textField.value);
                }
                catch
                {
                    textField.value = "{}";
                    editableItem.Value = JToken.Parse(textField.value);
                }
            }

            JToken lastValid = editableItem.Value as JToken;

            textField.RegisterValueChangedCallback(evt =>
            {
                var newValue = evt.newValue;
                try
                {
                    var parsed = JToken.Parse(newValue);
                    editableItem.Value = parsed;
                    lastValid = parsed;
                    textField.RemoveFromClassList("error");
                }
                catch
                {
                    textField.AddToClassList("error");
                }
            });

            return configItem;
        }

        private VisualElement CreateTextFieldItem(EditableConfigItem editableItem)
        {
            VisualElement configItem = configurationItemTextField.Instantiate();
            TextField textField = configItem.Q<TextField>();

            if (textField == null)
                return configItem;

            textField.label = editableItem.Key;
            textField.value = editableItem.Value?.ToString() ?? string.Empty;

            textField.RegisterValueChangedCallback(evt =>
            {
                // Mantieni come stringa pura
                editableItem.Value = evt.newValue;
            });

            return configItem;
        }

        private VisualElement CreateNumericFieldItem(EditableConfigItem editableItem)
        {
            VisualElement configItem = configurationItemNumericField.Instantiate();
            IntegerField integerField = configItem.Q<IntegerField>();

            if (integerField == null)
                return configItem;

            integerField.label = editableItem.Key;
            integerField.value = Convert.ToInt32(editableItem.Value);

            integerField.RegisterValueChangedCallback(evt =>
            {
                // Salva sempre come Int64 per coerenza (JSON numeri interi)
                editableItem.Value = (long)evt.newValue;
            });

            return configItem;
        }

        private VisualElement CreateCheckBoxItem(EditableConfigItem editableItem)
        {
            VisualElement configItem = configurationItemCheckBox.Instantiate();
            Toggle toggle = configItem.Q<Toggle>();

            if (toggle == null)
                return configItem;

            toggle.label = editableItem.Key;
            toggle.value = editableItem.Value is bool b && b;

            toggle.RegisterValueChangedCallback(evt =>
            {
                editableItem.Value = evt.newValue;
            });

            return configItem;
        }
    }

}
