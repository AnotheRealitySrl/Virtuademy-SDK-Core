using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace Virtuademy.SDK.Core.Utilities
{
    /// <summary>
    /// JWT token used to identify the user
    /// </summary>
    [Serializable, Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.Fields)]
    public class JwtToken
    {
        [SerializeField] private string bearer;
        [SerializeField] private DateTime expiry;
        [SerializeField] private string apiLabel;
        [SerializeField] private List<string> apiUrls;

        public string Bearer { get => bearer; set => bearer = value; }
        /// <summary>When the token expires, in UTC.</summary>
        /// <remarks>
        /// A value that arrives without an offset (<see cref="DateTimeKind.Unspecified"/>) is
        /// taken as UTC, which is what the platform sends. This used to convert it as local time,
        /// while <see cref="IsExpired()"/> compared it as UTC: on a device away from UTC the two
        /// disagreed by the device's offset.
        /// </remarks>
        public DateTime Expiry { get => AsUtc(expiry); }
        public string ApiLabel { get => apiLabel; set => apiLabel = value; }
        public IEnumerable<Uri> ApiUrls { get => apiUrls.Select(x => new Uri(x)); set => apiUrls = value.Select(x => x.ToString()).ToList(); }

        public bool IsExpired() => IsExpired(TimeSpan.Zero, TimeSpan.Zero);
        public bool IsExpired(TimeSpan timeOffset) => IsExpired(timeOffset, TimeSpan.Zero);

        /// <summary>
        /// Whether the token has expired, or will within <paramref name="leeway"/>.
        /// </summary>
        /// <param name="timeOffset">How far this device's clock sits from the server's.</param>
        /// <param name="leeway">
        /// How long before the expiry the token already counts as expired. A token checked with
        /// none can expire between the check and the server reading it, and the request then comes
        /// back 401.
        /// </param>
        public bool IsExpired(TimeSpan timeOffset, TimeSpan leeway) => DateTime.UtcNow - timeOffset + leeway > Expiry;

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}