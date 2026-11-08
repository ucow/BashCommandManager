using BashCommandManager.Cli;
using System.Text;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        return CliHost.Run(args);
    }
}
