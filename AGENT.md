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

Recent Changes (2025-10-06 .. 2025-10-08)
- Sorting + selection: Namespaces sorted by Name, Entities sorted by Path. Selection indices map to this order; added unit tests to prove it.
- Width logic improvements:
  - Namespaces: Use natural max widths; if content fits, do not expand (leave right side empty). If not, shrink Sub→ResourceGroup→Namespace; Sub prints full when there is space, else short 8-char.
  - Entities: Use natural Path/Status widths; shrink only when necessary; leave right side empty if content fits.
  - Messages: Keep sequence-left minimal padding, short 8-char MessageId, optional Subject (omitted if all empty), per-character colored Preview, whitespace squashing in Preview, no overflow.
- Session support: If entity requires sessions (or inferred from visible messages), a `Session` column (short 8-char) is shown after `MessageId`.
- Navigation: ESC goes back using a generic view stack (Namespaces ← Entities ← Messages), restoring paging and selection state.
- Stability: When switching namespaces/entities, receivers/clients are reset to avoid cross-namespace reuse (fixes 404/$management on prior namespace).

- Session filtering and DLQ workflows:
  - Session prefix filter: `session <text>` filters Messages view by SessionId prefix (case-insensitive); `session` clears the filter.
  - Message details now include `SessionId` when present.
  - Reject/resubmit/delete now accept flexible sequence expressions: single numbers, ranges (e.g., `514-590`), and comma-separated lists (e.g., `595,597,602-607`). Operations apply only to visible rows (respecting filters).
  - Resubmit from DLQ: clones to active queue and attempts to complete the DLQ copy using SDK (AAD); if completion cannot be confirmed in the bounded window, tags the DLQ copy with `ResubmittedBy`/`ResubmittedAt`.
  - Delete from DLQ: completes the message using SDK (AAD). Range delete supported.
  - After resubmit/delete, the DLQ view refreshes.

- Connection string removed:
  - Eliminated the need for `SERVICEBUS_CONNECTION_STRING` and AMQP SAS; all operations use AAD (Azure.Identity) via the Azure SDK.
  - Command-line `--connection-string` option and AMQP readiness checks removed.

DLQ Operations (Current)
- Use `Azure.Messaging.ServiceBus` with AAD to peek/receive/complete DLQ messages (no session links on sub-queues).
- Session DLQ: handled without session receivers by bounded browsing and matching on `SessionId` and `SequenceNumber`.
- Resubmit range flows attempt SDK completion of the DLQ copy after clone-and-send; fallback tagging provides traceability.

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
- Environment variables: `AZURE_TENANT_ID` (tenant). No connection string is required.

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
  - Optional SAS fallback (low priority): consider `--connection-string` only if AAD cannot be used, but target is AAD-first.
- Tables
  - Apply full AppConfig-style table measurement to Namespaces/Entities (cursor/resize handling) and consider adding filter/search.
- Navigation
  - Breadcrumbs or quick jump keys; improve PageUp fallback logic (compute previous pages even without history) and test.
- Tests
  - Width logic tests for Namespaces/Entities/Messages; PageUp fallback; ESC stack behavior.

## Known Issues (2025-10-15)

- AMQP with AAD (CBS/JWT) for session DLQ completion/delete intermittently fails or times out in a real namespace.
  - Latest observed logs:
    - Resubmit start targets=118 entity=intreceiver-submit mode=DeadLetter sessionEnabled=True
    - AMQP(AAD) DLQ completion attempt host=sh-dev-bus.servicebus.windows.net queue=intreceiver-submit session=15970 seq=118
    - After ~11s: AMQP DLQ completion result=False; UI shows “Resubmitted 0 message(s), 1 DLQ completions failed.”
  - Current implementation snapshot:
    - AMQP connection via SASL-ANONYMOUS using AmqpNetLite; CBS put-token to `$cbs` with AAD token (scope `https://servicebus.azure.net/.default`).
    - Audience: `amqp://{host}/{queue}/$DeadLetterQueue`.
    - Token types attempted: `servicebus.windows.net:jwt`, then `jwt` (short wait each).
    - Waits for CBS `status-code == 200` and correlation-id matching the put-token message-id; includes `expiration`.
    - On success, opens receiver to `{queue}/$DeadLetterQueue`, browses to the target by SessionId/SequenceNumber, and Accepts it.
    - SAS fallback remains available if provided.
  - Recently fixed:
    - Added credit on CBS receiver (`SetCredit(5)`), preventing hangs due to no credit.
    - Normalized host in logs; correlation-id check; dual token-type attempt.
  - Hypotheses to test next:
    1) Audience format: try `sb://{host}/{entity}` and root `amqp://{host}/` if entity audience fails.
    2) CBS diagnostics: log `status-description` and non-200 codes; confirm correlation-id echoes request id.
    3) Token claims/scope: verify identity has sufficient data-plane rights; consider token refresh immediately before CBS.
    4) DLQ browse/settle: validate Accept semantics on DLQ under AAD; adjust to explicit disposition if required.
    5) Timeouts/credit: extend CBS wait slightly; ensure adequate DLQ receiver credit.
  - Proposed next steps:
    - Add verbose CBS diagnostics (audience, token type, status-code/description, correlation-id, elapsed, timeout vs. failure).
    - Implement audience fallback order and a CLI flag to force a specific audience for debugging.
    - Add a limited CBS retry before declaring failure.
    - Keep SAS fallback behind a flag for reliability while iterating.
