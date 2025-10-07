using System.Text;

namespace ServiceBusCli;

// Minimal, testable line-editing engine with viewport and cursor
internal sealed class LineEditorEngine
{
    public StringBuilder Buffer { get; } = new StringBuilder();
    public int Cursor { get; private set; } = 0; // insertion index
    public int ScrollStart { get; private set; } = 0; // viewport start

    public void SetInitial(string? text)
    {
        Buffer.Clear();
        if (!string.IsNullOrEmpty(text)) Buffer.Append(text);
        Cursor = Buffer.Length;
        ScrollStart = 0;
    }

    public void Insert(char ch)
    {
        Buffer.Insert(Cursor, ch);
        Cursor++;
    }
    public void Backspace()
    {
        if (Cursor > 0) { Buffer.Remove(Cursor - 1, 1); Cursor--; }
    }
    public void Delete()
    {
        if (Cursor < Buffer.Length) { Buffer.Remove(Cursor, 1); }
    }
    public void Left() { if (Cursor > 0) Cursor--; }
    public void Right() { if (Cursor < Buffer.Length) Cursor++; }
    public void Home() { Cursor = 0; }
    public void End() { Cursor = Buffer.Length; }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

    public void CtrlWordLeft()
    {
        if (Cursor == 0) return;
        int i = Cursor - 1;
        while (i >= 0 && !IsWordChar(GetChar(i))) i--;
        while (i >= 0 && IsWordChar(GetChar(i))) i--;
        Cursor = Math.Max(0, i + 1);
    }
    public void CtrlWordRight()
    {
        int i = Cursor;
        while (i < Buffer.Length && !IsWordChar(GetChar(i))) i++;
        while (i < Buffer.Length && IsWordChar(GetChar(i))) i++;
        Cursor = i;
    }
    public void CtrlWordBackspace()
    {
        if (Cursor == 0) return;
        int start = Cursor - 1;
        while (start >= 0 && !IsWordChar(GetChar(start))) start--;
        while (start >= 0 && IsWordChar(GetChar(start))) start--;
        int from = Math.Max(0, start + 1);
        int len = Cursor - from;
        if (len > 0) { Buffer.Remove(from, len); Cursor = from; }
    }
    public void CtrlWordDelete()
    {
        int i = Cursor;
        while (i < Buffer.Length && !IsWordChar(GetChar(i))) i++;
        while (i < Buffer.Length && IsWordChar(GetChar(i))) i++;
        int len = Math.Max(0, i - Cursor);
        if (len > 0) Buffer.Remove(Cursor, len);
    }

    private char GetChar(int i) => Buffer[i];

    public void EnsureVisible(int contentWidth)
    {
        contentWidth = Math.Max(1, contentWidth);
        if (Cursor < ScrollStart) ScrollStart = Cursor;
        if (Cursor - ScrollStart >= contentWidth)
            ScrollStart = Math.Max(0, Cursor - contentWidth + 1);
    }

    public string GetView(int contentWidth)
    {
        contentWidth = Math.Max(1, contentWidth);
        int end = Math.Min(Buffer.Length, ScrollStart + contentWidth);
        string view = Buffer.ToString(ScrollStart, Math.Max(0, end - ScrollStart));
        // Add ellipses if scrolled
        if (ScrollStart > 0 && view.Length > 0)
            view = '…' + (view.Length > 1 ? view[1..] : string.Empty);
        if (end < Buffer.Length && view.Length > 0)
            view = (view.Length > 1 ? view[..^1] : string.Empty) + '…';
        return view;
    }
}

