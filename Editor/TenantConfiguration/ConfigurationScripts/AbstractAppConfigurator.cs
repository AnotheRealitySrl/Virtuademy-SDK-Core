using System.Threading.Tasks;

using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    public abstract class AbstractAppConfigurator : ScriptableObject
    {
        public abstract Task ConfigureApp(AppConfigurationSettings settings);
    }
}

