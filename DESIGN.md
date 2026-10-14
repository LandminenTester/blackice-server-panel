---
name: Unofficial BlackICE - Server Webpanel
description: Cyberpunk operating surface with dark panels, angular red contours, cyan information and selective yellow actions.
colors:
  background: "#101116"
  surface: "#19171e"
  toolbar: "#151319"
  console: "#0c1016"
  foreground: "#f5d8d9"
  field-foreground: "#f9e5e6"
  muted: "#c3abb2"
  line: "#834653"
  red: "#ff6477"
  cyan: "#74e7e9"
  yellow: "#f4e56e"
  button: "#211b24"
  button-hover: "#3b202b"
  primary-foreground: "#17151a"
  primary-hover: "#fff393"
  selected-surface: "#2c2720"
  field: "#121118"
  notice-surface: "#15292e"
  error-surface: "#301921"
  error-foreground: "#ffb2bd"
typography:
  headline:
    fontFamily: "IBM Plex Sans, sans-serif"
    fontSize: "clamp(30px, 3vw, 44px)"
    fontWeight: 500
    lineHeight: 1.1
    letterSpacing: "-0.03em"
  title:
    fontFamily: "IBM Plex Sans, sans-serif"
    fontSize: "20px"
    fontWeight: 500
  body:
    fontFamily: "IBM Plex Sans, sans-serif"
    lineHeight: 1.6
  label:
    fontFamily: "IBM Plex Sans, sans-serif"
    fontSize: "14px"
  console:
    fontFamily: "IBM Plex Mono, monospace"
    fontSize: "13px"
    lineHeight: 1.8
rounded:
  control: "0px"
spacing:
  compact: "8px"
  control-gap: "12px"
  mobile-gutter: "16px"
  panel-inset: "24px"
  heading-gap: "32px"
components:
  button:
    backgroundColor: "{colors.button}"
    textColor: "{colors.red}"
    rounded: "{rounded.control}"
    padding: "10px 20px"
  button-primary:
    backgroundColor: "{colors.yellow}"
    textColor: "{colors.primary-foreground}"
    rounded: "{rounded.control}"
    padding: "10px 20px"
  button-primary-hover:
    backgroundColor: "{colors.primary-hover}"
  field:
    backgroundColor: "{colors.field}"
    textColor: "{colors.field-foreground}"
    rounded: "{rounded.control}"
    padding: "12px"
---

# Design System: Unofficial BlackICE - Server Webpanel

## Overview

**Creative North Star: "Cyberpunk server operations"**

The user-pinned direction is a Cyberpunk operating surface. Dark charcoal and plum panels carry sharp red contours, cyan operational information and selective yellow action accents. IBM Plex typography and generous separation keep the expressive palette readable during server administration.

This system replaces the former green, rounded interface. Preserve the approved dark, angular identity across Dashboard, Console, Bundles, Settings, Users, sign-in and first-run setup, including diagnostic streams, optional LAN bridge details and RE Server updates. English and German share the same visual hierarchy and control behavior; longer translations must wrap without obscuring an action or state. This increment extends the incumbent implementation without a redesign or a new approved composition.

**Key Characteristics:**

- Dark tonal surfaces and angular red contours.
- Cyan operational information and visible focus.
- Selective yellow actions and active selections.
- Readable English/German controls and native output interaction.

Review evidence is bounded: `.impeccable/review/desktop.png` and `mobile.png` cover Bundles in German; `desktop-console.png` and `mobile-console.png` cover Console in English; `desktop-setup.png` and `mobile-setup.png` cover the Account step in English. These are disposable instances with synthetic fixtures, not real server or bundle names. The reviewer disposition was **fix**, identifying obsolete documentation and Settings feedback persisting after navigation to Console. Current `navigate()` clears notice and error before changing views. The correction was confirmed in the live DOM and replacement Console viewport captures: no unrelated settings success remains after navigation. Full-page capture proved unreliable during this bounded confirmation, so the final Console evidence uses normal viewport captures. This is a bounded correction check, not a new full review. The mechanical design detector ran once and returned no findings (`[]`). This evidence does not establish exhaustive accessibility, browser coverage or backend correctness.

## Colors

The frontmatter records implemented colors from `ui/src/style.css`; semantic use matters more than decoration.

