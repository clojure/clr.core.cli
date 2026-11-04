using Cljr;


// Initial args processing
// An error processing the args prints an error message and exit code 1
// Some values indicate early exit, e.g. -h, --help, -v, --version
// Some values cause warning messages to be printed

var cliArgs = CommandLineParser.Parse(args);

if (cliArgs.IsError)
{
    Util.Warn(cliArgs.ErrorMessage!);
    return 1;
}

if (cliArgs.Mode is EMode.Help)
{
    Util.PrintHelp();
    return 0;
}

if (cliArgs.Mode is EMode.Version)
{
    Util.PrintVersion();
    return 0;
}

if (cliArgs.HasFlag("pom"))
    Util.Warn("We are CLR!  We don't do -Spom");

if (cliArgs.JvmOpts.Count > 0)
    Util.Warn("We are CLR!  -Jjvm_opts aren't going to do you much good.");


// Figure out the environment of config files, etc.
// Create directories and files where needed.

EnvironmentManager mgr = new(cliArgs);

if (cliArgs.HasFlag("verbose"))
    Util.Print(mgr.GetVerboseMessage());

{
    var result = Processor.MaybeMakeClasspath(mgr);
    if (result.IsImmediateExit)
    {
        if (result.Exception is not null)
            Util.Warn($"Error creating classpath: {result.Exception.Message}");
        Environment.Exit(result.ExitCode);
    }
}

if (cliArgs.HasFlag("prep"))
{
    /* already done */
}
else if (cliArgs.HasFlag("pom"))
{
    // TODO -- are we doing this?
    //   & $JavaCmd -XX:-OmitStackTraceInFastThrow @CljJvmOpts -classpath $ToolsCp clojure.main -m clojure.tools.deps.script.generate-manifest2 --config-user $ConfigUser --config-project $ConfigProject --gen=pom @ToolsArgs
}
else if (cliArgs.HasFlag("path"))
{
    Util.Print(mgr.GetClasspath());
}
else if (cliArgs.HasFlag("describe"))
{
    Util.Print(mgr.GetDescribeMessage());
}
else if (cliArgs.HasFlag("tree"))
{
    /* already done */
}
else if (cliArgs.HasFlag("trace"))
{
    Util.Print("Wrote trace.edn");
}
else
{
    //if (Test - Path $JvmFile) {
    //    $JvmCacheOpts = @(Get - Content $JvmFile)

    if (cliArgs.Mode is EMode.Exec or EMode.Tool)
    {
        var result = Processor.RunExecOrToolProcess(mgr);
        if (result.IsImmediateExit)
        {
            if (result.Exception is not null)
                Util.Warn($"Error running exec or tool process: {result.Exception.Message}");
            Environment.Exit(result.ExitCode);
        }
    }

    else
    {
        var result = Processor.RunMain(mgr);
        if (result.IsImmediateExit)
        {
            if (result.Exception is not null)
                Util.Warn($"Error running main process: {result.Exception.Message}");
            Environment.Exit(result.ExitCode);
        }
    }
}

return 0;




