# Virtuademy-SDK-Core

The creator-facing scripting surface and the platform's transport, in one package. It is where two
graphs meet: a creator gets it underneath `Virtuademy-SDK-Environments` and names only
`Virtuademy.ScriptingApi`; an application — the platform's own or an external one — takes it for the
HTTP stack every API client is built on, and for the Configuration API client that tells those
clients where to point.

## What is here today

Five assemblies.

| Assembly | Holds | First-party references |
|---|---|---|
| `Virtuademy.ScriptingApi` | the surface an interpreted script may name | **none** |
| `Virtuademy.SDK.Core.Client` | HTTP transport, credentials, endpoint resolution | `SPACS.Utility` |
| `Virtuademy.SDK.TenantConfiguration.Wire` | the Configuration API's wire types | **none** |
| `Virtuademy.SDK.TenantConfiguration` | the Configuration API client | `SPACS.Utility`, `…Core.Client`, `….Wire` |
| `Virtuademy.SDK.Core.Editor` | tenant switch, editor auth, build bases, Android launch scheme | `SPACS.Utility`, `…Core.Client`, `…TenantConfiguration(.Wire)` |

**No assembly here references the application framework** (`Virtuademy-SystemCore`). The package's
only first-party dependency is `SPACS-Utility`, which is what `package.json` declares.

### `Virtuademy.ScriptingApi` — what an interpreted script may name

`IVirtuademyFramework` is the entry point, and it declares **what only the platform knows**: which
session this is, what the local player has saved, what this run reports about the learner. Three
groups hang off it — `ISessionApi`, `ISaveDataApi`, `IAnalyticsApi` — plus the types they hand back:

- **The five client-model views** — `UserView`, `SessionView`, `ExperienceView`, `EnvironmentView`,
  `TagView`. These are the *trimmed half* of the client model, not a copy of it: the application's
  `CMUser`, `CMSession`, `CMExperience`, `CMEnvironment` and `CMTag` derive from them and keep the
  rest. `UserView` carries the avatar's URL as a flat string and no preference object, so an
  authored world does not receive the user's nickname, bio, height, date of birth, city or social
  links.
- **The seventeen analytics types** — `AnalyticDTO`, the three xAPI types, the six experience
  records, the four enums and `SettableFieldAttribute`. They are here because the shipped Visual
  Scripting nodes build xAPI statements out of them.

Two properties of this assembly are load-bearing rather than incidental:

- **It declares no first-party reference at all**, which is the condition for the server-side
  whitelist to admit it.
- **`IVirtuademyFramework.Install` is `internal`**, opened by `[InternalsVisibleTo]` to exactly one
  assembly, `Virtuademy.Worlds.ScriptingApiBackend`. That assembly is the application's
  (`Assets/_Project/ScriptingApi/`), not this package's — a creator installs the surface, not what
  answers it. A script references this assembly *in full*, so a public installer would let one
  script replace the surface every other script is calling.

The shape is written to the interpreter's budget, which is what lets a script name it: no `Task`
(`System.Threading` is denied), no generic member, no `Nullable<T>`, and anything asynchronous takes
an `Action` or returns an `IEnumerator`. That is also why the groups hand back views instead of the
application's client models, and why `ISessionApi` has both `HasShard` and `IsShardOpen` where a
`bool?` would have done.

`IVirtuademyFramework.IsAvailable` is false in an authoring project, where these contracts compile
and nothing answers them.

**The sibling surface is not here.** Everything the Virtuademy player merely *provides* — the
avatar, the world, the screen, the help panel, the language, the device — is on
`IVirtuademyGameplay`, in `Virtuademy-SDK-Environments`. The line between the two is server-held
state, not audience. The application installs both, in one place.

### `Virtuademy.SDK.Core.Client` — the transport

`ApiClientBase` is the base every API client in the platform derives from, `ApiHelper` builds the
requests and `HttpHelper` sends them; `ApiResponse`, `ApiResponse<T>`, `ApiResponseArray<T>`,
`ApiResponseSearch<T>` and `ApiResponseError` are the envelope shapes it unwraps. Addressing and
identity sit beside them: `IApiEndpointResolver` / `ApiEndpointResolver`, `AppIdentification`,
`PlatformConfig`, `LaunchScheme`, `ApiInfo`. Authentication is `ITokenProvider`, `EAuthentication`,
`JwtToken` and `HmacCredential`. Namespaces are `Virtuademy.SDK.Core.ApiSystem`, `.Authentication`,
`.Utilities` and `Virtuademy.SDK.Http`.

