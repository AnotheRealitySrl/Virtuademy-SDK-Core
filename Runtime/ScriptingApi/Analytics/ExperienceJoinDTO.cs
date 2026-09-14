using System;

namespace Virtuademy.ScriptingApi
{
    [Serializable]
    public class ExperienceJoinDTO : ExperienceAnalyticDTO
    {
        [SettableField(isRequired = true)]
        public string context;

    }
}
