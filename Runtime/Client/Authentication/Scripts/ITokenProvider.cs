using Virtuademy.SDK.Core.Utilities;

using System;
using System.Threading.Tasks;

using SPACS.Utilities;

namespace Virtuademy.SDK.Core.Authentication
{
    /// <summary>
    /// Supplies and refreshes the bearer tokens an API client signs its calls with.
    /// </summary>
    /// <remarks>
    /// Split out of <c>IAuthenticationSystem</c> so that the transport can ask for a
    /// token without depending on the thing that authenticates a user. The transport needs two
    /// operations; the authentication system has those plus session loading, sign-in events and
    /// a Unity lifecycle, none of which a client making an HTTP call has any business knowing
    /// about.
    /// <para>
    /// It is also what let this assembly stop referencing the one that holds the system
    /// framework: with tokens described by an interface that lives here, nothing in this
    /// assembly has to name <c>IAuthenticationSystem</c> — which is a system, and stayed with
    /// the framework. <c>IAuthenticationSystem</c> derives from this interface, so the one
    /// implementation already satisfies it and nothing had to be rewired.
    /// </para>
    /// <para>
    /// Any future instantiable client in this assembly takes one of these as a constructor
    /// argument. <c>ApiSystemBase</c> cannot — it is a <c>ScriptableObject</c>, so it exposes a
    /// settable property and resolves the framework's implementation into it.
    /// </para>
    /// </remarks>
    public interface ITokenProvider
    {
        /// <summary>
        /// The cached token for an API, identified by the label the API reports for itself in
        /// <c>GET /apiserver/info</c>. Throws when no token is held for that label.
        /// </summary>
        JwtToken FindToken(string apiLabel);

        /// <summary>
        /// Refreshes the whole token set. Called when a token is found expired, so the next
        /// <see cref="FindToken"/> returns a live one.
        /// </summary>
        Task GetTokens();
    }

    /// <summary>
    /// Implemented by a token provider that tells its clients when the tokens it handed out stop
    /// being the right ones.
    /// </summary>
    /// <remarks>
    /// A client keeps the token it found until that token expires, and only asks the provider
    /// again when it holds none. Without this signal, a sign-out followed by another user's
    /// sign-in left every client signing as the first user until that token expired. A separate
    /// interface rather than a member of <see cref="ITokenProvider"/>, so that existing
    /// implementations keep compiling; a provider that does not implement it behaves as before.
    /// </remarks>
    public interface ITokenProviderNotifications
    {
        /// <summary>
        /// Raised when the provider's token set was cleared (sign-out) or replaced (sign-in,
        /// refresh). A client drops its cached token and asks again on its next request.
        /// </summary>
        event Action TokensChanged;
    }
}
