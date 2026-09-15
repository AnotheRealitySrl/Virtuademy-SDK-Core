using UnityEngine;

namespace Virtuademy.SDK.TenantConfiguration.Editor
{
    public abstract class AbstractPlatformSettings : ScriptableObject
    {
        public abstract void Configure(object settings = null);
    }
}