- **Primary — Red:** brand, headings, structural accents, ordinary button text and danger contours. Error messages use the brighter error foreground on a dark red surface.
- **Secondary — Cyan:** console output, selected language display, focus outlines, success feedback and connected/running indicators.
- **Tertiary — Yellow:** primary progression/start/apply actions, selected navigation and the current setup step.
- **Neutral:** charcoal background, plum toolbar and surface, a deeper console, pale pink foreground and muted pink secondary copy. The line color separates regions and fields.

**The State Text Rule.** Colored square indicators always accompany readable status text. Cyan is positive operational information; disabled controls and stale or unknown bundle states must not imply confirmed availability.

## Typography

Self-hosted IBM Plex Sans (400, 500 and 600) serves the interface; self-hosted IBM Plex Mono (400) serves output, commands, paths and bundle names. Keep prose in sentence case and preserve the locale's spelling.

- Main screen headings use 34px in the desktop operation shell and 32px on narrow screens. The headline token remains the general heading rule. Authentication/setup headings are 40px on desktop and 32px on mobile.
- Section titles use the title token. Body paragraphs use the body line height with a maximum width of 70ch.
- Form labels and state badges use 14px. Metadata and hints use 13px; mobile navigation and footer use 12px.
- Console text uses the console token and reduces to 11px on mobile. Bundle names use 15px mono. Runtime values use tabular numerals.

## Layout

An opaque toolbar (82px high on desktop) holds the brand, server controls and textual state, language selector and sign-out. The desktop app shell fills the remaining viewport height and pairs a fixed 228px navigation rail with a flexible workspace without a maximum width. Workspace padding is 24px at the top, 32px horizontally and 18px at the bottom, with a 16px gap between regions. The page heading and feedback precede each content region. Console and long lists scroll internally; the workspace can scroll when content exceeds the available height, while the header and sidebar remain visible.

Dashboard pairs a 280px runtime column with recent output, separated by 36px. The runtime column also holds the bridge summary and scrolls internally when needed. Console uses one full-width panel with diagnostic filters, selectable output and command row. Bundles use a divided list with identity/state on the left and actions on the right. Desktop Settings content is capped at 1000px; its selectors at 380px. Bridge configuration and RE Server updates use collapsible outlined regions. Authentication/setup uses a centered container capped at 620px, padded 64px vertically and 32px horizontally. Preserve the fixed 1920×1080 operation shell at default zoom; growing diagnostic content scrolls within its output region.

Between 721px and 1100px, the navigation rail narrows to 190px, workspace padding becomes 20px and Dashboard stacks. Runtime details become a two-column definition list. Users also stacks, with the user list capped at 220px and permissions arranged in two columns. The header retains compact controls and the textual server state; connection text is hidden.

At 720px and below, navigation becomes a horizontally scrollable row of available icon-and-label tabs beneath the toolbar. The toolbar has a minimum height of 72px and can grow with wrapped text; server controls and state occupy a dedicated full-width row. Workspace padding becomes 22px vertically and 16px horizontally. Toolbars wrap, bundle identity precedes full-width action rows, and Users fields and permissions become one column. Authentication/setup padding becomes 42px by 22px. Paths, identifiers and feedback wrap. Console output scrolls within a non-resizable textarea: it flexes to available panel height on desktop and uses 50dvh on mobile. The sidebar footer contains BlackICE, the unofficial designation and the public GitHub link; on mobile these wrap beneath navigation.

## Elevation & Depth

Depth comes primarily from tonal surfaces, one-pixel contours and region dividers. The toolbar stays opaque over scrolled content. Floating listboxes use a downward shadow (`0 12px 24px #0008`); dialogs use a broader shadow (`0 20px 60px #0008`) and a dark backdrop. Keep these elevations attached to overlays rather than ordinary content panels.

## Shapes

Buttons and fields have square corners. Panels, square status marks, line icons and outlined setup step numbers repeat this angular language. Active navigation clips its upper-right corner by 12px; the yellow selection outline and fill remain visible. Use restrained red structural lines rather than adding rounded cards, pill controls or decorative neon effects.

## Components

### Buttons and fields

