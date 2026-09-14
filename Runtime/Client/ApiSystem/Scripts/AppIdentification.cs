using Virtuademy.SDK.Core.Utilities;

using System;

using Unity.Properties;

using UnityEngine;

using SPACS.Utilities;

namespace Virtuademy.SDK.Core.ApiSystem
{
    [Serializable, Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.Fields)]
    public class AppIdentification
    {
        [SerializeField] private HmacCredential credential;
        [SerializeField] private string apiBaseUrl;
        [SerializeField] private string apiVersion;

        [CreateProperty] public HmacCredential Credential => credential;
        [CreateProperty] public string ApiBaseUrl => apiBaseUrl;
        [CreateProperty] public string ApiVersion => apiVersion;

        public AppIdentification(HmacCredential credential, string apiBaseUrl, string apiVersion = null)
        {
            this.credential = credential;
            this.apiBaseUrl = apiBaseUrl;
            this.apiVersion = apiVersion;
        }

        public AppIdentification()
        {
            // Default constructor for serialization
        }
    }

}

