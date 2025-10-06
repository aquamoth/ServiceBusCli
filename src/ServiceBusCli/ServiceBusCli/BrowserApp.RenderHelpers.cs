using System;
using System.Linq;
using System.Collections.Generic;
using ServiceBusCli.Core;

namespace ServiceBusCli;

public sealed partial class BrowserApp
{
    private void RenderMessagesTable(IReadOnlyList<MessageRow> messages)
    {
        var totalWidth = Console.WindowWidth;
        var (seqW, enqW, idW, subjW, previewW, showSubject) = ComputeMessageColumnWidths(messages, totalWidth);
        // Header
        ColorConsole.Write("Seq".PadRight(seqW), _theme.Number);
        Console.Write(" ");
        ColorConsole.Write("Enqueued".PadRight(enqW), _theme.Control);
        Console.Write(" ");
        ColorConsole.Write("MessageId".PadRight(idW), _theme.Letters);
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
            var subjStr = (m.Subject ?? string.Empty);
            var prevStr = m.Preview;

            ColorConsole.Write(Align(TextTruncation.Truncate(seqStr, seqW), seqW, padLeft: false), _theme.Number);
            Console.Write(" ");
            Console.Write(Align(TextTruncation.Truncate(enqStr, enqW), enqW, padLeft: false));
            Console.Write(" ");
            ColorConsole.Write(Align(TextTruncation.Truncate(idStr, idW), idW, padLeft: false), _theme.Letters);
            if (showSubject)
            {
                Console.Write(" ");
                Console.Write(Align(TextTruncation.Truncate(subjStr, subjW), subjW, padLeft: false));
            }
            Console.Write(" ");
            Console.Write(Align(TextTruncation.Truncate(prevStr, previewW), previewW, padLeft: false));
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

    private static (int seqW, int enqW, int idW, int subjW, int previewW, bool showSubject)
        ComputeMessageColumnWidths(IReadOnlyList<MessageRow> messages, int totalWidth)
    {
        int minPreview = 10;
        int spaceBetween = 1;
        int seqW = Math.Max(3, messages.Count == 0 ? 3 : messages.Max(m => m.SequenceNumber.ToString().Length));
        int enqW = messages.Any(m => m.Enqueued.HasValue) ? 20 : 0; // 'u' format ~20
        int idW = 8; // short id view
        bool showSubject = messages.Any(m => !string.IsNullOrEmpty(m.Subject));
        int subjW = showSubject ? Math.Min(24, Math.Max(8, messages.Max(m => (m.Subject ?? string.Empty).Length))) : 0;

        int columns = 1; // preview only
        if (seqW > 0) columns++;
        if (enqW > 0) columns++;
        if (idW > 0) columns++;
        if (showSubject) columns++;

        int spaces = (columns - 1) * spaceBetween;
        int occupied = seqW + enqW + idW + subjW + spaces;
        int previewW = Math.Max(minPreview, totalWidth - occupied);

        if (previewW < minPreview)
        {
            int deficit = minPreview - previewW;
            if (showSubject && subjW > 0)
            {
                int reduce = Math.Min(deficit, subjW);
                subjW -= reduce; deficit -= reduce;
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

        return (seqW, enqW, idW, subjW, previewW, showSubject);
    }
}

