# Release notes

## Unreleased

### Added
- **`AppConfigurationImporter.Import`: connect the project to a tenant without clicking** (minor bump). What
  "Connect the SDK to your tenant" does by hand: the file downloaded from the Back Office (**Download SDK
  Environments Config**) goes into the project (`Assets/Editor/TenantConfiguration`, the folder of the settings
  asset, unless it already is under `Assets/`), into the **App Assets** of the `AppConfigurationSettings` asset,
  and is selected (its app, from the file name, and the file's first environment, or the one asked for). The file
  is validated (an environment with an app id, a secret and an API base URL) and nothing of its content is logged
  or quoted in an error. **It holds the app's secret, so the importer refuses to put it where git would track
  it:** when the project is inside a git work tree it asks git (`git check-ignore`) about the destination (and the
  settings asset, which keeps a copy of the credential, and the source when it already is under `Assets/`) and,
  when one is not ignored, throws `AppConfigurationImporter.SecretNotIgnoredException` before writing anything,
  unless the caller passes `allowTracked: true`. Not a git work tree, or git missing: it proceeds with a warning.
  A creator project already ignores `Assets/Editor/TenantConfiguration/` (the team's template does). The Available
  Tenants window has an **Import configuration file...** button that calls it; on a refusal it shows a dialog
  that names the risk, and only its third button, **Import anyway**, repeats the import with the override (Enter,
  Escape and a batch run all end in "not imported"). `EnvironmentsCli tenant-config`
  (`Virtuademy-SDK-Environments`) calls it too. Idempotent.
- `AppConfigurationSettings.FindOrCreate()`: the project's settings asset, created at
  `Assets/Editor/TenantConfiguration` when there is none. The window opened with the same logic, which is now
  this method.
- `EditorSessionManager.InteractiveLoginAllowed`.

### Changed
- **`EditorSessionManager` never starts an interactive login in batch mode** (behavior change, with the minor
  bump above). `EnsureValidTokenAsync` and everything that goes through it (`SendAuthorizedAsync`) read their
  `allowInteractive` argument through `InteractiveLoginAllowed`, which is false when the editor runs in batch
  mode: a deploy, a script verification or a policy fetch with an expired session used to open the system
  browser on a machine where nobody could answer it, and wait for ever. They now fail as they do when the
  login is not allowed, and the caller says to log in.

## v0.3.0

### Added
- **The client-model views.** `UserView`, `SessionView`, `ExperienceView`, `EnvironmentView` and
  `TagView` join `Virtuademy.ScriptingApi`: what an authored world may see of a user, a session,
  an experience, an environment and a tag. Five types, eighteen values.

  The surface is not a judgement call — it is what the three shipped `Expose: CM*` nodes already
  publish, read off their ports. The application's own `CMUser`, `CMSession`, `CMExperience`,
  `CMEnvironment` and `CMTag` derive from these and keep the rest: preferences, participants,
  permissions, ownership, the loaded thumbnail, the localisation sources.

  Two decisions inside that surface are worth recording. `UserView` carries a flat
  `ProfileImageUrl` rather than the preference object the node reads it through, because that
  object also holds the user's nickname, bio, height, date of birth, city, social links and hand
  preference — personal data, and the point of drawing this line is to decide such things on
  purpose. And there is no `CatalogView`: the addressable catalogue steers the platform's own
  content delivery, its only two uses are in the application, and an authored world has no
  business naming it. The `Catalog` port left `Expose: CMEnvironment` with it — the one port this
  split removes, so a graph that read it has to be re-authored.
- **`Virtuademy.ScriptingApi`**, the assembly a creator's interpreted script may name. It opens
  with the seventeen analytics types — `AnalyticDTO`, the three xAPI types, the six experience
  records and the four enums — which had to stay reachable because the shipped Visual Scripting
  nodes build xAPI statements out of them. It declares no first-party reference, which is the
  condition for the server-side whitelist to admit it.
- **`IVirtuademyFramework`, the entry point to what only the platform knows.** An interface with
  static members, so the surface and the way to reach it are one declaration: `IsAvailable`,
  `Current` (throws `InvalidOperationException` when nothing has installed an implementation,
  rather than returning null) and the `Installed` event for code that initialised first. Three
  groups hang off it:
  - `ISessionApi`: the session and local-user scalars, the `LocalUser`, `Details` and
    `Environment` views, `SetShardOpen`, `GetUser`, `FindExperience`, `JoinExperience`, and the
    `OtherPlayerEntered` / `OtherPlayerLeft` events, flattened to a user id and a session id.
  - `ISaveDataApi`: `Get`, `Set`, `Delete` and `SubmitLeaderboardRecord`.
  - `IAnalyticsApi`: `Send` and `GenerateExperienceGuid`.

  The line between this surface and `IVirtuademyGameplay` in `Virtuademy-SDK-Environments` is
  server-held state, not audience: which session this is, what the local player has saved and
  what this run reports about the learner exist nowhere but the platform. `Install` is
  `internal` and `[InternalsVisibleTo]` opens it to one assembly,
  `Virtuademy.Worlds.ScriptingApiBackend`, the application's own — a script references this
  assembly in full, so a public installer would let one script replace the surface every other
  script calls.

  The shape is written to the interpreter's budget: no `Task` (`System.Threading` is denied), no
  generic member, no `Nullable<T>`. Anything asynchronous returns an `IEnumerator` a script waits
  on with `yield return`, or takes an `Action` that receives the result. An implementation must
  keep the public method itself out of an iterator body: that body would not run until the first
  `MoveNext`, so a caller who ignores the return value would never see the work start.
- **The editor tenant switch moves here, so an app developer can reach it.** It was in the
  authoring package's editor assembly, which an external app does not install. New assembly
  `Virtuademy.SDK.Core.Editor` holds `TenantSelectionWindow` (menu `Virtuademy/Available Tenants`),
  `AppConfigurationWindow`, `AppConfigurationSettings`, `PlatformConfigWriter`, the three bases an
  application extends (`BuildScriptBase`, `AbstractAppConfigurator`, `AbstractPlatformSettings`)
  and the editor-login helpers the window needs (`AzureAuthService`, `EditorSessionManager`,
  `EditorLoginState`, `EditorApiEndpoint`, `JwtBearerInspector`). The three MSAL libraries the
  sign-in needs are bundled under `Editor/Microsoft.Identity/` rather than declared through
  NuGetForUnity, which a package fetched by git URL cannot ask for.
- **`TenantSwitch`: the tenant switch works in a project with no application of its own.**
  `TenantSwitch.Apply` reads the tenant and writes the two generated assets, `PlatformEndpoints`
  and `PlatformCredentials`. The window's change-config button is always shown now and runs it
  when the project has no `AbstractAppConfigurator`; with one, the configurator runs and calls
  into it. Before, such a project could pick a tenant and got nothing written.

  The `Configuration` endpoint comes from the app config (`ApiBaseUrl`, `ApiVersion`), not from
  discovery, and is written whether or not discovery answered: asking the platform where the
  Configuration API lives needs to know already. A loopback address in the app config wins over
  what discovery reports, so a switch driven from a local API does not record the deployed
  hostname and undo itself on the next run. The endpoint asset is merged, never replaced, so a
  partial answer does not drop addresses it already held.
- **`LaunchScheme` and `AndroidLaunchSchemeInjector`: the URI scheme the platform launches an
  external app with is derived, not typed twice.** `LaunchScheme.Derive` returns `v` plus the whole
  app id (`va1728f51-a525-44b9-a737-b482452f587b`), `UriFor` returns it as the `scheme://` prefix
  the launcher opens, and `IsValid` checks it; it is runtime code, so the platform application and
  the build compute the same value. The scheme comes from the credential in the app config and
  there is no override, on purpose: an override is a way to make the two halves disagree, and the
  failure is silent. On an Android build the injector adds a `VIEW` intent filter for it to the
  launcher activity of the generated Gradle project; it adds nothing when there is no scheme or
  the scheme is already claimed. The tenant switch logs the value.
- `ApiClientBase.TokenSource`, a `Func<ITokenProvider>` resolved inside the call. Set it instead of
  `Tokens` when the provider does not outlive the client: a framework system is re-created across a
  scene load and may not exist yet when its owner initialises. `Tokens` still wins when both are
  set.
- `TenantConfigurationClient` and the tenant wire DTOs: the Configuration API client, extracted from
  the application system that used to be it, so nothing in the creator graph needs a framework to
  read a tenant configuration.
- `ApiClientBase.AdoptConnection`, so a second client can reuse a connection another one resolved
  rather than resolving its own.
- The transport assembly `Virtuademy.SDK.Core.Client` (`…Interface.Client` before this rename).
- `TenantPublicConfig.PlatformVersion`: the platform version the tenant is pinned to (`2026.6.0`),
  from the Configuration-API's `config-public` field `platformVersion`; null when the tenant is not
  pinned or the API predates the field.

### Changed
- **The DTOs and the platform contracts left for `Virtuademy-SDK-Library`.** The assemblies
  `Virtuademy.SDK.ApiData.Wire` and `Virtuademy.SDK.Core` are retired: 48 wire types,
  `IPlatformContext`, `IPlatformAuthentication` and the five value types all belong to the HTTP
  client's side of the line, and keeping them here meant a creator installed the platform's wire
  format in order to author a world.
- **Renamed.** The package id becomes `com.anotherealitysrl.virtuademy-sdk-core` and the assembly
  `Virtuademy.SDK.Core`; the wire assembly becomes `Virtuademy.SDK.ApiData.Wire`. The name
  `Virtuademy.SDK.Core` was in use by the framework package, which took `Virtuademy.SystemCore` in
  the same wave — the two moves are one change and cannot be split, because for one commit the old
  and the new name would collide.

  The v0.2.0 note below argued the wire assembly should keep its name because a built bundle records
  components by assembly name, so a rename invalidates published worlds. **That argument is spent,
  not wrong:** no bundle built on these packages is published yet, and a republication is already
  planned. The rename was taken now, while it costs a republication that was going to happen anyway,
  rather than after it costs a migration. Creator projects carry across with the rename migrator in
  `Virtuademy-SDK-Environments`, which rewrites `$type` values in Visual Scripting graphs.
- The `displayName` is now `Virtuademy-SDK-Core`, hyphenated like every sibling package, and the
  `description` says what the package is now: the scripting surface and the transport, not the
  platform contracts.
- **The editor login speaks Virtuademy.** The `EditorPrefs` keys `Reflectis_EditorLogin_*` become
  `Virtuademy_EditorLogin_*` and the token cache moves to `Library/Virtuademy/Auth`, so everyone
  logs in once more after updating. The menu entry and window title `Show available tenants`
  become `Available Tenants` (`Virtuademy/Available Tenants`); `Virtuademy-SDK-Environments` opens
  the window by that path, so release the two packages together.
- The utilities the runtime imports are `SPACS.Utilities` now. `JwtToken` and `HmacCredential` stay
  in `Virtuademy.SDK.Core.Utilities`, which is why both imports sit side by side in some files.
- The README describes the package as it is: five assemblies, the two generated assets and which
  of them is a secret, and where the sibling gameplay surface lives.

### Breaking
- **The package id and the assemblies changed, and the DTOs and platform contracts are not here.**
  Anything that names `com.anotherealitysrl.virtuademy-sdk-core` by its previous id, or the
  assemblies `Virtuademy.SDK.Core` and `Virtuademy.SDK.ApiData.Wire`, must follow them: the wire
  DTOs, `IPlatformContext`, `IPlatformAuthentication` and the five value types are in
  `Virtuademy-SDK-Library` (assembly `Virtuademy.SDK.Library`). The rename migrator in
  `Virtuademy-SDK-Environments` rewrites creator projects.
- **`Expose: CMEnvironment` has no `Catalog` port.** A graph that read it must be re-authored;
  there is no view to land on.
- **The WebSocket stack is not in this package.** `WebSocketHandler`, `WebGLWebSocketHandler`,
  `WebSocketMessagesHandler`, `IWebSocketHandler`, `IWebSocketListener`, `BaseWebSocketListener`
  and the WebGL `webSocketClient.jslib` live in `Virtuademy-SDK-Library`, with the new
  `WebSocketClient` registry (address to handler, the wait on a connection that is still opening,
  scheme normalisation) beside them. The namespace is unchanged, so a project that used them needs
  an assembly reference to that package and no source edit. It matters to code built against an
  earlier `develop`, where they were part of this package's transport; a creator no longer
  receives a socket they never reach.
- **`ILocalizationApi`, `IScreenApi`, `IHelpApi` and `IPlatformApi` are not in `Virtuademy.ScriptingApi`.**
  They are in `Virtuademy.Environments.ScriptingApi` (`Virtuademy-SDK-Environments`) and are reached
  through `IVirtuademyGameplay`, not `IVirtuademyFramework`. They sat here for a few commits on
  `develop` and were never in a tagged release; code that was built against that state changes the
  namespace and the entry point.
- **`WorldOperation` does not exist; the thirteen members that returned it return `IEnumerator`.**
  Four of them are in this package (`GetUser`, `FindExperience`, `JoinExperience`,
  `GenerateExperienceGuid`). Wait with `yield return`, or pass a callback; a handle can no longer be
  held and polled. Like the previous item, this only concerns code built against `develop` between
  the two commits.
- **The tenant switch left the authoring package's editor assembly.** Its types are in
  `Virtuademy.SDK.Core.Editor`, which `Virtuademy.SDK.Environments.Editor` references; an assembly
  that named them needs that reference. The five editor-login helpers change namespace from
  `Virtuademy.SDK.Environments.Editor` to `Virtuademy.SDK.TenantConfiguration.Editor`.

### Fixed
- A token provider is no longer copied along with a connection: the two have different lifetimes,
  and copying the provider meant adopting a null one.
- **The editor finds the Application API token of a tenant provisioned under the current
  convention.** Those tenants label it `<TenantLabel>Application`, so the bare-label match found
  nothing; the bare label stays as a fallback for tenants provisioned before. The failure log lists
  the labels that did come back, which tells "no token was minted" from "minted under a label we
  do not match".
- **The tenant window's session label follows the token.** It turns yellow at the moment the token
  stops being usable (expiry minus `ExpiryMargin`) instead of staying green until the next
  login/logout, and shows the new expiry after a silent renewal. New
  `EditorLoginState.OnTokenChanged`, raised on renewal and on a server 401, re-renders the label
  only; `OnLoginStateChanged` stays out of token rotations. `EditorSessionManager.DescribeSession`
  is the one text for both status labels. No automatic logout: an expired token is renewed from the
  MSAL refresh token, and a logout would break a deploy in progress. The Addressables window in
  `Virtuademy-SDK-Environments` uses the new members, so release the two together.
- `package.json` declares `com.unity.modules.unitywebrequest`, which the HTTP client names: a project
  that installs the package alone no longer relies on the consumer having asked for the module.

## v0.2.0

### Changed
- **The wire DTOs live here now.** `Virtuademy.SDK.PlatformApi.Wire` — 65 files, zero references —
  moved from the `virtuademy-sdk-platformapi` package into this one, unchanged: same assembly name,
  same GUIDs, same bytes. The eight assemblies that reference it are untouched.

  The reason is that this package **names six of those DTOs** in `IPlatformContext`, so anyone who
  installs the contracts needs them by construction. They were in a package that also carries the
  platform's HTTP client — 923 lines, 64 endpoints — which needs `Virtuademy.SDK.Core`. So the two
  audiences the contracts exist for could not install them: an external app got 70 compile errors,
  and a creator got the platform client shipped into their project to be able to name a `UserDTO`.
  Measured by `Virtuademy-ExternalApp-Test`.

  The assembly keeps the name `Virtuademy.SDK.PlatformApi.Wire` even though it no longer sits in
  that package. An assembly name is a wire symbol — a built bundle records components by it — so
  renaming it would invalidate every published world for a tidier label. Left as a naming debt.
- `com.unity.nuget.newtonsoft-json` is now declared: 32 of the DTO files use it, and it travelled
  with them.

### Known
- The package now carries **two** assemblies. `Virtuademy.SDK.Interface` still declares no
  transport and no credentials; `noEngineReferences` stays `false`, because the DTOs carry
  `[SerializeField]` and that is what makes the contracts' closure engine-bound.

## v0.1.0

First release as a repository of its own. The package existed before this, embedded in
`Virtuademy-Unity` under `Packages/`, which meant the one package an external app developer needs
could not be installed by git URL — the whole point of it being public. The eight commits that
built it are preserved here with their original dates and messages.

### Added
- `IPlatformContext` — platform and session state as an application sees it: identity, session,
  experience, world, permissions, participants, shards, and the caller's own save data.
- `IPlatformAuthentication` — sign-in, as a sibling rather than a member, because `Initialize`
  presupposes an authenticated user.
- `WorldChooser`, and the five types this contract declares itself: `PlatformContextState`,
  `PlatformPermission`, `PlatformLaunchData`, `LoginChallenge`, `SessionShard`.
- `IApplicationManager` and `EApplicationState`, the two application-lifecycle types.

### Known
- **This assembly is not reference-free.** It references `Virtuademy.SDK.PlatformApi.Wire` for the
  six DTOs the contracts hand back, and `noEngineReferences` is `false` because those DTOs carry
  `[SerializeField]`. The README explains why naming them beats mirroring them; what the reference
  costs is the standalone, outside-Unity compile the package once had.
- **A mock cannot populate the contract.** Those DTOs expose private `[SerializeField]` fields and
  get-only properties with no public constructor, so `LocalUser`, `Session`, `Experience` and
  `World` are null in any test double. The types this contract owns are all constructible.
  `Virtuademy-ExternalApp-Test` exists to keep that measured.