Buttons have a minimum height of 44px, a one-pixel contour and the button spacing token. Ordinary actions are red on dark plum; primary actions use yellow with dark text; danger actions use a red border. Hover changes the background and border, while disabled buttons reduce opacity and show the unavailable cursor. Inputs and textareas retain native editing behavior, visible labels, cyan carets and a cyan two-pixel focus outline with a four-pixel offset. Selection uses cyan with dark text.

### Custom selectors and switches

Selectors use a labelled button with `aria-haspopup="listbox"` and `aria-expanded`. Opening focuses the selected option; options expose `aria-selected`. Up/Down arrows wrap through options, Home/End jump to the ends, Escape closes and returns focus, and choosing an option returns focus to the trigger. Leaving the control closes its listbox. Disabled selectors use the button's native disabled state.

The Follow output control is a button with `role="switch"`, a visible label and `aria-checked`. Its square thumb moves between the ends of a small outlined track and becomes cyan when checked. Native button activation supports keyboard use.

### Navigation

Dashboard and Settings, plus permission-visible Console, Bundles and Users, form a tablist with one active tab in the tab sequence. Each tab declares selection and links to the labelled tabpanel. Left/Right arrows wrap through the available tabs, while Home/End select the first/last available view and move focus. Selected tabs use yellow, an outlined contour and the cut corner. Navigation clears action feedback and errors so messages remain tied to the view that produced them.

### Console and operational state

Recent output and Console output are labelled read-only textareas that retain keyboard focus, selection and scrolling. Desktop output regions flex within their panels instead of resizing; Console is also non-resizable on mobile. DiagnosticStream replaces the former console modes and manual log refresh. Permission-filtered source and module selectors, a labelled search field, Pause/Resume and Download share a wrapping toolbar. Follow output and Include history reuse the square switches. The cursor stream polls every second while unpaused, keeps up to 2000 received rows and reports dropped lines. History includes retained streams; download exports the currently filtered output, rather than promising arbitrary historical downloads. Following output scrolls to the latest line. Source/module/search fields stack at 720px and below, while Pause and Download share the action row. The command field has a visually hidden label and a visible placeholder, and sending requires permission, a running connected server and nonempty input.

Server and bundle actions disable while busy or when state/connection makes them unavailable. Restart, Stop and forced stop for the server open a native modal dialog with a labelled heading and Cancel/Confirm actions. Bundle rows keep names, textual states and associated actions together; running bundles offer Restart/Stop, stopped bundles offer Start, and unconfirmed state disables control. While Bundles is selected, refresh is attempted every 1500ms, with overlapping/busy requests skipped; manual refresh remains visible.

### Feedback and setup

Errors use `role="alert"`; success and progress feedback use `role="status"` where implemented. Setup exposes an ordered Account/Server/Finish sequence with `aria-current="step"`, labelled fields and validation-gated progression. Folder browsing and server confirmations use native dialogs. Installation progress exposes a labelled progressbar with phase text; the indeterminate transfer animation runs for 1.5 seconds per cycle.

Hover background transitions last 140ms with ease-out timing. Under `prefers-reduced-motion: reduce`, animations and transitions are disabled and the indeterminate bar becomes a static full-width indicator; textual phase feedback remains available.

### Panel updates

Settings begins with a square outlined update card, padded 16px by 20px on desktop and 14px on mobile. Its red heading shares a row with Check for updates; the heading wraps on mobile. Installed/latest versions use cyan mono text, and release notes use the existing cyan link treatment. Version metadata wraps with 12px vertical and 24px horizontal gaps. Actions share a row on desktop and become full-width buttons at 720px and below. The card introduces no new color or shape tokens.

Native updates separate Download and verify from Install and restart. Download requires an available update and install permission; installation requires the verified ready state and opens a labelled native confirmation dialog. Busy, active and reconnecting states disable relevant actions. Cancel is offered while downloading, verifying or ready. Hosts that cannot install show explanatory copy instead of installation controls. A labelled native progress element displays download bytes in an eight-pixel cyan bar. Phase and reconnect text use `role="status"`; failures use the existing alert styling. Reconnecting state persists across reloads until polling confirms application has finished. These controls retain the existing focus and reduced-motion behavior.

Bounded visual evidence for this increment is `.impeccable/review/fullhd-updates.png` (1920×1080 Settings) and `mobile-updates.png` (390px). Root-agent layout checks place the Settings bottom at 979px within the 1080px viewport without workspace scrolling; mobile has no horizontal overflow. Captures show the idle/current state, not every download, confirmation or reconnect state. This confirmation preserves the prior accessibility and backend review limits.

