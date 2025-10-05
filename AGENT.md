Project: Keyboard-first Azure Service Bus CLI (C# .NET 9)

Goal
- Build a fast, cross-platform, keyboard-first console app to browse and (eventually) edit Azure Service Bus messages, matching the UX, themes, and colorization of the existing AppConfig CLI.

Non-Goals / Constraints
- Do not modify or depend on code changes in `codex-appconfig/`.
- Keep UX/style parity with AppConfig CLI: header, paging list, bottom command line, themes, and colorization.
- Initial scope focuses on discovery, listing, paging, and viewing a selected message in an external editor.

Tech Stack
- .NET 9 console app.
- Azure SDKs: `Azure.Identity`, `Azure.ResourceManager`, `Azure.ResourceManager.ServiceBus`, `Azure.Messaging.ServiceBus`.
- Cross-platform external editor integration (VISUAL/EDITOR env vars; platform fallbacks).

High-Level Flow (v0)
1) Auth & discovery: Sign in (AAD) and enumerate Service Bus namespaces the user can access via ARM. Expand to queues and topic subscriptions.
2) Pre-select via CLI: If a namespace and entity are provided as args, skip selection UI.
3) Selection UI: If not preselected, show a list (numbered) of resources to choose, mirroring the AppConfig app’s login/selection flow and style.
4) Main screen: List messages of the selected entity (queue or topic-subscription) with PageUp/PageDown paging and a bottom command line.
5) Command: `open <n>` opens the selected message in the external editor, using the same editor selection logic and colorized preview style.

Resource Model
- Resource selection is two-stage:
  - Namespace → Entity
  - Entities:
    - Queues: `sb://<ns>.servicebus.windows.net/<queue>`
    - Topic subscriptions: `sb://<ns>.servicebus.windows.net/<topic>/Subscriptions/<subscription>`
- Discovery uses ARM (read-only) to list: Namespaces → Queues, Topics → Subscriptions.

Auth
- Default: AAD with chained credentials and selectable mode like the AppConfig CLI (auto, device, browser, cli, vscode), with optional `--tenant`.
- Data plane requires `Azure Service Bus Data Receiver/Sender` or `Owner` on the entity/namespace. ARM discovery requires Reader/contributor access at subscription/RG/namespace scope.
- Environment fallbacks: respect `AZURE_TENANT_ID` if provided.

CLI Options (initial)
- `--namespace <name|resource-id>`: Preselect a namespace (name or full ARM ID).
- `--queue <name>`: Preselect a queue in the selected namespace.
- `--topic <name>` and `--subscription <name>`: Preselect a topic/subscription entity.
- `--auth <auto|device|browser|cli|vscode>`: Auth mode (default: auto).
- `--tenant <guid>`: Entra tenant ID.
- `--theme <name>`: Theme preset (`default`, `mono`, `no-color`, `solarized`), same as AppConfig CLI.
- `--no-color`: Disable color output.

UI/UX Parity with AppConfig CLI
- Header: Title, page indicator `PAGE x/y` right-aligned. Below, the selected Namespace/Entity (colorized), and any active filters.
- List: Non-wrapping, fits to terminal width with per-page dynamic column sizing. Columns: `#`, `Seq`, `Enqueued`, `Expires`, `MessageId`, `Subject`, `[Props]`, `Preview`.
- Paging: `PageUp`/`PageDown` navigates message pages; terminal resize triggers a refresh and reflow.
- Bottom command line: `Command (h for help)>` identical style.
- Themes & colorization: Reuse the same palette and rules; keys (property names) vs values colorized similarly to AppConfig’s key/value schema.

Message Retrieval & Paging
- Use `Azure.Messaging.ServiceBus` receivers in Peek mode to avoid locks: `PeekMessagesAsync(pageSize)` and `PeekFromSequenceNumberAsync` for forward paging.
- Page size default: 50 (configurable). Maintain last seen sequence number per page to continue with `+1` for the next page.
- Subscriptions: open a receiver for `topic/subscription`. Queues: open a queue receiver.
- Note: Peek is read-only (safe); not all queues may have messages; handle empty pages gracefully.

Command Set (initial)
- `open <n>`: Open message number `n` from the current page in the external editor.
  - Editor resolution: `$VISUAL` → `$EDITOR` → OS default (`xdg-open`/`open`/`notepad`).
  - Temp file format: colorized preview for console; for editing, write a structured, commented text or raw body + metadata header (initially read-only display; editing semantics TBD).
- `h|help|?`: Show commands and usage snapshot.
- `q|quit|exit`: Quit.

