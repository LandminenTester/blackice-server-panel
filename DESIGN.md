---
name: Replay Server Panel
description: Dark operating surface with a prominent console and quiet green status indicators.
colors:
  background: "#121715"
  surface: "#1b2420"
  console: "#0c110e"
  foreground: "#e5ebe4"
  muted: "#aebcb1"
  line: "#35443b"
  accent: "#9cdda6"
  danger: "#f0aaa2"
typography:
  headline:
    fontFamily: "IBM Plex Sans, sans-serif"
    fontSize: "32px"
    fontWeight: 500
    lineHeight: 1.15
    letterSpacing: "-0.025em"
  console:
    fontFamily: "IBM Plex Mono, monospace"
    fontSize: "12px"
    lineHeight: 1.8
rounded:
  input: "6px"
  button: "7px"
  console: "10px"
  dialog: "12px"
---

# Replay Server Panel

## Overview

The user-selected direction is a dark operating surface: a persistent toolbar, console as the primary region, and quiet green status marks. Clear operational state takes precedence over decoration.

Review scope: initial live disposable-instance review at desktop and mobile widths, followed by a bounded confirmation of four fixes using fresh desktop/mobile screenshots, current source, and root-agent live DOM evidence. The confirmation covers empty-state wording, keyboard access to console output, mobile status visibility, and sticky toolbar positioning; it is not a new backend or full accessibility audit.

Latest review: the Bundles addition received a bounded desktop/mobile visual review using `.impeccable/review/desktop.png` and `.impeccable/review/mobile.png`, with the disposition **ship** and no material fixes. This review covers the added interface within the established visual system; it does not constitute a full backend audit.

## Colors

Charcoal background and darker console establish hierarchy; surface and line colors separate controls without decorative shadows. Accent green indicates available primary actions and online state. Danger color identifies forced-stop controls. Every operational state also uses text.

## Typography

Self-hosted IBM Plex Sans serves interface copy; IBM Plex Mono serves console output and command entry. The main heading reduces to 28px on narrow screens. Console text reduces to 11px on mobile.

## Layout

The toolbar stays at the top while scrolling using sticky positioning and an opaque surface. Desktop uses a flexible console column and a 280px status column with a 32px gap. At 900px the layout stacks; at 520px details become one column and compact runtime/connection text appears beside the heading before the actions and console.

Bundle rows occupy the existing console region and separate entries with thin rules. Each row keeps its name and textual status together, with its actions alongside on desktop. At 520px the rows stack identity above actions and the console toolbar may wrap.

## Shapes

Controls use restrained rounded corners. The console is one bordered region with its own toolbar, output, and command row.

## Components

- Console output is a read-only textarea with a descriptive accessible name, native keyboard focus, selection, and scrolling. Visible focus uses a 2px accent outline with a 4px offset.
- Empty log output says there are no entries in the current file. Empty live output describes running, stopped, external, or unavailable supervisor state.
- Action buttons disable when busy, disconnected, or incompatible with server state. Stop and restart actions open confirmation dialogs; forced stop uses danger styling.
- The Live Console, Log File, and Bundles tabs share the existing quiet navigation treatment. Only the selected tab enters the tab sequence; Left/Right arrows wrap between tabs, and Home/End select the first/last tab while moving focus.
- Bundle entries group each name, textual status, and associated actions. Running bundles offer Restart and Stop; stopped bundles offer Start. Unknown status or an unavailable connection disables control. Manual refresh remains beside the panel heading; automatic refresh runs every 10 seconds while the Bundles view is selected.
- Hover transitions use 140ms ease-out; reduced-motion preference disables transitions.

## Do's and Don'ts

- Keep console output prominent and operational status understandable through text.
- Retain mobile status before operational controls and keep the toolbar visible during page scrolling.
- Do not imply a stopped server merely because logs are empty.
- Do not remove keyboard access or visible focus from the console.
