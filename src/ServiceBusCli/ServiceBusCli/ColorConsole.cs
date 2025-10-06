using System;

namespace ServiceBusCli;

public static class ColorConsole
{
    public static void Write(string text, ConsoleColor color)
    {
        var old = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            Console.Write(text);
        }
        finally
        {
            Console.ForegroundColor = old;
        }
    }
}

