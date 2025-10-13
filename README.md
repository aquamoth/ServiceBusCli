# Keyboard-first Azure Service Bus CLI

Cross-platform, keyboard-first console app to browse Azure Service Bus namespaces, entities, and messages. Matches the look and feel of the AppConfigCli TUI (header, paging list, bottom command line, themes, and colorization).

## License

ServiceBusCli is open source under the Apache 2.0 License.
This allows free use, modification, and redistribution, including for commercial purposes.
Corporations can safely adopt it without legal risk.
As always, while best efforts are made to avoid defects, you use this software at your own risk.

## Requirements

- .NET 9 SDK
- Azure permissions
  - ARM discovery: Reader (or higher) on the subscription/RG/namespace
  - Data plane (messages): Azure Service Bus Data Receiver (Listen) or Data Owner on the namespace or entity

## Build and Run

Using the solution:

```bash
dotnet restore ServiceBusCli.sln
dotnet build ServiceBusCli.sln
dotnet run --project src/ServiceBusCli/ServiceBusCli -- --auth device
```

Preselect an entity:

```bash
# Queue
 dotnet run --project src/ServiceBusCli/ServiceBusCli -- \
  --auth device \
  --namespace <ns-name-or-fqdn> \
  --queue <queue-name>

# Subscription
 dotnet run --project src/ServiceBusCli/ServiceBusCli -- \
  --auth device \
  --namespace <ns-name-or-fqdn> \
  --topic <topic-name> \
  --topic-subscription <subscription-name>
```

Themes:

```bash
--theme default|mono|no-color|solarized   # or use --no-color
```

## Usage

CLI options (current):

- `--subscription <azure-sub-id>`: Filter ARM discovery to a subscription
- `--namespace <name|fqdn>`: Preselect namespace (accepts FQDN like `foo.servicebus.windows.net`)
- `--queue <name>`: Preselect queue
- `--topic <name>` and `--topic-subscription <name>`: Preselect topic subscription
- `--auth <auto|device|browser|cli|vscode>`: Auth mode (default: auto)
- `--tenant <guid>`: Entra tenant ID
- `--theme <name>`: Theme preset (`default`, `mono`, `no-color`, `solarized`)
- `--no-color`: Disable color output

Keyboard actions:

- PageUp/PageDown: Navigate pages in all views
- ESC: Go back (Messages → Entities → Namespaces)
- Command line: Type commands at the bottom, press Enter to execute
- History: Up/Down cycles previous commands; draft preserved while navigating

Commands:

- `open <n>`: Open message number `n` from the current page in the external editor
- `reject <expr>`: Move messages from an active queue to its DLQ
  - `<expr>` supports single numbers, ranges (`514-590`), and comma-separated lists (`595,597,602-607`); applies to visible rows
- `resubmit <expr>`: From a queue’s DLQ view, clone messages back into the main queue and remove them from DLQ
  - Attempts SDK completion of the DLQ copy after clone-and-send; if not confirmed within bounds, tags the DLQ copy with `ResubmittedBy`/`ResubmittedAt`
- `delete <expr>`: From a queue’s DLQ view, complete (delete) DLQ messages
- `dlq` / `queue` (in Messages): Toggle between viewing a queue and its DLQ
- `session <text>`: Filter Messages view by `SessionId` prefix (repeat `session` with no text to clear)
- `help | h | ?`: Show commands and usage
- `quit | q | exit`: Quit

External editor resolution: `$VISUAL` → `$EDITOR` → OS default (`xdg-open` on Linux, `open -t` on macOS, `notepad` on Windows).

## Project Layout

- `ServiceBusCli.sln`: Solution
- `src/ServiceBusCli.Core`: Core domain (models, selection, theme helpers)
- `src/ServiceBusCli/ServiceBusCli`: Console app (TUI, input handling, editor integration)
- `tests/ServiceBusCli.Core.Tests`, `tests/ServiceBusCli.Tests`: Unit tests

## Implementation Notes

- Discovery uses Azure Resource Manager to enumerate namespaces and entities (queues, topic subscriptions)
- Messages are peeked via `ServiceBusReceiver.PeekMessagesAsync` for paged browsing without locks
- UI mirrors AppConfigCli: header with PAGE indicator, per-page dynamic widths, and colorized previews
- Command line editor supports in-place edits (cursor left/right/home/end, backspace/delete, ctrl-word nav) and history

### Environment

- `AZURE_TENANT_ID`: Entra tenant ID

## Status

- v0: AAD auth + ARM discovery, entity browsing, paged message listing, external editor open, themes/colorization
- DLQ workflows: peek, resubmit, delete; range/list expressions; AAD-only (no connection string required)
- Session support: shows `SessionId` when present; `session` prefix filter
- Future: additional filters, JSON pretty-print, broader clone/send workflows

## Contributing

Issues and PRs are welcome. Please open an issue to discuss larger changes.
