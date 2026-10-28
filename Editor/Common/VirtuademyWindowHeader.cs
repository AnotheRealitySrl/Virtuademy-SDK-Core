using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    /// <summary>
    /// The Virtuademy header of the SDK's editor windows: the logo, with light lettering on the dark skin and dark
    /// lettering on the light one, and the window's name under it.
    /// </summary>
    /// <remarks>
    /// The setup package (Virtuademy-SDK-Environments-Setup) draws the same header from its own copy of the two images
    /// and of <c>VirtuademyWindowHeader.uss</c>, because it runs before the SDK is installed and cannot reference it.
    /// Change both together.
    /// </remarks>
    public static class VirtuademyWindowHeader
    {
        private const string folder = "Packages/com.anotherealitysrl.virtuademy-sdk-core/Editor/Common";

        /// <summary>The header with <paramref name="title"/> under the logo; without the image it shows the title alone.</summary>
        public static VisualElement Create(string title)
        {
            VisualElement header = new() { name = "window-header" };
            StyleSheet styles = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{folder}/VirtuademyWindowHeader.uss");
            if (styles != null)
                header.styleSheets.Add(styles);
            header.AddToClassList("vd-window-header");

            VisualElement logo = new() { name = "header-logo" };
            logo.AddToClassList("vd-window-header__logo");
            Texture2D image = AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{folder}/Icons/virtuademy-logo-{(EditorGUIUtility.isProSkin ? "light" : "dark")}.png");
            if (image != null)
                logo.style.backgroundImage = image;
            else
                logo.style.display = DisplayStyle.None;
            header.Add(logo);

            Label name = new(title) { name = "header-subtitle" };
            name.AddToClassList("vd-window-header__title");
            header.Add(name);
            return header;
        }
    }
}