### LAN bridge and RE Server updates

The optional bridge summary separates transport from health and phase, with readable labels for build commit, tick, last reply, start time and last exit/code. Unknown values use an em dash or localized unknown state; empty output does not imply a stopped server. The bridge supports `sv_lan 1` only and preserves PTY/ConPTY fallback when unavailable. Settings places its enable switch and labelled port/retention fields inside a native details region. Fields use three columns on desktop and one on mobile; Save opens a labelled native confirmation dialog. Restart-required and saved feedback use status text. Save failures remain visible independently of background polling failures.

RE Server updates reuse the same collapsible region, red headings, cyan mono version metadata and yellow apply action. The workflow checks available versions, prepares a selected official-alpha release or local backup, and applies only a ready job after native confirmation. Target selection is disabled during an active or ready job; permission and readiness gate preparation/apply. The UI states the official-alpha/local-backup availability boundary and makes no promise of arbitrary historical downloads. Cancel is offered for downloading or ready jobs. Job state and byte count use status text; action failures remain visible after successful background polling. At 720px and below, the hint and Check button stack without allocating spare height, and action buttons occupy the full width in both German and English.

Bounded finish evidence is `.impeccable/review/bridge-desktop-{dashboard,console,settings,server-updates,target,bridge}.png` and `bridge-mobile-{dashboard,console,bridge,server-updates,en-updates}.png`. The latest German and English mobile update captures show the corrected stacked heading. The finish reviewer found no further visual changes needed after that fix; action/polling errors were subsequently separated in `Bridge.svelte` and `ServerUpdates.svelte`. Root-agent validation reports `npm check` with zero errors/warnings; diagnostics 27, server updates 17, core 22, panel updates 14 and integration 68 passing tests, plus successful real Windows/Linux/Docker bridge and update checks on `main` at `3a2dd8e`. This is bounded visual and functional evidence, not an independent security audit or exhaustive accessibility/browser coverage.

### Server directory recovery

Settings → General adds a native server-directory text field and Check and save path action inside the existing square outlined panel. The field retains the dark surface, pale foreground and cyan focus treatment; its explicit label is available to assistive technology. Desktop keeps the input and action on one row with a 12px gap; at 720px and below they stack and stretch to the available width. Saved feedback uses the existing cyan status treatment, and validation failures use the red alert treatment. Editing clears prior feedback.

A missing directory or executable replaces the global Start/Restart controls with readable recovery text and, for users with settings permission, an Update server path action that opens Settings → General directly. Stop remains available while a server is running. Native path changes require a connected, stopped server and a validated absolute directory containing the executable. Saving persists the target and updates backend and bundle paths without a webhost restart; it does not move files or force a restart. Docker presents the directory read-only and explains that changing a container mount requires deployment changes and container recreation.

This is a code-led, bounded extension of the user-pinned Cyberpunk/Plex shell, with no new approved composition or token changes. Evidence is `.impeccable/review/path-desktop-{general,warning,saved}.png` and `path-mobile-{general,saved}.png`. Root-agent integration checks confirm relocated-directory detection and blocked Start, rejection of relative and missing targets, acceptance of a valid path with spaces, preserved credentials, backend/bundle adoption without a webhost restart, and CSRF/settings-permission gates. Browser checks confirm saved/error feedback and no horizontal overflow at 390px. The mechanical detector returned `[]`; Svelte checking reported zero errors and warnings. These checks do not establish a full accessibility or security audit.

### Startup preference

Settings reuses the existing custom Toggle for the persisted auto-start preference, with a localized explanation and disabled state during requests or disconnection. Language selection also persists the native control-terminal language. No visual identity or spacing tokens changed. Native process terminals are functional OS surfaces and do not adopt the web dashboard chrome.

### Fixed dashboard and accounts
At desktop sizes, the header and sidebar remain visible. The workspace occupies the remaining viewport; the console output and large lists scroll within their own regions. Full HD target: 1920×1080 at default zoom. Server controls and status share the header on every authenticated page. Sidebar footer contains BlackICE, the unofficial designation and the public GitHub link. Users reuses Toggle and existing form tokens for individual permissions; API authorization controls capabilities. The wide layout pairs a bordered user list with an editor separated by a red top contour. Username/password fields share a row, and permissions use a three-column grid. The enabled switch and individual permission switches expose labels and checked state, and disable during requests. Editing preserves the username and permits an optional replacement password; creation requires a password. Save uses the existing yellow primary action, and Delete opens a native confirmation dialog. Editor errors and saved feedback retain alert/status semantics.

