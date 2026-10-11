---
name: Unofficial REPLAY.re - Server Webpanel
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

# Design System: Replay Server Panel

## Overview

**Creative North Star: "Cyberpunk server operations"**

The user-pinned direction is a Cyberpunk operating surface. Dark charcoal and plum panels carry sharp red contours, cyan operational information and selective yellow action accents. IBM Plex typography and generous separation keep the expressive palette readable during server administration.

This system replaces the former green, rounded interface. Preserve the approved dark, angular identity across Dashboard, Console, Bundles, Settings, sign-in and first-run setup. English and German share the same visual hierarchy and control behavior; longer translations must wrap without obscuring an action or state.

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
- **Tertiary — Yellow:** primary progression/start actions, selected navigation and console mode, and the current setup step.
- **Neutral:** charcoal background, plum toolbar and surface, a deeper console, pale pink foreground and muted pink secondary copy. The line color separates regions and fields.

**The State Text Rule.** Colored square indicators always accompany readable status text. Cyan is positive operational information; disabled controls and stale or unknown bundle states must not imply confirmed availability.

## Typography

Self-hosted IBM Plex Sans (400, 500 and 600) serves the interface; self-hosted IBM Plex Mono (400) serves output, commands, paths and bundle names. Keep prose in sentence case and preserve the locale's spelling.

- Main screen headings use the headline token; narrow screens use 32px. Authentication/setup headings are 40px on desktop and 32px on mobile.
- Section titles use the title token. Body paragraphs use the body line height with a maximum width of 70ch.
- Form labels and state badges use 14px. Metadata and hints use 13px; mobile navigation and footer use 12px.
- Console text uses the console token and reduces to 11px on mobile. Bundle names use 15px mono. Runtime values use tabular numerals.

## Layout

An opaque sticky toolbar (82px high on desktop) holds the brand, connection text, language selector and sign-out. The app shell pairs a 228px navigation rail with a flexible workspace, capped at 1700px. Workspace padding is 42px at the top and 40px horizontally. The page heading, textual state badge, feedback and operational controls precede each content region.

Dashboard pairs a 280px runtime column with recent output, separated by 36px. Console uses one full-width panel with a mode toolbar, selectable output and command row. Bundles use a divided list with identity/state on the left and actions on the right. Settings content is capped at 650px; its selectors at 380px. Authentication/setup uses a centered container capped at 620px, padded 64px vertically and 32px horizontally.

At 1100px and below, the navigation rail narrows to 190px, workspace gutters become 24px and Dashboard stacks. Runtime details become a two-column definition list; toolbar connection text is hidden, while the page state badge remains.

At 720px and below, navigation becomes a horizontal row of four icon-and-label tabs beneath the toolbar. The toolbar has a minimum height of 72px and can grow with wrapped text. Workspace gutters become 16px; the heading and state badge stack. Toolbars wrap, server actions share available width, and bundle identity precedes full-width action rows. Authentication/setup padding becomes 42px by 22px. Paths, identifiers and feedback wrap; console content scrolls within its textarea. Console output height remains responsive (`clamp(360px, 55vh, 680px)`) and vertically resizable.

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

Dashboard, Console, Bundles and Settings form a tablist with one active tab in the tab sequence. Each tab declares selection and links to the labelled tabpanel. Left/Right arrows wrap, while Home/End select the first/last view and move focus. Selected tabs use yellow, an outlined contour and the cut corner. Navigation clears action feedback and errors so messages remain tied to the view that produced them.

### Console and operational state

Recent output and Console output are labelled read-only textareas that retain keyboard focus, selection, scrolling and vertical resizing. Live console and Log file are pressed-state buttons in a labelled group. Following live output scrolls to the latest line; log mode offers explicit refresh. The command field has a visually hidden label and a visible placeholder, and sending requires a running connected server and nonempty input.

Server and bundle actions disable while busy or when state/connection makes them unavailable. Restart, Stop and forced stop for the server open a native modal dialog with a labelled heading and Cancel/Confirm actions. Bundle rows keep names, textual states and associated actions together; running bundles offer Restart/Stop, stopped bundles offer Start, and unconfirmed state disables control. While Bundles is selected, refresh is attempted every 1500ms, with overlapping/busy requests skipped; manual refresh remains visible.

### Feedback and setup

Errors use `role="alert"`; success and progress feedback use `role="status"` where implemented. Setup exposes an ordered Account/Server/Finish sequence with `aria-current="step"`, labelled fields and validation-gated progression. Folder browsing and server confirmations use native dialogs. Installation progress exposes a labelled progressbar with phase text; the indeterminate transfer animation runs for 1.5 seconds per cycle.

Hover background transitions last 140ms with ease-out timing. Under `prefers-reduced-motion: reduce`, animations and transitions are disabled and the indeterminate bar becomes a static full-width indicator; textual phase feedback remains available.

## Do's and Don'ts

- **Do** preserve the red/cyan/yellow roles and dark angular surfaces selected by the user.
- **Do** keep state text, visible labels, keyboard access and cyan focus outlines alongside visual indicators.
- **Do** preserve wrapping and responsive stacking for both English and German.
- **Do** keep Console selection/scrolling usable and require confirmed bundle control availability.
- **Don't** restore the former green palette or rounded control language.
- **Don't** present unrelated settings feedback after navigation or infer stopped state from empty output.
- **Don't** treat synthetic review fixtures as product defaults or describe this bounded review as a complete accessibility or backend audit.


## Startup preference addition

Settings reuses the existing custom Toggle for the persisted auto-start preference, with a localized explanation and disabled state during requests or disconnection. Language selection also persists the native control-terminal language. No visual identity or spacing tokens changed. Native process terminals are functional OS surfaces and do not adopt the web dashboard chrome.

## Fixed dashboard and accounts
At desktop sizes, the header and sidebar remain visible. The workspace occupies the remaining viewport; the console output and large lists scroll within their own regions. Full HD target: 1920×1080 at default zoom. Server controls and status share the header on every authenticated page. Sidebar footer contains REPLAY.re, the unofficial designation and the public GitHub link. Users reuse Toggle and existing form tokens for individual permissions; API authorization controls capabilities.

Verification of the account/shell change: complete 1920×1080 console and Users screenshots; DOM checks on all five authenticated pages show no body/workspace scrolling at default Full HD zoom. Mobile Users at390px has no horizontal overflow. Dynamic-tab Home/End reaches Users. Finish review resolved keyboard wrapping and permission-sensitive cached logs. Backend permission, password/session revocation and protected-last-manager checks are covered separately by integration/core tests.
