using System.Runtime.CompilerServices;

// Monaco makes this component's completion-provider wiring untestable end-to-end through the DOM (no JS
// engine in bUnit) — the kernel-calling half is exposed `internal` instead and exercised directly.
[assembly: InternalsVisibleTo("SharpNotebook.Web.Tests")]
