using System;

namespace Virtuademy.ScriptingApi
{
    [Serializable]
    public class ExperienceStartDTO : ExperienceAnalyticDTO
    {
        [SettableField(isRequired = true)]
        public string context;

        public ExperienceStartDTO()
        {
        }

        public ExperienceStartDTO(string key, string context)
        {
            SetKey(key);
            this.context = context;
        }
    }
}