**Two generated `ScriptableObject` assets carry the configuration**, and they are deliberately
separate (see meta-repo ADR 0025):

- `PlatformEndpoints` — the endpoint table. **Committed.**
- `PlatformCredentials` — the HMAC credential a build signs its pre-login calls with. **A secret;
  it must not be committed.** Neither has a `[CreateAssetMenu]`: one made by hand would be empty and
  would shadow the generated one.

With one asset holding both, a git history could not tell an environment moving hostname from a
credential being rotated, and ignoring the file to protect the secret would hide the half worth
reviewing.

**On TLS**: `AcceptAllCertificates` exists and is reached only when `allowUntrustedServers` is set.
The default is `default` — UnityTls, validating against the device trust store. **Never enable it in
production**; the on-prem chain problem it papers over is fixed server-side (meta-repo ADR 0015).

### `Virtuademy.SDK.TenantConfiguration` and `.Wire`

The wire half declares `Tenant` (with `Env` and `TenantStatus`), `TenantConfig`,
`TenantPublicConfig`, `ApiEndpoint` and `AzureB2CConfig`, and references nothing — so a caller can
name a tenant DTO without taking the client.

`TenantConfigurationClient` is the client for the Configuration API: the tenant, its app config, the
public projection, and the endpoint table every other API client resolves its address from. It is a
plain `ApiClientBase` and `IApiEndpointResolver`, **not** a framework system — the framework half
lives in the application, as a thin system that owns one of these and forwards to it. It carries no
serialized configuration of its own: the address comes from the `Configuration` entry of
`PlatformEndpoints` and the credential from `PlatformCredentials`. An application installs one
statically (`IsInstalled`); editor tooling with no application around it constructs its own.

### `Virtuademy.SDK.Core.Editor`

The tenant-switch tooling, and the package's only menu item, `Virtuademy/Available Tenants` →
`TenantSelectionWindow`. Beside it: `AppConfigurationWindow` + `AppConfigurationSettings`,
`TenantSwitch.Apply` (read the tenant the selected app belongs to, then write the two generated
assets) and `PlatformConfigWriter`. `TenantSwitch` is what a project *without* an
application-specific configurator needs — which is every external application, for which the window
used to offer a tenant, accept a selection, and write nothing. An application with its own
configurator calls into it rather than repeating the flow.

`AppConfigurationImporter.Import` connects a project to a tenant the way the docs describe it by hand: the
file downloaded from the Back Office goes into the project (`Assets/Editor/TenantConfiguration`, next to the
settings asset), into the App Assets of the settings asset, and is selected. The file holds the app's secret, so
the import refuses (`SecretNotIgnoredException`) when git does not ignore that folder, unless the caller passes
`allowTracked`. The window's **Import configuration file...** button and the command line
(`EnvironmentsCli tenant-config`, in `Virtuademy-SDK-Environments`) both call it. The file holds the app's
secret: nothing logs its content.

Three `ScriptableObject` bases an application extends: `BuildScriptBase`, `AbstractAppConfigurator`,
`AbstractPlatformSettings`.

Editor authentication runs on MSAL — `AzureAuthService`, `EditorSessionManager`, `EditorLoginState`,
`EditorApiEndpoint`, `JwtBearerInspector`, against the three precompiled DLLs under
`Editor/Microsoft.Identity/`. In batch mode a session is renewed silently or not at all:
`EditorSessionManager.InteractiveLoginAllowed` is false, so no call opens the browser. `AndroidLaunchSchemeInjector` is an
`IPostGenerateGradleAndroidProject` that writes the launch scheme into the generated Gradle project.

## What is deliberately absent

- **The platform contracts and the wire DTOs.** `IPlatformContext`, `IPlatformAuthentication`, the
  five value types and the wire DTOs left on 2026-09-14 for the package that holds the platform's
  REST client, where their implementation and their audience already were. The assemblies
  `Virtuademy.SDK.Core` and `Virtuademy.SDK.ApiData.Wire` are retired. **This is not the surface an
  external application uses** — that was the framing when the contracts lived here, and the code
  never supported it, because `Install` has always been internal. An external application reaches
  the platform through those clients, where `Task` is unconstrained because the whitelist binds
  creator code only. What this package genuinely shares with that path is its *types*.
