using BashCommandManager.Cli.Application;
using BashCommandManager.Cli.CommandLine;
using BashCommandManager.Cli.Infrastructure;
using BashCommandManager.Cli.Operations;
using BashCommandManager.Cli.Output;

namespace BashCommandManager.Cli;

/// <summary>CommandPath → 处理器的唯一映射表。</summary>
public static class CliDispatcher
{
    public static Task<CliResult> ExecuteAsync(
        ParsedCommand command,
        CliServices services,
        CliCommandStore? store,
        CommandResolver? resolver)
    {
        if (NeedsStore(command.Verb) && (store is null || resolver is null))
        {
            throw CliErrors.Internal($"命令 {command.CommandPath} 需要数据库，但会话未创建");
        }

        return command.CommandPath switch
        {
            "groups.list" => GroupCommands.ListAsync(command, store!),
            "groups.create" => GroupCommands.CreateAsync(command, store!, resolver!),
            "groups.rename" => GroupCommands.RenameAsync(command, store!, resolver!),
            "groups.delete" => GroupCommands.DeleteAsync(command, store!, resolver!),
            "commands.list" => CommandCommands.ListAsync(command, store!, resolver!),
            "commands.search" => CommandCommands.SearchAsync(command, store!, resolver!),
            "commands.show" => CommandCommands.ShowAsync(command, services, store!, resolver!),
            "commands.add" => CommandCommands.AddAsync(command, services, store!, resolver!),
            "commands.edit" => CommandCommands.EditAsync(command, services, store!, resolver!),
            "commands.delete" => CommandCommands.DeleteAsync(command, store!, resolver!),
            "commands.move" => CommandCommands.MoveAsync(command, store!, resolver!),
            "commands.frequent" => CommandCommands.FrequentAsync(command, store!),
            "commands.import" => CommandCommands.ImportAsync(command, services, store!, resolver!),
            "commands.export" => CommandCommands.ExportAsync(command, services, store!, resolver!),
            "run" => RunCommand.ExecuteAsync(command, services, store!, resolver!),
            "config.get" => ConfigCommand.GetAsync(command, services),
            _ => throw CliErrors.Usage($"命令尚未接线: {command.CommandPath}")
        };
    }

    private static bool NeedsStore(string verb) => verb is "groups" or "commands" or "run";
}
