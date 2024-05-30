namespace Cljr;


public static class CommandLineParser
{
    static readonly List<string> DeprecatedPrefixes = new() { "-R", "-C", "-O" };
    static bool StartsWithDeprecatedPrefix(string arg) => DeprecatedPrefixes.Any(p => arg.StartsWith(p));
    static string? NonBlank(string s) => string.IsNullOrEmpty(s) ? null : s;
    static string ExtractAlias(string s) => s[2..];
    static List<string> GetArgs(int i, string[] args) => args.Skip(i).ToList();

    public static ParseItems Parse(string[] args)
    {
        ParseItems items = new();

        int i = 0;
        while (i < args.Length)
        {
            var arg = args[i++];

            // PowerShell workaround
            if (Program.IsWindows)
            {
                switch (arg)
                {
                    case "-M:":
                    case "-X:":
                    case "-T:":
                    case "-A:":
                        if (i >= args.Length)
                            return items.SetError($"Invalid arguments, no value following {arg}.");
                        else
                            arg += args[i++];
                        break;
                }
            }

            if (StartsWithDeprecatedPrefix(arg))
                return items.SetError($"{arg[..2]} is no longer supported, use -A with repl, -M for main, -X for exec, -T for tool");

            if (arg == "-Sresolve-tags")
                return items.SetError("Option changed, use: clj -X:deps git-resolve-tags");


            if (arg == "-version" || arg == "--version")
            {
                items.Mode = EMode.Version;
                return items;
            }

            if (arg == "-h" || arg == "--help" || arg == "-?")
            {
                items.Mode = EMode.Help;
                return items;
            }

            if (arg.StartsWith("-J"))
            {
                items.JvmOpts.Add(ExtractAlias(arg));
                continue;
            }

            if (arg == "-P")
            {
                items.AddFlag("prep");
                continue;
            }

            if (arg.StartsWith("-S"))
            {
                var flag = ExtractAlias(arg);
                switch (flag)
                {
                    case "pom":
                    case "path":
                    case "tree":
                    case "repro":
                    case "force":
                    case "verbose":
                    case "describe":
                    case "trace":
                        items.AddFlag(flag);
                        break;

                    case "deps":
                        if (i >= args.Length)
                            return items.SetError($"Invalid arguments, no value following {arg}.");
                        items.Deps = args[i++];
                        break;

                    case "cp":
                        if (i >= args.Length)
                            return items.SetError($"Invalid arguments, no value following {arg}.");
                        items.ForceClasspath = args[i++];
                        break;

                    case "threads":
                        if (i >= args.Length)
                            return items.SetError($"Invalid arguments, no value following {arg}.");
                        if (Int32.TryParse(args[i++], out var numThreads))
                            items.Threads = numThreads;
                        else
                            return items.SetError($"Invalid argument, non-integer following {arg}");
                        break;

                    default:
                        return items.SetError($"Unknown option: {arg}");
                }
                continue;
            }

            if (arg == "-A")
            {
                return items.SetError("-A requires an alias");
            }

            if (arg.StartsWith("-A"))
            {
                items.AddReplAliases(ExtractAlias(arg));
                continue;
            }

            if (arg.StartsWith("-M"))
            {
                items.Mode = EMode.Main;
                items.SetCommandAliases(EMode.Main, NonBlank(ExtractAlias(arg)));
                items.CommandArgs = GetArgs(i, args);
                return items;
            }

            if (arg.StartsWith("-X"))
            {
                items.Mode = EMode.Exec;
                items.SetCommandAliases(EMode.Exec, NonBlank(ExtractAlias(arg)));
                items.CommandArgs = GetArgs(i, args);
                return items;
            }

            if (arg.StartsWith("-T:"))
            {
                items.Mode = EMode.Tool;
                items.SetCommandAliases(EMode.Tool, NonBlank(ExtractAlias(arg)));
                items.CommandArgs = GetArgs(i, args);
                return items;
            }

            if (arg.StartsWith("-T"))
            {
                items.Mode = EMode.Tool;
                items.ToolName = NonBlank(ExtractAlias(arg));
                items.CommandArgs = GetArgs(i, args);
                return items;
            }

            if (arg == "--")
            {
                items.CommandArgs = GetArgs(i, args);
                return items;
            }

            items.CommandArgs.Add(arg);
            items.CommandArgs.AddRange(GetArgs(i, args));
            return items;
        }

        return items;
    }

}
