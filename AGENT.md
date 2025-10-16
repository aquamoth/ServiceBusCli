Project: Keyboard-first Azure Service Bus CLI (C# .NET 9)

Goal
- Fast, cross-platform, keyboard-first console app to browse and manage Azure Service Bus messages with AppConfig CLI-style UX.

Scope & Constraints
- No code changes in `codex-appconfig/`; replicate style only.
- v0 focuses on discovery, listing, paging, viewing, and DLQ workflows (resubmit/delete/tag).

Tech
- .NET 9, Azure SDKs (`Azure.Identity`, `Azure.ResourceManager`, `Azure.Messaging.ServiceBus`).
- External editor via `$VISUAL`/`$EDITOR` with OS fallbacks.

CLI
- `--subscription`, `--namespace`, `--queue`, `--topic`, `--topic-subscription`.
- `--auth auto|device|browser|cli|vscode`, `--tenant <guid>`.
- `--theme default|mono|no-color|solarized`, `--no-color`.
- `--amqp-verbose` for CBS/AAD diagnostics.

Auth
- AAD-only. SAS/connection strings are not used or required (including DLQ).

UI/UX
- Header with `PAGE x/y`, context line, global numbering, non-wrapping tables, PageUp/PageDown, footer command line + status.

Messages & Paging
- Peeks with `ServiceBusReceiver.PeekMessagesAsync`; maintains next sequence for forward paging; cached PageUp.

DLQ Workflows (Resolved)
- Session and non-session DLQ: AAD-only.
- Resubmit (DLQ→active): clone and send, then complete DLQ copy via SDK for non-session; for session queues, complete via AMQP (AAD/JWT + CBS) with robust browse.
- Delete (DLQ): prefer SDK on DLQ sub-queue; fallback to AMQP (AAD) two-pass browse with bounded time/credit; UI refreshes after operation.
- CBS diagnostics: audience, token type, status-code/description, correlation-id, elapsed. Accepts 200/202.

Known Issues — Resolved (2025-10-16)
- Intermittent DLQ completion timeouts fixed by: CBS verbose + 202 acceptance, removal of session filters on sub-queues, two-pass browse with bounded windows, and UI refresh after delete.

Build/Run
- Build: `dotnet build ServiceBusCli.sln`
- Run: `dotnet run --project src/ServiceBusCli -- --auth device [--amqp-verbose]`

Permissions
- ARM: Reader+ to discover. Data plane: `Azure Service Bus Data Receiver/Sender` or `Data Owner`.

Tests (snapshot)
- Sorting/selection order; basic width/truncation; command parsing; paging math.