Message Display & Colorization
- Apply the same theme system and color roles as AppConfig:
  - Control/UI, Numbers, Default, Letters/Keys (mapped onto message property keys/values).
- Suggested mapping:
  - Keys (property names): Letters/Keys color.
  - Values (string/number/datetime): Number/Default color as appropriate.
  - Body preview: Default, with special bytes escaped and limited width; show encoding (UTF-8/JSON/XML) when inferable.

Editing Semantics (Future)
- Out of scope for v0: editing and resubmitting messages. Later phases can allow cloning a message, editing properties/body, and sending to a target entity or dead-letter queue.

Project Layout (planned)
- Solution: `ServiceBusCli.sln`
- `src/ServiceBusCli.Core`: Domain models, paging, colorizer, table layout, command parser, ARM discovery abstractions.
- `src/ServiceBusCli`: Console app (UI, rendering, external editor, key handling, command loop).
- `tests/ServiceBusCli.Core.Tests`: Unit tests for Core helpers.
- `tests/ServiceBusCli.Tests`: App-level tests (parser, range mapping, editor/CLI plumbing).

Code Reuse Strategy (no changes to old app)
- Replicate the visual style by porting the minimal set of abstractions/ideas:
  - Theme presets and color roles.
  - Table layout and text truncation helpers.
  - Command parser patterns and key handling (PageUp/PageDown, resize refresh).
- Do not change `codex-appconfig/`; copy concepts and re-implement as needed with similar naming for familiarity.

Testing Plan (v0)
- Core unit tests (skeleton to start):
  - `TableLayout` sizing given column samples and terminal width.
  - `TextTruncation` ellipsis rules for narrow widths.
  - `CommandParser`: `open <n>`, `help`, `quit` parsing.
  - Paging math: next/prev sequence number windows, empty pages.
  - Colorizer: mapping property types → color roles.
- App tests (light):
  - Command dispatch for `open <n>` with a mocked repository and editor.
  - Resource mapping from ARM models → internal entity model.

Build/Run (planned)
- Build: `dotnet build ServiceBusCli.sln`
- Run (AAD + discovery): `dotnet run --project src/ServiceBusCli -- --auth device`
- Preselect entity:
  - Queue: `--namespace <ns-name> --queue <queue>`
  - Subscription: `--namespace <ns-name> --topic <topic> --subscription <sub>`
- Themes: `--theme default|mono|no-color|solarized`, `--no-color`.

Permissions & Environment
- ARM discovery requires Reader access to enumerate Service Bus namespaces and entities.
- Data plane requires `Azure Service Bus Data Receiver` (and later `Data Sender` for editing/publish).
- Environment variables honored:
  - `AZURE_TENANT_ID`: tenant; overrides default.
  - `SERVICEBUSCLI_THEME` and `SERVICEBUSCLI_NO_COLOR`: mirror CLI flags.

Risks & Mitigations
- Large namespaces/entities: cap list and page sizes; lazy-load; add per-page timeouts.
- Message body formats: detect UTF-8/JSON/XML heuristically; fall back to hex preview.
- Subscriptions vs queues: ensure consistent receiver setup; support DLQ later.
- Throttling: conservative retries on data-plane; cancellable operations; do not block UI.

Open Questions
- Editing: Is the first edit flow “clone and send to entity X”, or “unlock, modify in place” (not generally supported) or “dead-letter manipulation”?
- Filters: Should we add server-side filters (e.g., subject contains, time range) before v1?
- Selection semantics: Add an active row cursor for quick `open` without specifying `n`?

Milestones
1) v0.1: Auth + discovery UI; preselect via CLI; list messages with paging; `open <n>` in external editor; themes/colorization.
2) v0.2: Filters, better body preview (JSON pretty-print), DLQ peek.
3) v0.3: Editing workflow (clone/send), send to queue/subscription; export/import files.

How We’ll Keep Style Parity
- Mirror layout decisions (index column width, right-aligned page indicator, compact prompts).
- Re-implement theme presets and color roles 1:1.
- Port table measurement/truncation logic behaviorally (new code, matching outcomes).

Next Steps (for implementation)
1) Scaffold solution/projects and theme/table/colorizer helpers.
2) Implement ARM discovery (namespaces → queues/topics → subs) and selection UI.
3) Wire AAD auth options (modes, tenant) like AppConfig CLI.
4) Implement Service Bus peek paging and main list UI.
5) Add command loop with `open <n>` and external editor integration.
6) Add unit test skeletons and wire CI later.

