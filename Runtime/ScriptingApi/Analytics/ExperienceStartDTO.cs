using System;

namespace Virtuademy.ScriptingApi
{
    [Serializable]
    public class ExperienceStartDTO : ExperienceAnalyticDTO
    {
        [SettableField(isRequired = true)]
        public string context;
    }
}
