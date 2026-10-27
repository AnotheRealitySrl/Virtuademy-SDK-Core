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

`AndroidLaunchSchemeInjector` is an `IPostGenerateGradleAndroidProject` that writes the launch scheme into the
generated Gradle project.

#### Editor login: connections in a per-user store

Editor authentication runs on MSAL, against the three precompiled DLLs under `Editor/Microsoft.Identity/`, and a
login is a **connection** (meta-repo ADR 0037): one tenant of one Configuration-API instance, with its Azure
account and its platform token. Its key is the instance's normalized base URL plus the `tenantKey` that
`GET /manage/apps/tenant/config-public` returns (`TenantPublicConfig.TenantKey`): the tenant key alone is not unique,
because every Configuration-API instance has its own Platform DB. A credential whose `config-public` gives no
`tenantKey` (a platform-admin app) cannot become a connection.

The connections live in one store per OS user, **outside every project**, shared by every project and Unity process of
that user: `%USERPROFILE%\.virtuademy\sdk\`. Not `%LOCALAPPDATA%`: a packaged (MSIX) app can redirect that folder,
`%APPDATA%` and `HKCU` for the processes it starts, so a command line started from such an app (the Claude desktop app,
for one) would keep a store of its own beside the editor's. The profile folder is not redirected.

| File | Holds |
|---|---|
| `connections.bin` | every connection: the app credential imported from the Back Office file, the session, the MSAL account it is pinned to. Encrypted with DPAPI for the current user (a P/Invoke to `crypt32`, `Dpapi`: `ProtectedData` is not referenced here). A revision on each record. |
| `msal.bin` | the one MSAL token cache for every account, through MSAL's own hooks (`EncryptedMsalCache`), encrypted the same way. |
| `status.json` | the same connections **without any secret**: label, environment, user, whether the token is valid, when it expires. A script can read it without starting Unity. |
| `*.lock` | lock files (`FileShare.None`, released by the OS when the process dies) that order writers across processes. |

A writer takes the lock, reads the file again, changes **only its own connection** and replaces the file atomically; one
process renews a connection and the others read what it stored. Nothing is kept in SessionState or EditorPrefs, and an
asset-import worker process never reads or writes the store. **Where nothing can be encrypted (every OS but Windows, for
now) the connections stay in the editor process**, carried over a domain reload through SessionState (process memory) and gone
when the editor closes; nothing is written to disk. A batch `login` is refused there, since it would end with the process.

`ConnectionLoginService` is the login with no UI: `LoginAsync` reads the tenant, the public configuration and the auth
config over HMAC, runs the Azure login and exchanges the token, and ends with the connection in the store. The window,
the command line and the request pump all call it. Silent renewal is pinned to the connection's account, never to the
first account of the cache, and the browser is given that account as a hint (an answer for another one is refused). An interactive login uses the system browser and a loopback port MSAL picks; it **prints
the authorization URL as well as opening it** (`InteractiveLoginOptions.OnAuthUrl`), allows MSAL a minute to issue it
and the user three minutes **from the moment the URL is issued** (`LoginTimeoutException` says which phase ran out).
Logout drops the connection's token and removes its MSAL account only when no other connection uses the same authority and
account.

A project says which connections it uses in `UserSettings/Virtuademy/connections.json` (`ProjectConnections`: no token
and no secret, keys only). Its **default** connection is chosen in Virtuademy > Available Tenants (a selection or a login
there makes it the default one): an editor request or a command that names no connection falls back to it, and "Configure
App" uses it. It does not decide play mode: the tenant switch writes `PlatformCredentials.asset` from the selected file. `EditorLoginState` is the view of it. It does not decide where a deploy goes: the Addressables window and the
command line name their connections. `EditorApiEndpoint` takes the Application API address from the tenant a connection
reported at login, never from the project's `PlatformEndpoints` asset. `EditorSessionManager` renews and sends on any
connection by key (`SendAuthorizedAsync(connectionKey, ...)`); without a key it means the default one.
**An operation takes its connection once, at the start, and passes the key down** (the deploy, the published-environments
index, the script verification all do): the default connection can change while it runs, and the token of a connection is
sent only to the hosts of its own tenant (anything else is refused before it is sent). Logins from the command line and
request files make the connection the default one only when the project has none. In
batch mode a session is renewed silently or not at all (`EditorSessionManager.InteractiveLoginAllowed` is false): the `login`
command is how a browser is asked for.

The first use of the store in a process runs the one-time migration of the old per-machine login (`LegacyLoginMigration`):
it deletes `Virtuademy_EditorLogin_Token` from EditorPrefs, where the token sat in clear text, and nothing else; the other nine
`Virtuademy_EditorLogin_*` keys stay for an older SDK on the same machine, and nothing reads them. The credentials of the
project's own configuration files go into the store the first time the Available Tenants window opens in an editor
session (`ImportProjectCredentialsAsync`, which asks the platform once per credential), or at the first `login -vdApp`.
Each connection then needs one login.

**From the command line and a script.** `EnvironmentsCli` (in `Virtuademy-SDK-Environments`) has `login`, `logout`,
`connections` and `whoami`, which write a JSON result to the file `-vdResultFile` names. They run in batch mode only while
the project is closed, because Unity refuses a second instance of a project (`Temp/UnityLockfile` tells the two cases apart).
While the editor has the project open, a script drops a request file in `UserSettings/Virtuademy/requests/` and the editor
answers it (`EditorRequestPump`, polling from `EditorApplication.update` every 250 ms, also when the
window is unfocused):

- Write `<id>.json.tmp` and rename it to `<id>.json`: `{ "command": "login" | "logout" | "status", "connection": "...", "app":
  "...", "env": "...", "noBrowser": false, "loginTimeoutSeconds": 180, "resultFile": "path" }`. `connection` is a key, a tenant
  key, a tenant label or `app/env`; `app` and `env` name a credential of the project's configuration files that is not in the
  store yet. Only those three commands are accepted, never a build or a deploy.
- The answer is `<id>.result.json` (or `resultFile`), with `state: done` and `status` (`done`, `login-required`,
  `connection-unknown`, `login-timeout`, `login-failed`, `bad-request`). A login writes it first with
  `state: awaiting-browser` and the `authUrl`. The request file is renamed `.working` when it is picked up and `.done` when handled.
- No answer, and no output of any of these commands, contains a token or a secret.

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

- **The connection store has no macOS or Linux implementation.** The record file and the MSAL cache are encrypted
  with DPAPI, so on Windows only. Elsewhere the connections live in the editor process (SessionState) and a warning says so;
  the memory mode was tested on Windows by forcing it.
  The macOS one (an AES-CBC + HMAC-SHA256 file whose key is in the Keychain) is not written; its lock file
  (`FileShare.None`) is not tested on Mono for macOS either. Tested so far on Windows, Unity 6000.3.21f1, with a fake platform:
  no real B2C login has been run through it.
- **Deploy targets are not per connection yet.** The Addressables window deploys the worlds it listed for the project's
  default connection (it refuses when that changed since), and its ticks are kept per project and connection (the old
  machine-wide value is no longer read). `UserSettings/Virtuademy/connections.json` holds the connections used and the
  default one. The tenant switch still writes `PlatformCredentials.asset` from the window's selected file, not from the
  default connection's record (the two are the same tenant once the file is a connection), and the check that git ignores
  `UserSettings/` and `PlatformCredentials.asset` is not made.

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
  - Success: `BrowserRedirectSuccess = …` on the `SystemWebViewOptions` of the `AcquireTokenInteractive` call in
    `AzureAuthService.LoginInteractive` (which already sets `OpenBrowserAsync` there), pointing at the
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
