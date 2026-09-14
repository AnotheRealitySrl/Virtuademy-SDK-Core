# Release notes

## v0.3.0

### Changed
- **The DTOs and the platform contracts left for `Virtuademy-SDK-Library`.** The assemblies
  `Virtuademy.SDK.ApiData.Wire` and `Virtuademy.SDK.Core` are retired: 48 wire types,
  `IPlatformContext`, `IPlatformAuthentication` and the five value types all belong to the HTTP
  client's side of the line, and keeping them here meant a creator installed the platform's wire
  format in order to author a world.

### Added
- **`Virtuademy.ScriptingApi`**, the assembly a creator's interpreted script may name. It opens
  with the seventeen analytics types — `AnalyticDTO`, the three xAPI types, the six experience
  records and the four enums — which had to stay reachable because the shipped Visual Scripting
  nodes build xAPI statements out of them.

  It declares no first-party reference, which is the condition for the server-side whitelist to
  admit it, and it is where the platform contracts and the client-model base types are headed.

### Changed
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
- The `displayName` is now `Virtuademy-SDK-Core`, hyphenated like every sibling package.

### Added
- `TenantConfigurationClient` and the tenant wire DTOs: the Configuration API client, extracted from
  the application system that used to be it, so nothing in the creator graph needs a framework to
  read a tenant configuration.
- `ApiClientBase.AdoptConnection`, so a second client can reuse a connection another one resolved
  rather than resolving its own.
- The transport assembly `Virtuademy.SDK.Core.Client` (`…Interface.Client` before this rename).

### Fixed
- A token provider is no longer copied along with a connection: the two have different lifetimes,
  and copying the provider meant adopting a null one.

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
