# Troop 786 Voting (tablet)

.NET MAUI Android kiosk app plus server-side vote logic for the troop leadership election.
Extends the existing AWS election app (API Gateway + .NET 10 Lambda + DynamoDB single table).

## Projects

| Project | Purpose |
|---|---|
| `Troop786.Core` | Contracts, tally rule, idempotent vote service, outbox sync. Plain net10.0, no platform code. |
| `Troop786.Aws` | `DynamoKioskStore`: conditional puts so one vote per scout per race round. |
| `Troop786.Tests` | xUnit tests for the tally rule, dedup, late uploads, revert and offline sync. |
| `Troop786.Tablet` | MAUI Android app: SQLite vote outbox, HTTP client, sync coordinator, Code Entry screen. |

## Rules implemented

- Most votes wins; only an exact tie for first place triggers a runoff between the tied candidates.
- One vote per scout per race per round; first accepted wins, later ones are flagged and never counted.
- Runoffs reuse the same codes and write separate vote records (`Round` 2, 3, ...).
- A vote cast while a round was open still counts when it uploads after the round closed.
- Admin revert deletes the vote (which reopens the scout's code) and writes an audit entry.
- Single-candidate races are auto-elected (`RaceTally.IsAutoElected`).

## Run the tests

```
dotnet test Troop786.Tests
```

## Build the tablet app

Requires the MAUI Android workload and Android SDK:

```
dotnet workload install maui-android
dotnet build Troop786.Tablet -f net10.0-android
```

Before it will talk to the backend: set `ApiBaseUrl` in `MauiProgram.cs`.

## Not built yet

SPL and PL ballot pages, confirmation, PLC selection, admin screens, the cycle bundle download
(`GET /kiosk/bundle`), device enrollment, and the Lambda endpoint wiring. The Code Entry screen currently
reports "no election data" because `UnsyncedCodeLookup` is registered until the bundle exists.

Add the Barlow and Barlow Condensed fonts to `Troop786.Tablet/Resources/Fonts` and register them to match
the web app's typography; the XAML currently uses the platform default font.

## Tablet sizing

Layouts target a Galaxy Tab A7 Lite (8.7 in, 1340x800 px). No page assumes a fixed 1280x800 canvas: bodies
scroll, and controls are sized in dp so header, content and footer fit a screen of roughly 600 dp height.
Confirm on the real device.