Verification of the account/shell change: `.impeccable/review/fullhd-console.png` and `fullhd-users.png` are complete 1920×1080 captures; `mobile-users.png` covers Users at 390px. The bounded finish review disposition is **ship**. Root-agent DOM checks on all five authenticated pages show no body/workspace scrolling at default Full HD zoom; Mobile Users has no horizontal overflow. Dynamic-tab Home/End reaches Users. Finish review resolved keyboard wrapping and permission-sensitive cached logs. Backend permission, password/session revocation and protected-last-manager checks are covered separately by integration/core tests. This increment preserves the Cyberpunk tokens and reduced-motion behavior; its visual confirmation does not constitute an exhaustive accessibility or backend audit.

### Experimental Bundle Tools settings

Bundle Tools retains the incumbent outlined settings surface and keeps its enable switch above three local subtabs: General, Build tools and Connected accounts. Only the selected region is rendered. The subtabs reuse the existing yellow active treatment, cyan focus outline and tablist semantics; Left/Right and Home/End select and focus tabs. Tool rows distinguish installed versions and system versus panel-managed provenance in readable badges. A system tool offers an optional panel copy, while removal is limited to panel-managed tools. These states preserve the existing palette and do not introduce new token values.

Connected-account labels, provider badges and stored-token badges form a wrapping flex row with the existing compact (8px) gap. The provider field reuses the custom selector. Tool identity wraps separately from actions; at 720px and below rows stack vertically and action groups wrap. The sidebar experimental label occupies its own line above the panel version rather than extending the version button. These are code-evidenced refinements; visual and functional verification remain bounded to the checks reported for this increment.

### Bundle project workspace

Project lists use quiet divider-separated rows, with identity/build state on the left and Details/Build actions on the right. Web-UI and branch/commit badges wrap with the existing compact gap. Progressive disclosure keeps branch/tag selection, package versions, development/review actions and project workflow policy inside a focused details dialog rather than expanding every list item. The dialog separates Project and Merge branches into keyboard-operable tabs so each desktop region fits the Full HD workspace. The setup, detail and archive-inspection surfaces share a two-column grid with a single-column layout at 900px and below. Dialogs use the incumbent dark surfaces, red rules and yellow primary action, bounded by the viewport with internal scrolling. Optional web-package switches use two columns and stack at 540px.

Searchable account/owner selectors use labelled combobox inputs, bounded listboxes, selected-state yellow and the existing cyan focus treatment. Repository discovery groups account, owner, search, results and pagination in that order. Source output remains selectable plain monospaced text inside a separate operation dialog, with a structured toolbar for state, follow, pause and download; text search does not change the original log. Phase emphasis uses yellow and errors use the existing error foreground. Upload stages precede archive metadata/installation and checks, making validation separate from the install decision. These are extensions of the incumbent operational world, not new palette or typography tokens. Permission-hidden actions must not be represented as available; long EN/DE labels and untrusted repository/log text must wrap without introducing horizontal page scrolling.

## Do's and Don'ts

- **Do** preserve the red/cyan/yellow roles and dark angular surfaces selected by the user.
- **Do** keep state text, visible labels, keyboard access and cyan focus outlines alongside visual indicators.
- **Do** preserve wrapping and responsive stacking for both English and German.
- **Do** keep diagnostic selection/scrolling usable, export only the filtered output and require confirmed bundle control availability.
- **Don't** restore the former green palette or rounded control language.
- **Don't** present unrelated settings feedback after navigation or infer stopped state from empty output.
- **Don't** treat synthetic review fixtures as product defaults or describe this bounded review as a complete accessibility or backend audit.

Bundle creation keeps identity and actions above the two-column form; repository controls stay present while disabled. Package changes use a separate dialog. The Merge branches tab compares independently selected remote source/target refs. Bundle search/category/checked time share a toolbar, and lifecycle options live in one management dialog. Permission controls use descriptive fieldset groups.
