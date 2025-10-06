using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using ServiceBusCli.Core;

namespace ServiceBusCli;

public sealed partial class BrowserApp
{
    private void RenderMessagesTable(IReadOnlyList<MessageRow> messages)
    {
        var totalWidth = Console.WindowWidth;
        bool showSubject = messages.Any(m => !string.IsNullOrEmpty(m.Subject));
        bool showSession = messages.Any(m => !string.IsNullOrEmpty(m.SessionId));
        var (seqW, enqW, idW, sessW, subjW, previewW) = ComputeMessageColumnWidths(messages, totalWidth, showSubject, showSession);
        // Header
        ColorConsole.Write("Seq".PadRight(seqW), _theme.Number);
        Console.Write(" ");
        ColorConsole.Write("Enqueued".PadRight(enqW), _theme.Control);
        Console.Write(" ");
        ColorConsole.Write("MessageId".PadRight(idW), _theme.Letters);
        if (showSession)
        {
            Console.Write(" ");
            ColorConsole.Write("Session".PadRight(sessW), _theme.Control);
        }
        if (showSubject)
        {
            Console.Write(" ");
            ColorConsole.Write("Subject".PadRight(subjW), _theme.Control);
        }
        Console.Write(" ");
        ColorConsole.Write("Preview".PadRight(previewW), _theme.Control);
        Console.WriteLine();

        for (int i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            var seqStr = m.SequenceNumber.ToString();
            var enqStr = (m.Enqueued?.ToString("u") ?? string.Empty);
            var idStr = (m.MessageId ?? string.Empty);
            if (idStr.Length > 8) idStr = idStr.Substring(0, 8);
            var sessStr = (m.SessionId ?? string.Empty);
            if (sessStr.Length > 8) sessStr = sessStr.Substring(0, 8);
            var subjStr = (m.Subject ?? string.Empty);
            var prevStr = m.Preview;

            ColorConsole.Write(Align(TextTruncation.Truncate(seqStr, seqW), seqW, padLeft: false), _theme.Number);
            Console.Write(" ");
            Console.Write(Align(TextTruncation.Truncate(enqStr, enqW), enqW, padLeft: false));
            Console.Write(" ");
            ColorConsole.Write(Align(TextTruncation.Truncate(idStr, idW), idW, padLeft: false), _theme.Letters);
            if (showSession)
            {
                Console.Write(" ");
                Console.Write(Align(TextTruncation.Truncate(sessStr, sessW), sessW, padLeft: false));
            }
            if (showSubject)
            {
                Console.Write(" ");
                Console.Write(Align(TextTruncation.Truncate(subjStr, subjW), subjW, padLeft: false));
            }
            Console.Write(" ");
            WriteColorized(prevStr, previewW);
            Console.WriteLine();
        }
    }

    private static string Align(string text, int width, bool padLeft)
    {
        if (width <= 0) return string.Empty;
        if (text.Length >= width) return text;
        var pad = new string(' ', width - text.Length);
        return padLeft ? pad + text : text + pad;
    }

    private static (int seqW, int enqW, int idW, int sessW, int subjW, int previewW)
        ComputeMessageColumnWidths(IReadOnlyList<MessageRow> messages, int totalWidth, bool showSubject, bool showSession)
    {
        int minPreview = 10;
        int spaceBetween = 1;
        int seqW = Math.Max(3, messages.Count == 0 ? 3 : messages.Max(m => m.SequenceNumber.ToString().Length));
        int enqW = messages.Any(m => m.Enqueued.HasValue) ? Math.Max(12, Math.Min(20, messages.Max(m => (m.Enqueued?.ToString("u") ?? string.Empty).Length))) : 0;
        int idW = 8;
        int sessW = showSession ? 8 : 0;
        int subjW = showSubject ? Math.Max(8, messages.Max(m => (m.Subject ?? string.Empty).Length)) : 0;

        int columns = 1;
        if (seqW > 0) columns++;
        if (enqW > 0) columns++;
        if (idW > 0) columns++;
        if (sessW > 0) columns++;
        if (showSubject) columns++;

        int spaces = (columns - 1) * spaceBetween;
        int occupied = seqW + enqW + idW + sessW + subjW + spaces;
        int previewW = Math.Max(minPreview, totalWidth - occupied);

        if (previewW < minPreview)
        {
            int deficit = minPreview - previewW;
            if (showSubject && subjW > 0)
            {
                int reduce = Math.Min(deficit, subjW);
                subjW -= reduce; deficit -= reduce;
            }
            if (deficit > 0 && sessW > 0)
            {
                int reduce = Math.Min(deficit, Math.Max(0, sessW - 4));
                sessW -= reduce; deficit -= reduce;
            }
            if (deficit > 0 && enqW > 12)
            {
                int reduce = Math.Min(deficit, enqW - 12);
                enqW -= reduce; deficit -= reduce;
            }
            if (deficit > 0 && seqW > 3)
            {
                int reduce = Math.Min(deficit, seqW - 3);
                seqW -= reduce; deficit -= reduce;
            }
            previewW = minPreview;
        }

        return (seqW, enqW, idW, sessW, subjW, previewW);
    }

    private void WriteColorized(string text, int width)
    {
        if (width <= 0) return;
        var t = TextTruncation.Truncate(text ?? string.Empty, width);
        ConsoleColor current = Console.ForegroundColor;
        ConsoleColor? active = null;
        var sb = new StringBuilder();

        ConsoleColor ColorFor(char ch)
        {
            if (char.IsControl(ch)) return _theme.Control;
            if (char.IsDigit(ch)) return _theme.Number;
            if (char.IsLetter(ch)) return _theme.Letters;
            return _theme.Default;
        }

        foreach (var ch in t)
        {
            var col = ColorFor(ch);
            if (active == null) { active = col; }
            if (col != active)
            {
                ColorConsole.Write(sb.ToString(), active.Value);
                sb.Clear();
                active = col;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0 && active != null)
        {
            ColorConsole.Write(sb.ToString(), active.Value);
        }
        var pad = width - t.Length;
        if (pad > 0) Console.Write(new string(' ', pad));
    }
}
