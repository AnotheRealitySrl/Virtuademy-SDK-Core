using System.Runtime.CompilerServices;

// The application installs the implementation and nothing else can, because
// IVirtuademyFramework.Install is internal to this assembly. An interpreted script references this
// assembly in full, so a public installer would let one script replace the surface every other
// script calls.
//
// The named assembly lives in Assets/_Project, not in this package: the implementations of these
// contracts belong to the application, along with SM and the platform's own systems, while the
// surface ships to creators. A creator installs this surface; they do not install what answers it.
[assembly: InternalsVisibleTo("Virtuademy.Worlds.ScriptingApiBackend")]
