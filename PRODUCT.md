# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

Blazor Server (.NET 10, ASP.NET Core), interactive server render mode. No CSS framework currently — inline styles only.

## Users

Single user: the developer building this tool, using it themselves as a personal daily driver. Not built for distribution to other users at this time (explicitly confirmed — no onboarding/multi-user concerns apply).

## Product Purpose

A Jupyter Notebook clone for C#: write and run C# code in cells inside a browser-based notebook, with variables/state persisting across cells like a REPL, rich output (tables, HTML, images, charts), and NuGet packages installable per-notebook. Success means it's a tool the user actually reaches for instead of a plain `.csx` script or a throwaway console project — comfortable for daily exploratory C# work.

## Positioning

Existing options for "notebook-style C#" are Jupyter kernels via .NET Interactive (Microsoft has put it in maintenance mode) or the C# Interactive window in Visual Studio (not a notebook, no persisted cell/output document). SharpNotebook is a small, self-built alternative: its own Roslyn Scripting kernel process (no .NET Interactive dependency), its own `.sharpnb` file format, running as a local Blazor Server app.

## Operating Context

Runs entirely on one machine, bound to localhost, no auth. Notebooks are `.sharpnb` files under a root folder (default `~/Documents/SharpNotebook`), browsed via an in-app file browser. Opening a notebook spawns a dedicated `SharpNotebook.Kernel.Host` OS process that stays alive for that notebook's session; cells execute against it via Roslyn's `ScriptState` chaining, so variables and `using`s persist cell to cell. Requires the .NET SDK installed on the host machine.

## Capabilities and Constraints

- Cell types: code (C#) and markdown (Markdig-rendered preview).
- Rich output: plain text, HTML, and images (base64 PNG) via a unified display protocol; any `IEnumerable` auto-renders as an HTML table; ScottPlot charts render inline as an example of the general byte[]→image path.
- IntelliSense: real Roslyn `CompletionService` results (not Monaco — a native Blazor dropdown driven by a small hand-written JS interop shim for cursor position/insert, since this dev environment has no browser to verify a Monaco integration against).
- `#r "nuget:PackageId,Version"` in a cell resolves and loads a real NuGet package (via `dotnet restore` on a throwaway project, not a hand-rolled resolver) — it then stays referenced for later cells in that kernel session.
- Kernel lifecycle: "Przerwij" (kill + respawn the kernel process — there is no cooperative cancellation for a stuck cell) and "Restart i uruchom wszystko" (kill + respawn + re-run every code cell top to bottom).
- Trust model: a notebook is only "trusted" (Run enabled) if it was created through this app's own "Nowy" button; anything else (hand-edited, copied in) opens locked behind a warning banner until the user explicitly trusts it — because a notebook is arbitrary code execution by definition and there is no sandbox.
- Constraint: single-user only, no auth, no remote access — explicitly out of scope, not a gap to fill later.
- Deliberately not supported: polyglot (F#/PowerShell alongside C#) — rejected because the underlying multi-language engine (.NET Interactive) is the same one this project avoided depending on.

## Evidence on Hand

None — no existing users, demos, testimonials, or press. This is a from-scratch personal tool; nothing here should be fabricated as social proof or case-study material.

## Product Principles

1. Lean over feature-complete: build the minimum that makes daily exploratory C# work comfortable, not a Jupyter/VS Code feature-for-feature clone.
2. No dependency on tooling that's itself at risk (this is why .NET Interactive was avoided as the execution engine).
3. A notebook is arbitrary code execution — never soften that fact with a false sense of safety (hence the trust-gating model instead of a sandbox that would be misleading if incomplete).
4. Reuse existing, battle-tested tooling (`dotnet restore`, Roslyn's own APIs) over hand-rolling equivalents, even when the hand-rolled version would be more "integrated."

## Accessibility & Inclusion

No accessibility requirement has been established beyond ordinary browser/keyboard usability; this is a single-user personal tool with no confirmed additional needs.
