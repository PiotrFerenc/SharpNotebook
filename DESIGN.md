---
name: SharpNotebook
description: A calm, dependable dev-tool surface in Catppuccin Mocha — real Monaco editors, quiet mono chrome, color reserved for state.
colors:
  base: "#1e1e2e"
  mantle: "#181825"
  crust: "#11111b"
  surface0: "#313244"
  surface1: "#45475a"
  surface2: "#585b70"
  text: "#cdd6f4"
  subtext1: "#bac2de"
  subtext0: "#a6adc8"
  overlay0: "#6c7086"
  mauve: "#cba6f7"
  pink: "#f5c2e7"
  red: "#f38ba8"
  peach: "#fab387"
  yellow: "#f9e2af"
  green: "#a6e3a1"
  teal: "#94e2d5"
  sapphire: "#74c7ec"
  blue: "#89b4fa"
  lavender: "#b4befe"
typography:
  chrome:
    fontFamily: "'JetBrains Mono', ui-monospace, Consolas, monospace"
    fontSize: "0.7rem – 1rem"
    fontWeight: 600 – 700
    lineHeight: normal
  code:
    fontFamily: "'JetBrains Mono', ui-monospace, Consolas, monospace"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: normal
rounded:
  all: "0"
spacing:
  xs: "0.2em"
  sm: "0.4rem"
  md: "0.6rem"
  lg: "0.75rem"
components:
  panel:
    backgroundColor: "{colors.mantle}"
    borderColor: "{colors.surface0}"
    rounded: "{rounded.all}"
    shadow: "0 4px 14px rgba(17,17,27,0.45)"
  button-primary:
    backgroundColor: "{colors.surface0}"
    textColor: "{colors.text}"
    borderColor: "{colors.surface1}"
    rounded: "{rounded.all}"
    hover:
      backgroundColor: "{colors.surface1}"
      borderColor: "{colors.mauve}"
      textColor: "{colors.mauve}"
  button-danger:
    hover:
      borderColor: "{colors.red}"
      textColor: "{colors.red}"
  input:
    backgroundColor: "{colors.crust}"
    textColor: "{colors.green}"
    borderColor: "{colors.surface1}"
    rounded: "{rounded.all}"
    focus:
      borderColor: "{colors.mauve}"
      ring: "0 0 0 3px rgba(203,166,247,0.25)"
  badge:
    backgroundColor: "{colors.peach}"
    textColor: "{colors.crust}"
    rounded: "{rounded.all}"
  banner-warning:
    backgroundColor: "{colors.surface0}"
    borderColor: "{colors.peach}"
    textColor: "{colors.peach}"
    rounded: "{rounded.all}"
  output:
    backgroundColor: "{colors.crust}"
    textColor: "{colors.green}"
    borderColor: "{colors.surface0}"
    rounded: "{rounded.all}"
  output-error:
    textColor: "{colors.red}"
  output-html:
    backgroundColor: "{colors.text}"
    textColor: "{colors.crust}"
  code-editor:
    engine: Monaco (BlazorMonaco), one real editor instance per cell
    theme: vs-dark (Monaco built-in — see Rules)
    language: csharp for code cells, markdown for markdown cells
  scrollbar:
    trackColor: "{colors.mantle}"
    thumbColor: "{colors.surface2}"
    thumbHoverColor: "{colors.overlay0}"
    rounded: "{rounded.all}"
  selection:
    backgroundColor: "{colors.mauve}"
    textColor: "{colors.crust}"
rules:
  - name: One Signal Color
    description: Mauve is the only color that means "this is interactive/focused" — hover, active links, focus rings, the restart icon. Every other hue (red, peach, green) means a specific state (danger, warning, success/code-output), never decoration.
  - name: Code Stays Green
    description: The kernel's stdout/output readout and the Monaco code font both live in the same register — terminal green on near-black — so output never gets mistaken for chrome.
  - name: Soft Not Hard
    description: Depth is a soft, low-opacity drop shadow (panels) or a 1px border (everything else), never a hard-offset zero-blur block shadow — that belonged to the pixel-art world this one replaced and is explicitly refused here.
  - name: Real Editor, Not a Styled Textarea
    description: Every cell (code or markdown) is a real Monaco editor instance (BlazorMonaco StandaloneCodeEditor), not a textarea dressed up to look like one. Monaco's own vs-dark theme is used as-is rather than a hand-rolled Catppuccin Monaco theme, a deliberate scope cut — see "Known gaps" below.
  - name: Icons Are Drawn
    description: All icons are hand-authored 24x24 stroke line-art (Components/Icon.razor), one consistent 2px stroke weight, currentColor — no emoji, no icon font.
notes:
  supersedes: A pixel-art / CRT-terminal world (hard 2px bevels, zero radius, Press Start 2P, 8x8 sprite icons) shipped one iteration earlier and rejected by the user in favor of this one. That world is now anti-reference only — nothing here should regress toward its hard-offset bevel shadows or blocky sprite icons. Corner radius later converged with that world's by coincidence (a separate, explicit user request for sharp corners on the Catppuccin palette) — the shadow/icon material stayed soft and drawn-line respectively; only the radius token changed.
  knownGaps:
    - "Monaco renders with its built-in vs-dark theme, not a custom Catppuccin-matched Monaco theme. BlazorMonaco supports Global.DefineTheme for this, but registering a per-language-global theme before every cell's editor constructs has a real ordering risk (Blazor's OnAfterRender fires children before parents) that can't be verified without a browser in this environment — vs-dark was chosen as the version that's certain to render rather than one that might silently fail to apply. Revisit once real browser testing is available."
    - "No further verification beyond source review, the mechanical detector, and the bUnit suite was possible this iteration (no browser) — the surrounding chrome (panels/buttons/banners) is exercisable and tested; the Monaco editor surface itself is not."
