# An introduction to `cljr`

Start by reading  [Deps and CLI Reference Rationale](https://clojure.org/reference/deps_and_cli).

The CLR port of [tools.deps](https://github.com/clojure/tools.deps) is currently located at [clr.tools.deps](https://github.com/clojure/clr.tools.deps).

This repo contains the .NET equivalent of the command line programs `cli` and `clojure`.    

## How it works

The main program follows the outline of the `cli` tool.
The first step is the parse the command-line arguments.

If the arg list contains `-h`, a help message will be printed.
This will give you the clues for other options.  (I need to edit the help text a bit to make it `cljr`-specific.)

At the moment, there is no distinction betwee `cli` and `clojure`.
(I don't see any difference in the JVM world either, despite what they say in the help text.)

```
...> Deps.Cljr.exe -h
Version: 1.0.0.0

You use the Clojure tools('clj' or 'clojure') to run Clojure programs
on the JVM, e.g.to start a REPL or invoke a specific function with data. The Clojure tools will configure the JVM process by defining a classpath (of desired libraries), an execution environment(JVM options) and specifying a main class and args.

Using a deps.edn file(or files), you tell Clojure where your source code resides and what libraries you need.Clojure will then calculate the full set of required libraries and a classpath, caching expensive parts of this process for better performance.

The internal steps of the Clojure tools, as well as the Clojure functions you intend to run, are parameterized by data structures, often maps. Shell command lines are not optimized for passing nested data, so instead you will put the data structures in your deps.edn file and refer to them on the command line via 'aliases' - keywords that name data structures.

'clj' and 'clojure' differ in that 'clj' has extra support for use as a REPL in a terminal, and should be preferred unless you don't want that support, then use 'clojure'.

Usage:

  Start a REPL   
      clj [clj-opt*] [-Aaliases]
  
  Exec fn(s) 
      clojure [clj-opt*] -X[aliases][a / fn *][kpath v]*
  
  Run main 
    clojure[clj-opt *] -M[aliases][init-opt *][main-opt][arg*]
 
  Run tool 
     clojure [clj-opt*] -T[name | aliases] a/fn[kpath v] kv-map?
  
  Prepare
     clojure[clj-opt*] -P[other exec opts]

exec-opts:
 -Aaliases Use concatenated aliases to modify classpath
 -X[aliases] Use concatenated aliases to modify classpath or supply exec fn/args
 -M[aliases] Use concatenated aliases to modify classpath or supply main opts
 -P Prepare deps - download libs, cache classpath, but don't exec

clj-opts:
 -Jopt Pass opt through in java_opts, ex: -J-Xmx512m
 -Sdeps EDN     Deps data to use as the last deps file to be merged
 -Spath Compute classpath and echo to stdout only
 -Stree Print dependency tree
 -Scp CP        Do NOT compute or cache classpath, use this one instead
 -Srepro Ignore the ~/.clojure/deps.edn config file
 -Sforce Force recomputation of the classpath(don't use the cache)
 -Sverbose Print important path info to console
 -Sdescribe Print environment and command parsing info as data
 -Sthreads Set specific number of download threads
 -Strace Write a trace.edn file that traces deps expansion
 --             Stop parsing dep options and pass remaining arguments to clojure.main
 --version Print the version to stdout and exit
 -version Print the version to stdout and exit

The following non-standard options are available only in deps.clj:

 -Sdeps-file Use this file instead of deps.edn
 -Scommand A custom command that will be invoked. Substitutions: { { classpath} }, {{main-opts
}}.

init - opt:
 -i, --init path Load a file or resource
 -e, --eval string   Eval exprs in string; print non-nil values
 --report target     Report uncaught exception to "file" (default), "stderr", or "none"

main-opt:
 -m, --main ns - name  Call the -main function from namespace w/args
 -r, --repl Run a repl
 path Run a script from a file or resource
 -                   Run a script from standard input
 -h, -?, --help Print this help message and exit

Programs provided by :deps alias:
 -X:deps mvn-pom Generate (or update) pom.xml with deps and paths
 -X:deps list              List full transitive deps set and licenses
 -X:deps tree              Print deps tree
 -X:deps find-versions Find available versions of a library
 -X:deps prep              Prepare all unprepped libs in the dep tree
 -X:deps mvn-install Install a maven jar to the local repository cache
 -X:deps git-resolve-tags Resolve git coord tags to shas and update deps.edn

For more info, see:
 https://clojure.org/guides/deps_and_cli
 https://clojure.org/reference/repl_and_main

```

- If the arg list contains `-h`, `-?`, or `--help`, a help message will be printed. (Above)
- If the arg list contains `-version` or `--version`, version info will be printed.

```
...> Deps.Cljr.exe -version
ClojureCLR CLI Version: 1.0.0.0
```

- If the arg list contains `-pom` or `-Jwhatever`, it will be ignored, other than printing a warning, "We are the CLR!".  Eventually we will come up with a substitute for the `-Jwhatever` that allows passing information to the CLR runtime. Maybe.  When I figure out what that might be.

- If the arg list containts `-Sverbose`, you will get extra information printed out, including information on installation and configuration directories that might be helpful in debugging.


After parsing the command line, the short-circuits help and version are checked for.    This followed by computing a bunch of environmental information:  the install, config and cache directories, the tools directory (including making sure there is a `tools.edn` file in there and copying a blank one if necessary).

Then the various aliases (`-X`, `-M`, `-T`, etc.) are computed and used to form the cache key, which is then hashed.  This determines the subdirectory in the cache file to look at and store cached info into.

You can see all this by running with `-Sverbose`:

```
> Deps.Cljr.exe -Sverbose
version      = 1.0.0.0
install_dir  = C:\work\clojure\deps.cljr\src\dotnet\Deps.Cljr\bin\Debug\net6.0\
config_dir   = C:\Users\dmill\.clojure
config_paths = C:\work\clojure\deps.cljr\src\dotnet\Deps.Cljr\bin\Debug\net6.0\deps.edn C:\Users\dmill\.clojure\deps.edn deps.edn
cache_dir    = C:\Users\dmill\.clojure\.cpcache
cp_file      = C:\Users\dmill\.clojure\.cpcache\D79F9C6847CA7B8A630F9F8E6C23BEE0.cp

Refreshing classpath
...
```

The string `D79F9C6847CA7B8A630F9F8E6C23BEE0` is the cache key hash.

The next step is to see if the classpath information is stale and refresh if necessary.  The classpath is computed by the ClojureCLR function  `clojure.tools.deps.script.make-classpath2`.  This is run by running a PowerShell script that starts up ClojureCLR and runs this function, with a bunch of arguments passed on the command line.

Here is what is executed:

```
run-clojure-main.ps1 -m clojure.tools.deps.script.make-classpath2 --config-user C:\Users\dmill\.clojure\deps.edn --config-project deps.edn --cp-file C:\Users\dmill\.clojure\.cpcache\D79F9C6847CA7B8A630F9F8E6C23BEE0.cp --jvm-file C:\Users\dmill\.clojure\.cpcache\D79F9C6847CA7B8A630F9F8E6C23BEE0.jvm --main-file C:\Users\dmill\.clojure\.cpcache\D79F9C6847CA7B8A630F9F8E6C23BEE0.main --manifest-file C:\Users\dmill\.clojure\.cpcache\D79F9C6847CA7B8A630F9F8E6C23BEE0.manifest
```

The script `run-clojure-main.ps1` is trivial:

```
clojure.main @args
```

Seriously, that's it.

You will note some oddities in the command line arguments, such as the `.jvm` file -- those will eventually be replaced.  The arguments passed to `clojure.tools.deps.script.make-classpath2` here are what that program is currently written to accept.

In order for this to work, we must have the `clr.tools.deps` code in the appropriate `bin` subdirectory.  This is a dependence that we must build in by hand -- we are building the tool that calculates dependencies and pulls in code, but there is no obvious way to have it bootstrap itself, other than to pull in the code as needed from being hardwired.  (to do -- for now, we just bring it in by hand.)
