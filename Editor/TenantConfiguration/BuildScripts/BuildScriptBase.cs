using System.Linq;

using UnityEditor;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    [CreateAssetMenu(fileName = "BuildScriptBase", menuName = "Virtuademy/SDK-TenantConfiguration/BuildScriptBase")]
    public class BuildScriptBase : ScriptableObject
    {
        public virtual void Build(params object[] buildParams)
        {
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray();
            BuildPlayerOptions buildPlayerOptions = new()
            {
                scenes = scenes,
                target = EditorUserBuildSettings.activeBuildTarget,
                locationPathName = "Build",
            };
            BuildPipeline.BuildPlayer(buildPlayerOptions);
        }
    }
}
