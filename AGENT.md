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

High-Level Flow (v0) — Implemented So Far
1) Auth & discovery (AAD): Enumerate Service Bus namespaces via ARM. Handles `--subscription <azure-sub-id>` to scope discovery and skips unauthorized subs.
2) Preselection via CLI: `--namespace`, `--queue` or `--topic --topic-subscription` jump directly into deeper views if resolvable.
3) TUI browser:
   - Namespaces view: paged list with global numbering; type number+Enter to select.
   - Entities view: queues + topic subscriptions with Status, Total, Active, DLQ counts; global numbering; number+Enter to select.
   - Messages view: peek active messages; PageDown forward pagination and PageUp across cached pages; footer command line.
4) Unauthorized handling: if peeking messages is unauthorized, show a persistent footer status and stay in Entities view.
5) Theming and colorization basics are in place (same palette roles as AppConfig).

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

CLI Options (current)
- `--subscription <azure-sub-id>`: Azure subscription filter for ARM discovery.
- `--namespace <name|fqdn>`: Namespace to preselect (accepts FQDN like `sh-dev-bus.servicebus.windows.net`).
- `--queue <name>`: Preselect a queue in the selected namespace.
- `--topic <name>` and `--topic-subscription <name>`: Preselect a topic subscription.
- `--auth <auto|device|browser|cli|vscode>`: Auth mode (default: auto).
- `--tenant <guid>`: Entra tenant ID.
- `--theme <name>`: Theme preset (`default`, `mono`, `no-color`, `solarized`).
- `--no-color`: Disable color output.

UI/UX Parity with AppConfig CLI (current)
- Header: Title with right-aligned `PAGE x/y`, selection context below.
- Lists: Global row numbering across pages; non-wrapping; dynamic sizing. Entities show columns: `#`, `Kind`, `Path`, `Status`, `Total`, `Active`, `DLQ`.
- Paging: `PageUp`/`PageDown` on all views (messages use forward peek; PageUp navigates cached pages).
- Footer: Command prompt; persistent status line for errors (e.g., unauthorized).
- Themes: Same palette roles; more colorization parity to follow.

Message Retrieval & Paging
- Uses `ServiceBusReceiver.PeekMessagesAsync` to avoid locks. Maintains next sequence number to continue forward. Empty pages handled gracefully.
- Queues use `CreateReceiver(queue)`. Subscriptions use `CreateReceiver(topic, subscription)`.

Recent Changes (2025-10-06)
- Sorting + selection: Namespaces sorted by Name, Entities sorted by Path. Selection indices map to this order; added unit tests to prove it.
- Width logic improvements:
  - Namespaces: Use natural max widths; if content fits, do not expand (leave right side empty). If not, shrink Sub→ResourceGroup→Namespace; Sub prints full when there is space, else short 8-char.
  - Entities: Use natural Path/Status widths; shrink only when necessary; leave right side empty if content fits.
  - Messages: Keep sequence-left minimal padding, short 8-char MessageId, optional Subject (omitted if all empty), per-character colored Preview, whitespace squashing in Preview, no overflow.
- Session support: If entity requires sessions (or inferred from visible messages), a `Session` column (short 8-char) is shown after `MessageId`.
- Navigation: ESC goes back using a generic view stack (Namespaces ← Entities ← Messages), restoring paging and selection state.
- Stability: When switching namespaces/entities, receivers/clients are reset to avoid cross-namespace reuse (fixes 404/$management on prior namespace).

AMQP Integration (Planned)
- Add `ServiceBusCli.Amqp` project to encapsulate raw AMQP operations where SDK is missing features (e.g., DLQ session completion).
- Initial target: DLQ session resubmit/complete path
  - Use CBS auth via `SERVICEBUS_CONNECTION_STRING` (SAS) to keep the first cut simple.
  - Open receiver to `sb://<ns>/<queue>/$DeadLetterQueue` with `com.microsoft:session-filter`.
  - Bounded receive (page-only) up to the target; complete the target, abandon others.
- Optional (future): AAD token via CBS using the app credential.
- Fallback behavior when AMQP is unavailable/failing: always resubmit (clone/send) and tag DLQ copy (non-session via SDK; for session, tag when AMQP path succeeds, otherwise leave a clear status).

Unit Tests
- SelectionHelper ensures selection respects sorted order.
  - Namespaces: `alpha`, `beta`, `gamma` returned for 1..3.
  - Entities: sorted by Path; `queueA` before `queueB`.

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

Project Layout
- Solution: `ServiceBusCli.sln`
- `src/ServiceBusCli.Core`: Domain (models, theme, parser, table, discovery, entities lister).
- `src/ServiceBusCli`: Console app (BrowserApp TUI, CLI wiring).
- `tests/ServiceBusCli.Core.Tests`, `tests/ServiceBusCli.Tests`: Unit tests (parser, truncation, sanity).

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
- ARM discovery: Reader access (or higher) on subscription/RG/namespace. Owner on ARM does not confer data-plane rights.
- Data plane (messages): requires `Azure Service Bus Data Receiver` (Listen) at namespace or entity scope; `Data Owner` grants Manage+Send+Listen.
- Environment variables: `AZURE_TENANT_ID` (tenant), and future `SERVICEBUS_CONNECTION_STRING` for SAS fallback (see Next Steps).
  - `SERVICEBUS_CONNECTION_STRING`: Enables AMQP DLQ session operations (CBS SAS) and may be needed for experimental features.

Risks & Mitigations
- Large namespaces/entities: cap list and page sizes; lazy-load; add per-page timeouts.
- Message body formats: detect UTF-8/JSON/XML heuristically; fall back to hex preview.
- Subscriptions vs queues: ensure consistent receiver setup; support DLQ later.
- Throttling: conservative retries on data-plane; cancellable operations; do not block UI.

Open Questions
- Editing flow: clone-and-send vs DLQ flows; in-place edits not generally supported.
- Filters: add server-side filter options (subject contains, time range)?
- Selection: add active row cursor for quick selection without entering numbers?

Milestones
1) v0.1: Auth + discovery UI; preselect via CLI; list messages with paging; `open <n>` in external editor; themes/colorization.
2) v0.2: Filters, better body preview (JSON pretty-print), DLQ peek.
3) v0.3: Editing workflow (clone/send), send to queue/subscription; export/import files.

How We’ll Keep Style Parity
- Mirror layout decisions (index column width, right-aligned page indicator, compact prompts).
- Re-implement theme presets and color roles 1:1.
- Port table measurement/truncation logic behaviorally (new code, matching outcomes).

Next Steps (implementation)
- Messages
  - Minimize Enqueued/numeric widths based on visible rows (“only use needed”), like other pages.
  - Add `goto <seq>` (jump without opening), and a footer hint.
  - Editor aliases (`o <seq>`) and more structured views (JSON/YAML) parity with AppConfigCli.
- Header/context
  - Colorize and dynamically size the context line under the title (Namespace/Entity), mirroring AppConfigCli.
- Discovery/auth
  - SAS fallback: add `--connection-string` and env support; optional `--use-root-sas` (explicit) to fetch RootManageSharedAccessKey via ARM for Owners without data-plane RBAC.
- Tables
  - Apply full AppConfig-style table measurement to Namespaces/Entities (cursor/resize handling) and consider adding filter/search.
- Navigation
  - Breadcrumbs or quick jump keys; improve PageUp fallback logic (compute previous pages even without history) and test.
- Tests
  - Width logic tests for Namespaces/Entities/Messages; PageUp fallback; ESC stack behavior.
