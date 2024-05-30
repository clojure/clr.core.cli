namespace Cljr.Tests;

public class ParsingTests
{
    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("-A:X:Y --help")]
    [InlineData("-Srepro --help")]
    public void HelpTests(string cli)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Mode == EMode.Help);
    }

    [Theory]
    [InlineData("-version")]
    [InlineData("--version")]
    [InlineData("-A:X:Y -version")]
    [InlineData("-Srepro --version")]
    public void VersionTests(string cli)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Mode == EMode.Version);
    }


    [Theory]
    [InlineData("describe")]
    [InlineData("force")]
    [InlineData("path")]
    [InlineData("pom")]
    [InlineData("repro")]
    [InlineData("trace")]
    [InlineData("tree")]
    [InlineData("verbose")]
    public void SFlagTests(string flag)
    {
        var cli = $"-S{flag} -X:A: B 12 13";
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.FlagCount == 1);
        Assert.True(items.HasFlag(flag));
    }


    //[InlineData("-P -X:A:B 12 13", CommandLineFlags.Prep)]
    [Fact]
    public void PFlagTests()
    {
        var cli = "-P -X:A:B 12 13";
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.FlagCount == 1);
        Assert.True(items.HasFlag("prep"));
    }

    [Theory]
    [InlineData("-Srepro -Sresolve-tags -X:A: B 12 13")]
    [InlineData("-Srepro -Othing -X:A: B 12 13")]
    [InlineData("-Srepro -Cthing -X:A: B 12 13")]
    [InlineData("-Srepro -Rthing -X:A: B 12 13")]
    public void DeprecatedOptionsTests(string cli)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var cmd = CommandLineParser.Parse(args);
        Assert.True(cmd.IsError);
        Assert.NotNull(cmd.ErrorMessage);
    }

    [Theory]
    [InlineData("-Srepro -Sdeps", "Invalid arguments")]
    [InlineData("-Srepro -Scp", "Invalid arguments")]
    [InlineData("-Srepro -Sthreads", "Invalid arguments")]
    [InlineData("-Srepro -Sthreads a", "Invalid argument")]
    [InlineData("-Srepro -Swhat! things", "Unknown option")]
    public void MissingOrBadArgumentsToOptions(string cli, string msgPrefix)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var cmd = CommandLineParser.Parse(args);
        Assert.True(cmd.IsError);
        Assert.NotNull(cmd.ErrorMessage);
        Assert.StartsWith(msgPrefix, cmd.ErrorMessage);
    }

    [Fact]
    public void StringArgumentsForOptionsTests()
    {
        string cli = "-Srepro -Sdeps ABC -Scp DEF -Sthreads 12 -X:A: B 12 13";
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Deps?.Equals("ABC"));
        Assert.True(items.ForceClasspath?.Equals("DEF"));
        Assert.True(items.Threads == 12);
    }

    [Fact]
    public void AOptionRequiresAnAliasTest()
    {
        string cli = "-Srepro -A  -X:A: B 12 13";
        string[] args = cli.Split(new char[] { ' ' });
        var cmd = CommandLineParser.Parse(args);
        Assert.True(cmd.IsError);
        Assert.NotNull(cmd.ErrorMessage);
        Assert.StartsWith("-A requires an alias", cmd.ErrorMessage);
    }

    [Theory]
    [InlineData("-Srepro -X:A: B 12 13", "B", "12", "13")]
    [InlineData("-Srepro -- B 12 13", "B", "12", "13")]
    public void ArgsGetPassedTests(string cli, params string[] expectedAargs)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var cmd = CommandLineParser.Parse(args);
        Assert.True(cmd.CommandArgs.Zip(expectedAargs.ToList()).All(x => x.First.Equals(x.Second)));
    }

    [Theory]
    [InlineData("-Srepro -X:A:B 12 13", EMode.Exec, ":A:B", "12", "13")]
    [InlineData("-Srepro -M:A:B 12 13", EMode.Main, ":A:B", "12", "13")]
    [InlineData("-Srepro -T:A:B 12 13", EMode.Tool, ":A:B", "12", "13")]
    [InlineData("-Srepro -- 12 13", EMode.Repl, null, "12", "13")]
    [InlineData("-Srepro 12 13", EMode.Repl, null, "12", "13")]
    public void CorrectCommandTypeCreated(string cli, EMode mode, string cmdAliases, params string[] expectedArgs)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Mode == mode);
        Assert.True(items.CommandArgs.Zip(expectedArgs.ToList()).All(x => x.First.Equals(x.Second)));

        if (cmdAliases != null)
            Assert.Equal(cmdAliases, items.CommandAliases[mode]);
        else
            Assert.False(items.CommandAliases.ContainsKey(mode));
    }

    [Fact]
    public void ToolWithAliasTest()
    {
        string cli = "-Srepro -T:A:B 12 13";
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Mode == EMode.Tool);
        Assert.Null(items.ToolName);
        Assert.Equal(":A:B", items.CommandAliases[EMode.Tool]);
    }


    [Fact]
    public void ToolWithToolNameTest()
    {
        string cli = "-Srepro -Tname 12 13";
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Mode == EMode.Tool);
        Assert.Equal("name", items.ToolName);
        Assert.False(items.CommandAliases.ContainsKey(EMode.Tool));
    }


    [Theory]
    [InlineData("-Srepro -X 12 13", EMode.Exec, "12", "13")]
    [InlineData("-Srepro -M 12 13", EMode.Main, "12", "13")]
    [InlineData("-Srepro -T 12 13", EMode.Tool, "12", "13")]
    public void CommandWithNoAliasTests(string cli, EMode mode, params string[] expectedArgs)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Mode == mode);
        Assert.False(items.CommandAliases.ContainsKey(mode));
        Assert.True(items.CommandArgs.Zip(expectedArgs.ToList()).All(x => x.First.Equals(x.Second)));
    }

    [Theory]
    [InlineData("-Srepro -A:A:B -A:C:D -X:A:B 12 13", EMode.Exec, ":A:B:C:D")]
    [InlineData("-Srepro -A:A:B -A:C:D -M:A:B 12 13", EMode.Main, ":A:B:C:D")]
    [InlineData("-Srepro -A:A:B -A:C:D -T:A:B 12 13", EMode.Tool, ":A:B:C:D")]
    [InlineData("-Srepro -A:A:B -A:C:D -- 12 13", EMode.Repl, ":A:B:C:D")]
    public void AArgPassesReplAliases(string cli, EMode mode, string replAliases)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Mode == mode);
        Assert.Equal(replAliases, items.CommandAliases[EMode.Repl]);
    }

    [Theory]
    [InlineData("-Srepro -A:A:B -A:C:D -X:")]
    [InlineData("-Srepro -A:A:B -A:C:D -M:")]
    [InlineData("-Srepro -A:A:B -A:C:D -T:")]
    [InlineData("-Srepro -A:A:B -A:C:D -A:")]
    public void PowerShellWorkaroundFailTests(string cli)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var cmd = CommandLineParser.Parse(args);
        Assert.True(cmd.IsError);
    }

    [Theory]
    [InlineData("-Srepro -X: A:B 12 13", EMode.Exec, ":A:B", "12", "13")]
    [InlineData("-Srepro -M: A:B 12 13", EMode.Main, ":A:B", "12", "13")]
    [InlineData("-Srepro -T: A:B 12 13", EMode.Tool, ":A:B", "12", "13")]
    [InlineData("-Srepro -A: A:B -- 12 13", EMode.Repl, ":A:B", "12", "13")]
    public void PowerShellWorkaroundSuccessTests(string cli, EMode mode, string cmdAliases, params string[] expectedArgs)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Mode == mode);
        Assert.True(items.CommandArgs.Zip(expectedArgs.ToList()).All(x => x.First.Equals(x.Second)));
        Assert.Equal(cmdAliases, items.CommandAliases[mode]);
    }

    [Theory]
    [InlineData("-Srepro -A: A:B -A: C:D -X:A:B 12 13", EMode.Exec, ":A:B:C:D")]
    [InlineData("-Srepro -A: A:B -A: C:D -M:A:B 12 13", EMode.Main, ":A:B:C:D")]
    [InlineData("-Srepro -A: A:B -A: C:D -T:A:B 12 13", EMode.Tool, ":A:B:C:D")]
    [InlineData("-Srepro -A: A:B -A: C:D -- 12 13", EMode.Repl, ":A:B:C:D")]
    public void PowerShellWorkaroundForASuccessTests(string cli, EMode mode, string replAliases)
    {
        string[] args = cli.Split(new char[] { ' ' });
        var items = CommandLineParser.Parse(args);
        Assert.True(items.Mode == mode);
        Assert.Equal(replAliases, items.CommandAliases[EMode.Repl]);
    }
}