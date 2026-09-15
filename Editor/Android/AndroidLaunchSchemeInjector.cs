#if UNITY_ANDROID

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using UnityEditor.Android;

using UnityEngine;

using Virtuademy.SDK.TenantConfiguration.Editor;

namespace Virtuademy.SDK.TenantConfiguration.Editor.Android
{
    /// <summary>
    /// Claims the platform's launch scheme in the built manifest, so an application the platform
    /// launches actually opens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written into the <b>generated</b> Gradle project rather than into a committed
    /// <c>Assets/Plugins/Android/AndroidManifest.xml</c>, and that is the whole reason this is a
    /// build step and not something the tenant switch writes directly. A manifest under Assets
    /// replaces Unity's own, so authoring one from a script means reproducing whatever activity
    /// class this engine version uses and keeping up with it forever; get it wrong and the
    /// application stops starting at all. The generated manifest is already correct and already
    /// merged — there is only an intent filter to add.
    /// </para>
    /// <para>
    /// It adds nothing when the project has no scheme, and nothing when the scheme is already
    /// claimed, so a project that declares its own filter by hand keeps it and a second build
    /// does not accumulate copies.
    /// </para>
    /// </remarks>
    public class AndroidLaunchSchemeInjector : IPostGenerateGradleAndroidProject
    {
        private static readonly XNamespace android = "http://schemas.android.com/apk/res/android";

        /// <summary>
        /// After Unity's own manifest work, so there is a merged manifest with a launcher activity
        /// in it to add to.
        /// </summary>
        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            AppConfigurationSettings settings = AppConfigurationSettings.FindSelected();
            string scheme = settings?.LaunchScheme;

            if (!LaunchScheme.IsValid(scheme))
            {
                // Not a warning: a project that never ran a tenant switch, or one whose application
                // the platform does not launch, is an ordinary project and not a broken build.
                return;
            }

            foreach (string manifest in Manifests(path))
            {
                if (Claim(manifest, scheme))
                {
                    return;
                }
            }

            Debug.LogWarning($"[{nameof(AndroidLaunchSchemeInjector)}] Could not find a launcher "
                             + $"activity to claim {scheme}:// on. The platform will not be able to "
                             + "launch this application; the manifest needs the intent filter by hand.");
        }

        /// <summary>
        /// Where the launcher activity can be. Unity has moved it between the library module and
        /// the launcher module across versions and export modes, so both are tried rather than
        /// pinning the layout of a generated project.
        /// </summary>
        private static string[] Manifests(string path)
            => new[]
            {
                Path.Combine(path, "src", "main", "AndroidManifest.xml"),
                Path.Combine(Path.GetDirectoryName(path) ?? path, "launcher", "src", "main", "AndroidManifest.xml"),
            };

        /// <returns>True when the scheme is claimed in this manifest — added now, or already there.</returns>
        private static bool Claim(string manifestPath, string scheme)
        {
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            XDocument document;

            try
            {
                document = XDocument.Load(manifestPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{nameof(AndroidLaunchSchemeInjector)}] Could not read "
                                 + $"{manifestPath}: {e.Message}");
                return false;
            }

            XElement activity = LauncherActivity(document);

            if (activity == null)
            {
                return false;
            }

            if (AlreadyClaims(activity, scheme))
            {
                return true;
            }

            activity.Add(new XElement("intent-filter",
                new XElement("action", new XAttribute(android + "name", "android.intent.action.VIEW")),
                new XElement("category", new XAttribute(android + "name", "android.intent.category.DEFAULT")),
                // Without BROWSABLE the filter is unreachable from a link, which is how the web
                // launch path arrives. The intent one costs nothing and the other is required.
                new XElement("category", new XAttribute(android + "name", "android.intent.category.BROWSABLE")),
                new XElement("data", new XAttribute(android + "scheme", scheme))));

            document.Save(manifestPath);

            Debug.Log($"[{nameof(AndroidLaunchSchemeInjector)}] {scheme}:// claimed in "
                      + $"{manifestPath}.");

            return true;
        }

        private static XElement LauncherActivity(XDocument document)
            => document.Root
                ?.Element("application")
                ?.Elements("activity")
                .FirstOrDefault(a => a.Elements("intent-filter")
                    .SelectMany(f => f.Elements("category"))
                    .Any(c => (string)c.Attribute(android + "name") == "android.intent.category.LAUNCHER"));

        private static bool AlreadyClaims(XElement activity, string scheme)
            => activity.Elements("intent-filter")
                .SelectMany(f => f.Elements("data"))
                .Any(d => string.Equals((string)d.Attribute(android + "scheme"),
                                        scheme,
                                        StringComparison.OrdinalIgnoreCase));
    }
}

#endif
