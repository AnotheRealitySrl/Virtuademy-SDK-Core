# Release notes

## Unreleased

Minor bump: additive API, plus behaviour changes an external app will notice (listed under
Changed). The editor login API changes shape (ADR 0037, platform 2026.7.0): the members that went are named
under Changed, and `Virtuademy.SDK.Core.Editor` is editor-only. **Breaking** for an app that used them: the
members listed under Removed (Configure App, `TenantConfigurationClient.UpdateAppCustomConfig`, `BuildScriptBase`) and
the type of `AppConfigurationSettings.BuildScript`; at 0.x a minor bump carries it.

### Added
- **Editor logins are connections in a per-user encrypted store** (meta-repo ADR 0037, first slice). A connection is one
  tenant of one Configuration-API instance: its key is the instance's normalized base URL plus the `tenantKey` of
  `config-public`, and it holds the app credential imported from the Back Office file, the session and the MSAL account it is
  pinned to. The store is under `%USERPROFILE%\.virtuademy\sdk\` (not `%LOCALAPPDATA%`, which a packaged app such as the
  Claude desktop app can redirect for the processes it starts), outside every project, shared by every project and
  process of the OS user: `connections.bin` (DPAPI for the current user, through a P/Invoke to `crypt32`), `msal.bin` (the
  one multi-account MSAL cache, through `SetBeforeAccess`/`SetAfterAccess`, encrypted the same way), `status.json` (the
  connections with no secret, readable without Unity) and lock files (`FileShare.None`). Each record has a revision; a writer
  takes the lock, re-reads the file, changes only its own connection and replaces the file atomically. Where nothing can be
  encrypted (every OS but Windows for now; the macOS Keychain one is pending) the connections stay in memory for the editor
  session, carried over a domain reload through SessionState (process memory, never EditorPrefs or a file), so a script
  recompilation does not log the creator out; nothing is written. A batch `login` is refused there (exit 29 in
  `EnvironmentsCli`), because it would end with the process, and the old registry token is not deleted. Asset-import worker
  processes never read or write it. New types: `ConnectionStore`,
  `ConnectionRecord`, `ProjectConnections`, `EditorRequestPump`, plus the internal `Dpapi`, `StoreCrypto`, `FileLock` and
  `EncryptedMsalCache`.
- **`ConnectionLoginService`: the login with no UI.** What `TenantSelectionWindow.OnLoginClicked` did inline (the HMAC reads of
  the tenant, the public configuration and the auth config, the validation of the config, the client id) is a service that the
  window, the command line and the request pump share. `LoginAsync` (by credential or by connection key), `AddConnectionAsync`,
  `ImportProjectCredentialsAsync`, `LogoutAsync`. An interactive login prints the authorization URL as well as opening the
  browser (`InteractiveLoginOptions.OnAuthUrl`, `OpenBrowser`), gives MSAL 60 s to issue it and the user three minutes counted
  from that moment (`LoginTimeoutException`, with `UrlIssued` saying which phase ran out), and pins the connection to its
  account. Two logins for one credential share one browser flow.
- **`ConnectionCommands` and `EditorRequestPump`**: `login`, `logout`, `connections`, `whoami` and `status` as one command layer.
  The command line (`EnvironmentsCli`, in `Virtuademy-SDK-Environments`) runs it in batch while the project is closed; an editor
  that has the project open polls `UserSettings/Virtuademy/requests/` from `EditorApplication.update` (250 ms, not in batch mode
  or in a worker process), runs `login`, `logout` and `status` requests only (never a build or a deploy), writes the answer next
  to the request or where `resultFile` says (a login first writes `state: awaiting-browser` with the URL) and renames the
  request `.working` then `.done`. No answer carries a token or a secret.
- **A token goes only to its own connection's hosts.** `EditorSessionManager` refuses (`InvalidOperationException`, nothing
  sent) a request whose host is not one of the connection's tenant APIs, and the editor tools take the connection key once per
  operation and pass it down: `EnvironmentsDeploy`, `PublishedEnvironmentsIndex`, the hot-update verifier and policy fetch
  all take an optional `connectionKey` (null means the project's only connection, read once at the start). The project's
  connections can change while a deploy runs (an import, a removal) without the deploy following them.
- `ConnectionStore.UpdateAsync` and `LockRenewalAsync`: another process's lock is awaited with `Task.Delay`, not slept on, so
  the editor's main thread is never held up behind it (a 3 s hold: the loop ticked normally). MSAL calls that touch the cache
  run on the thread pool for the same reason. The synchronous `Update` remains for code off the main thread.
- The interactive login passes the pinned account to the browser (`WithAccount`) and refuses an answer for another account
  (`LoginAccountMismatchException`, code `account-mismatch`): the browser's single sign-on cannot swap the connection's user.
  A request that joins a login in flight hears the same authorization URL.
- `ProjectConnections` (`UserSettings/Virtuademy/connections.json`, no token and no secret): the connections the project
  uses. **Nobody chooses a default connection**: an editor request or a command that names no connection uses the project's
  only one (`DefaultKey`), and with two or more it must name its connection (the command line exits 26). It decides neither
  where a deploy goes nor play mode. The same file keeps the project's deploy targets, per connection a
  world or the tenant level (`ProjectConnections.DeployTarget`, `TargetsOf`, `SetTargets`), which the Publish Environments
  window ticks. A connection added from a configuration file of the project
  (`ConnectionLoginService.AddConnectionAsync`, the import of the project's files) counts as one the project uses; a login that
  fails does not bind its connection. `ConnectionLoginService.RefreshConnectionAsync(key)` reads a
  stored connection's tenant again over HMAC (label, environment, platform version), with no login.
- `LegacyLoginMigration`: the first use of the store in a process deletes `Virtuademy_EditorLogin_Token` from EditorPrefs and
  SessionState (once per OS user, a marker in the store folder says so) and nothing else; the other nine
  `Virtuademy_EditorLogin_*` keys stay for an older SDK on the same machine and nothing reads them.
- `TenantPublicConfig.TenantKey` and `.Environment`, from `config-public` (`tenantKey` is the connection's key, `environment`
  is for display).
- `EditorSessionManager` methods that name the connection: `EnsureValidTokenAsync(connectionKey, ...)`,
  `SendAuthorizedAsync(connectionKey, ...)`, `LogoutAsync(connectionKey)`, `DescribeSession(ConnectionRecord, out bool)`;
  `EditorApiEndpoint.ApplicationApiUrlOf(connectionKey)`; `EditorLoginState.DefaultConnectionKey`.
- `ITokenProviderNotifications`, with the event `TokensChanged`: a token provider implements it to
  tell its clients that the tokens it handed out were cleared (sign-out) or replaced. Separate from
  `ITokenProvider`, so existing providers keep compiling.
- `ApiClientBase.TokenRefreshLeeway` (default 30 s).
- `JwtToken.IsExpired(TimeSpan timeOffset, TimeSpan leeway)`.
- `HttpHelper.DefaultTimeoutSeconds` (default 120) and `HttpHelper.UploadTimeoutSeconds` (default
  0, no limit).
- `ApiResponseError.Display(long statusCode, string content)`.
- `TenantPublicConfig.BackofficeUrl`: the tenant's Back Office address from `config-public` (`backofficeUrl`, platform
  2026.7.0; null when the tenant has none or the Configuration-API predates the field), kept on the connection as
  `ConnectionRecord.BackofficeUrl` at every login and refresh. The Publish Environments window of
  `Virtuademy-SDK-Environments` opens it (meta-repo ADR 0038).
- **`ConfigurationFileImport`: the import of the Back Office file, shared by both windows** (meta-repo ADR 0037, "Windows
  per kind of project"). `ImportIntoStoreAsync(file, env)` puts the file's credentials into the connection store, one
  connection per environment of the file, each recorded as one the project uses, and writes nothing to the project: the Publish Environments window and `EnvironmentsCli tenant-config` of
  `Virtuademy-SDK-Environments` call it, for environment projects. `ImportIntoProject(file, settings)` is the application's
  import, into the project and onto the settings asset, with the git dialog that the Available Tenants window used to hold
  itself. `PickFile()` opens the file panel in the user's Downloads folder. Nothing logs the file's content.
- `ProjectConnections.Remove(key)`: the project stops using a connection ("Remove from project..." in the Publish
  Environments window), with its deploy targets; the connection stays in the store. `ProjectConnections.UsedInStoreCount`.
- `TenantSelectionWindow.ActionsCreated` and `TenantSelectionWindow.Settings`: an application adds its own buttons beside
  "Change config" (the Virtuademy application adds its Build button there) and reads the settings asset when one is clicked.

### Changed
- **The editor session is read from the store, not from EditorPrefs** (behaviour change). `EditorLoginState` is now a read-only
  view of the project's default connection: nothing is kept in SessionState or EditorPrefs any more (the token sat in clear text
  in the registry), and a renewal or a logout done by another process shows at once. Removed: `EditorLoginState.Set`,
  `UpdateToken`, `MarkTokenExpired` and `Clear`, and `EditorSessionManager.LoginAsync(Tenant, ...)` (use
  `ConnectionLoginService.LoginAsync`). Old sessions are not carried over: every connection needs one login.
- **`AzureAuthService` has no static client any more**: `Init`, `InitEntraId`, `Reset`, `SignOutAsync` and the per-project
  cache under `Library/Virtuademy/Auth` are gone; `GetClient(clientId, authConfig)` returns one MSAL client per app registration
  and authority, all on the store's cache, and `LoginInteractive`, `TryAcquireTokenSilent` and `RemoveAccountAsync` take the
  client and the account to use. Silent renewal is **pinned to the connection's account**: it used to take the first account in
  the cache, which another tenant's login could have put there.
- **`EditorApiEndpoint` resolves from the connection** (behaviour change). The Application API address is the one the
  connection's tenant reported at login; the project's generated `PlatformEndpoints` asset no longer decides where an editor
  request goes (the build still reads it). A token goes to the host of the connection it belongs to.
- **A damaged or busy store file is handled carefully.** A reader without the lock keeps its last copy when the file is held
  or swapped (it opens with `FileShare.ReadWrite | Delete`), and never sets it aside; only a change, under the lock, sets a
  file nobody can decrypt aside. The swap is retried for about a second, and when it fails half-way (Windows error 1177) the
  new file is moved into place instead of being deleted.
- **The project's old `Library/Virtuademy/Auth/msal_*.bin` are deleted** the first time the store is used in the project: they
  held a refresh token in clear text and nothing reads them. A request file's `resultFile` must lie inside the requests folder.
- **Available Tenants is the tenant switch only** (behaviour change; meta-repo ADR 0037, "Windows per kind of project"). It
  lists the configuration files, selects one and applies it with **Change config** (the button was labelled "Configure App"),
  and imports a file into the project (`ConfigurationFileImport.ImportIntoProject`). It has no login, logout, session label or
  mismatch warning any more, no Build button and no App configuration button: the editor logs in only to publish
  environments, in the Publish Environments window of `Virtuademy-SDK-Environments`, which also took over the move of the
  project's old configuration files into the store. A selection no longer changes the project's connections. Its
  menu, `Virtuademy/Available Tenants`, is compiled only where the window has a job: not in a project that has
  `Virtuademy-SDK-Environments` installed (an environment project; `versionDefines` sets `VIRTUADEMY_SDK_ENVIRONMENTS`),
  unless the project defines `VIRTUADEMY_APPLICATION`, as the Virtuademy application does. The class stays public in every
  project, so code that opens it keeps compiling.
- **`AppConfigurationSettings.BuildScript` is a `ScriptableObject`** (was `BuildScriptBase`, which moved to the application).
  Nothing in the package reads it; the reference serialized in existing settings assets is kept.
- The messages that asked for a login name **Virtuademy > Publish Environments** instead of Available Tenants.
- **Requests created by `HttpHelper.CreateHttpRequest` time out after 120 s** (`UnityWebRequest.timeout`,
  which bounds the whole request). They had no timeout. Uploads (`RawBytes`, `MultipartFormData`)
  keep none. A request that legitimately takes longer needs `timeout` set on it, or the default
  raised.
- **A client drops its cached token when its provider raises `TokensChanged`**, and asks again on
  its next request. Before, a client kept the token it found until that token expired, so after a
  sign-out and another user's sign-in it went on signing as the first user.
- **Tokens are refreshed 30 s before they expire**, not at the expiry, and requests on one client
  that find the token expired together share one refresh.
- `JwtToken.Expiry` reads a value without an offset as UTC; it converted it as local time, while
  `IsExpired` compared it as UTC. `IsExpired` now compares against `Expiry`, so a value that
  carries an offset is converted before the comparison.

### Fixed
- `ApiResponse<T>`, `ApiResponseArray<T>` and `ApiResponseSearch<T>` threw from the constructor
  when a failed response's body was not JSON (the HTML page of a 502 from a proxy or gateway) or
  was `null`: the caller got an exception instead of a failed response. The body is now parsed
  safely and logged as an excerpt when it is not a problem-details object.
- **The session label showed the time without the date** ("session until 12:20" for a session that ends in a week), so a
  valid session read as one about to expire. The labels and the login and renewal logs now add the day and month when
  the moment is not today ("session until 2 Nov 12:20"; `EditorSessionManager.FormatLocal`).

### Removed
- **Configure App** (meta-repo ADR 0037, "Windows per kind of project"): `AppConfigurationWindow`, its UXML and its button
  in Available Tenants, and the public `TenantConfigurationClient.UpdateAppCustomConfig`. It had not worked since
  2026-04-01: it sent an HMAC-signed `PUT /manage/apps/config/custom`, a route Configuration-API dropped that day.
  Application configuration is changed in the Configuration Portal (meta-repo ADR 0008). `GetAppCustomConfig`, which the
  tenant switch reads, stays.
- **`BuildScriptBase`** and the Build button of Available Tenants moved to the Virtuademy application, their only user
  (the script kept its GUID there, so its assets still resolve). An external application is built with its developer's own
  tools. `AbstractAppConfigurator` stays: "Change config" runs it to apply an application's product name and theme.

## v0.3.0

### Added to this release on 2026-10-14 (tag moved)

The `v0.3.0` tag was moved on 2026-10-14 onto the commit that adds the changes below. They are editor-only
(command lines, editor windows); nothing a published world or the app loads changed. A project resolved before
keeps the earlier commit until it re-resolves: the setup's editor-start update or `SetupCli -vdSetupSteps
resolve` does it. SDK-Environments v10.0.0, moved the same day, needs this commit.

#### Added
- **`AppConfigurationImporter.Import`: connect the project to a tenant without clicking**. What
  "Connect the SDK to your tenant" does by hand: the file downloaded from the Back Office (**Download SDK
  Environments Config**) goes into the project (`Assets/Editor/TenantConfiguration`, the folder of the settings
  asset, unless it already is under `Assets/`), into the **App Assets** of the `AppConfigurationSettings` asset,
  and is selected (its app, from the file name, and the file's first environment, or the one asked for). The copy drops the
  " (1)" a browser adds to a second download of the same file, so the app keeps the Back Office's name and a newer download
  replaces the older one. The file
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

#### Changed
- **`EditorSessionManager` never starts an interactive login in batch mode** (behaviour change). `EnsureValidTokenAsync` and everything that goes through it (`SendAuthorizedAsync`) read their
  `allowInteractive` argument through `InteractiveLoginAllowed`, which is false when the editor runs in batch
  mode: a deploy, a script verification or a policy fetch with an expired session used to open the system
  browser on a machine where nobody could answer it, and wait for ever. They now fail as they do when the
  login is not allowed, and the caller says to log in.

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