- **Any first-party reference from `Virtuademy.ScriptingApi`.** Adding one is not a refactor; it
  takes the assembly out of the whitelist.
- **The application framework.** `SM` and the system framework are in `Virtuademy-SystemCore`, which
  nothing here names — which is what makes the interpreted-script perimeter structural rather than a
  matter of asmdef discipline.

## Known issues / TODO

- **`TenantConfigurationClient`'s doc comment reads backwards.** It says the base class "was the
  last thing tying this package to `Virtuademy-SDK-Core`" — written when that name meant the
  framework package, which took `Virtuademy-SystemCore` in the 2026-09-11 name swap. As written it
  now says the package is tied to itself. Same for the `Virtuademy-SDK-Core` mentions elsewhere in
  the runtime comments.
- **The editor assembly's namespaces do not match its name.** `Virtuademy.SDK.Core.Editor` declares
  `rootNamespace: Virtuademy.SDK.TenantConfiguration.Editor`, and every type in it sits there —
  including the ones that are not tenant configuration (the Azure auth helpers, the Android launch
  scheme injector). Left as is because renaming a namespace touches every consumer; worth doing on a
  dedicated pass.
- **`Virtuademy.SDK.Core.Client` declares no `rootNamespace`** and spreads over four namespaces
  (`…Core.ApiSystem`, `…Core.Authentication`, `…Core.Utilities`, `…SDK.Http`). `ApiResponse` and the
  HTTP helpers sitting under `Virtuademy.SDK.Http` rather than under `Virtuademy.SDK.Core` is the
  visible edge of that.
- **The browser page at the end of the editor login is MSAL's default**: one line of unstyled text
  ("Authentication complete. You can return to the application. Feel free to close this browser
  tab."), served by MSAL's listener on the loopback redirect URI. Decided on 2026-10-16, not done
  yet: two hosted pages, a success page and an error page, reached by redirect.
  - Where they live is not decided yet. Most likely with the Virtuademy B2C pages: the `b2cflows`
    blob container, folder `Virtuademy/`, mirrored in `Virtuademy-Website-Landing/b2c_templates/`.
    Hosted there, the browser's address bar shows the storage account's host
    (`spacsglobal.blob.core.windows.net`), which the B2C pages never show: B2C fetches them server
    side and serves them under `b2clogin.com`.
  - Success: `.WithSystemWebViewOptions(new SystemWebViewOptions { BrowserRedirectSuccess = … })` on
    the `AcquireTokenInteractive` call in `AzureAuthService.LoginInteractive`, pointing at the
    success page. MSAL answers the browser with a `302` to that URL.
  - Error: MSAL's `BrowserRedirectError` is a fixed URL too and carries nothing of the error (checked
    against the listener of MSAL 4.61.0, the version this package ships: the `Location` header is
    the configured URL as it is). The browser's address already holds both values, though
    (`http://localhost:<port>/?error=…&error_description=…&state=…`). So `HtmlMessageError` is a few
    lines with no styling: its script copies `error` and `error_description` (never `state`) into
    the query of the error page and calls `location.replace`; without JavaScript, a link to the same
    page. MSAL passes `HtmlMessageError` through `string.Format` (`{0}` the code, `{1}` the
    description), so any brace in it must be doubled. The response declares no charset: keep it
    ASCII.
  - The pages follow the Virtuademy B2C pages (`b2c_templates/Virtuademy/`): the light background, a
    white card with 30px corners, Poppins, the full mark. English or Italian from
    `navigator.languages`, as B2C picks the sign-in page's language (the editor sends no
    `ui_locales`). The error page shows the two values as text only (`textContent`, a length cap,
    the code checked against `^[a-z0-9_]+$`): anyone can build a link to it with text of their
    choosing.
  - Costs accepted: the two URLs become a contract every SDK version in the field calls, so they must
    never move; the pages must be online before the SDK version that uses them ships; when the host
    does not answer, the user sees a browser error after a sign-in that succeeded; the error details
    land in the host's access logs (a B2C description carries an error code, a correlation id and a
    timestamp).
  - Rejected: embedding the pages in this package (built on 2026-10-16 and not committed: a C# file
    holding the HTML, the CSS and the mark, too heavy for what it does).
  - The Configuration Portal's login sidecar (`src-auth/AzureAuthService.cs`) shows the same default
    page; a query parameter naming the app would let it use the same hosted pages.
