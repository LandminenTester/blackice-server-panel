# Product
<!-- impeccable:product-schema 1 -->

## Platform
web

## Stack
Confirmed: ASP.NET Core 10, static Svelte/TypeScript, Windows ConPTY supervisor.

## Users
ReplayRE server developers and administrators working locally on Windows; German interface.

## Product Purpose
Start, stop, restart and inspect a Replay server through a local authenticated web interface independent of any roleplay framework or database.

## Operating Context
Local port 20772, Windows first. Future Debian Docker backend behind HTTPS.

## Capabilities and Constraints
Private current-user named pipes; true console transport; optional continued server operation when the webhost stops. An externally started server is detected and never silently adopted or terminated. No general-purpose shell.

## Product Principles
Explicit process ownership; accurate states; controlled stop; independently deployable.
