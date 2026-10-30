namespace Virtuademy.ScriptingApi
{
    public abstract class ExperienceAnalyticDTO : AnalyticDTO
    {
        [SettableField(isRequired = true)]
        protected string key;

        public string uniqueId;

        public string GetKey()
        {
            return key;
        }

        /// <summary>Sets the experience key (the key its experience id was generated for).</summary>
        public void SetKey(string value)
        {
            key = value;
        }
    }
}
