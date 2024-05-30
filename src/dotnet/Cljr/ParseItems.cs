namespace Cljr;

public enum EMode { Version, Help, Repl, Tool, Exec, Main }

public class ParseItems
{
    readonly HashSet<string> Flags = new();
    public string? Deps { get; set; } = null;
    public string? ForceClasspath { get; set; } = null;
    public int Threads { get; set; } = 0;
    public string? ToolName { get; set; } = null;
    public List<string> JvmOpts { get; } = new();
    public List<string> CommandArgs { get; set; } = new();
    public EMode Mode { get; set; } = EMode.Repl;
    public bool IsError { get; set; } = false;
    public string? ErrorMessage { get; set; } = null;
    public Dictionary<EMode, string> CommandAliases { get; } = new();

    public ParseItems SetError(string message)
    {
        IsError = true;
        ErrorMessage = message;
        return this;
    }

    public void AddReplAliases(string aliases)
    {
        var currValue = CommandAliases.TryGetValue(EMode.Repl, out var currVal) ? currVal : "";
        CommandAliases[EMode.Repl] = currValue + aliases;
    }

    public void SetCommandAliases(EMode mode, string? alias)
    {
        if (alias is not null)
            CommandAliases[mode] = alias;
    }

    public string GetCommandAlias(EMode mode)
    {
        if (CommandAliases.TryGetValue(mode, out var alias))
            return alias;
        else
            return string.Empty;
    }

    public bool TryGetCommandAlias(EMode mode, out string alias)
    {
        return CommandAliases.TryGetValue(mode, value: out alias);
    }


    public void AddFlag(string flag)
    {
        Flags.Add(flag);
    }

    public bool HasFlag(string flag)
    {
        return Flags.Contains(flag);
    }

    public int FlagCount => Flags.Count;
}