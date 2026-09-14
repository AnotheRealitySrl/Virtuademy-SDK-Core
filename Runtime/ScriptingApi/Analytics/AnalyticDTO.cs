using UnityEngine;

namespace Virtuademy.ScriptingApi
{
    public class AnalyticDTO
    {
        [SerializeField] private EAnalyticVerb verb;
        [SerializeField] private int sessionId;

        [SerializeField][SettableField] public object customAttributes;

        [SerializeField] private XAPIStatement statement;

        [SerializeField] private string locale;


        public EAnalyticVerb Verb { get => verb; set => verb = value; }
        public int SessionId { get => sessionId; set => sessionId = value; }
        public string Locale { get => locale; set => locale = value; }
        public string Context { get; set; }
        public XAPIStatement Statement { get => statement; set => statement = value; }
    }
}
