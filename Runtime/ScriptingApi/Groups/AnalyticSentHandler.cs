namespace Virtuademy.ScriptingApi
{
    /// <summary>The answer to an analytic sent with <see cref="IAnalyticsApi.Send(EAnalyticVerb, AnalyticDTO, AnalyticSentHandler)"/>:
    /// whether the platform stored it, and the error when it did not.</summary>
    public delegate void AnalyticSentHandler(bool success, string error);
}
