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

The tenant-switch tooling, and the package's only menu item, `Virtuademy/Show available tenants` →
`TenantSelectionWindow`. Beside it: `AppConfigurationWindow` + `AppConfigurationSettings`,
`TenantSwitch.Apply` (read the tenant the selected app belongs to, then write the two generated
assets) and `PlatformConfigWriter`. `TenantSwitch` is what a project *without* an
application-specific configurator needs — which is every external application, for which the window
used to offer a tenant, accept a selection, and write nothing. An application with its own
configurator calls into it rather than repeating the flow.

Three `ScriptableObject` bases an application extends: `BuildScriptBase`, `AbstractAppConfigurator`,
`AbstractPlatformSettings`.

Editor authentication runs on MSAL — `AzureAuthService`, `EditorSessionManager`, `EditorLoginState`,
`EditorApiEndpoint`, `JwtBearerInspector`, against the three precompiled DLLs under
`Editor/Microsoft.Identity/`. `AndroidLaunchSchemeInjector` is an
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
