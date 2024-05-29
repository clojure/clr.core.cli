# clr.core.cli

An implementation of the Clojure CLI tool and deps.edn tooling for ClojureCLR.

For ClojureCLR on .NET 6 and later.

## Releases

The current release is 0.0.1-alpha1.


## Installation

`cljr` is installed as a `dotnet tool`.


## An introduction to deps.edna and the CLI

You can start by reading these references:

- [Clojure CLI Reference](https://clojure.org/reference/clojure_cli)
    - Ignore the nstallation and usage sections; we'll cover this below.
- [deps.edn Reference](https://clojure.org/reference/deps_edn)


## Intallation

`cljr` is installed as a `dotnet tool`:

```
dotnet tool install asdfasdfasdfasdfasdf
```


You should see output similar to this:



You can now invoke `cljr` from the command line.

## Functionality

A significant portion of the functionality of the JVM version is available.
Some parts await development.  Some likly will never be implemented.

There are two ways to invoke the JVM tool: as 'clj' and as 'clojure'.  
As the docs state:  "In general, you can treat these interchangeably but `clj` includes `rlwrap` for extended keyboard editing, particularly useful with the REPL. 
This reference uses `clj` for REPL examples and `clojure` for non-REPL examples."

`cljr` does not have this distinction (for now).  There is just `cljr`.

The status of the primary commands and the command line options is detailed below.  
In general terms, tools and deps prep are not supported yet.  Anything related to Maven or JVM is not supported.
Most other things are ready for testing.


| Command     | Invocation | Implemented? |
|:------------|:-----------|:-------------|
| Start a repl (default) | `cljr [clj-opts] [-Aaliases]` |  Yes |
| Execute a function (-X) | `cljr [clj-opts] -Xaliases my/fn? [kpath v ... ] kv-map? `| Yes |
| Run a tool (-T)  | `cljr [clj-opts] -T[name\|aliases] my/fn [kpath v ...] kv-map?` | Not yet |
| Run a main namespace or script (-M) | `cljr [clj-opts] -M[aliases] [init-opts] [main-opts] [args]` | Yes |

The status of the options:

| Option | Description | Status |
|:-------|:------------|:-------|
|exec-opts:|
| -Aaliases   | Apply aliases | Supported |
| -X[aliases] | Invoke function, using aliases | Supported |
| -Ttoolname  | Invoke tool by local name | Supported |
| -T[aliases] | Invoke tool using aliases | Supported |
| -M[aliases] | Invoke clojure.main, using aliases | Supported |
| -P          | Prepare deps but don't exec | Not yet |
||
|clj-opts: |
| -Jopt       | Pass JVM option | Irrelevant |
| -Sdeps EDN  | Extra deps.edn data  | Supported |
| -Srepro     | Ignore user deps.edn file | Supported |
| -Spath      | Compute and echo classpath | Supported |
| -Stree      | Print dependency tree | Supported |
| -Scp CP     | Use this classpath, ignore deps.edn  | Supported |
| -Sforce     | Force classpath computation | Supported |
| -Sverbose   | Print path info | Supported |
| -Sdescribe  | Print env and command parsing info | Supported |
| -Sthreads   | Set specific number of download threads  | Not supported |
| -Strace     | Write dep expansion trace.edn | Supported |
| --version   | Print version to stdout and exit | Supported |
| -version    | Print version to stderr and exit | Supported |
| --help -h -? | Print this help message | Supported |
||
|Programs provided by :deps alias:
| -X:deps list          | Print deps list+licenses | Supported |
| -X:deps tree          | Print deps tree | Supported |
| -X:deps find-versions | Find available lib versions | Supported |
| -X:deps prep          | Prepare all unprepped libs in deps | Not supported (yet) |
| -X:deps mvn-pom       | Generate pom.xml for deps.edn | Not supported |
| -X:deps mvn-install   | Install maven jar to local repo | Not supported |

## deps.edn features

Most of the features of `deps.edn` files are supported.

-- We support git lib and local directory dependencies only.  We do not support maven dependencie or local jars.  (Though we should have a discussion about the latter.)
-- We are still thinking about how nuget might come into play.  It's complicated.
-- We do not yet have support for tool publishing and preparation steps.  See below.

Generally, one puts a `deps.edn` file in the root directory of your code.  However, for projects that support multiple Clojure ports (JVM, CLR, Clojurescript), this will not always work.  
For example, the JVM version may want to use Maven repos for dependencies, which the CLR version does not support.

However, we cannot just read-conditionalization the `deps.edn` file because read-conditionalization is not supported in EDN files.
The workaround is that `cljr` will look for a file named `deps-clr.edn` first, then look for `deps.edn`.  This provides an override that `cljr` can see and the JVM tools will ignore.


## Running tests

If you would like to run tests of your project from the command line, you can take advantage of a port of [cognitect-labs/test-runner](https://github.com/cognitect-labs/test-runner) available at [dmiller/test-runner](https://github.com/dmiller/test-runner).
(If you want to look at the code, look in branch `clr-port`.)

Here is a sample `deps.edn` file set up to use this port of `test-runner`:

```Clojure
{:paths ["src/main/clojure"]
 :deps
 {io.github.clojure/clr.data.generators {:git/tag "v1.1.0" :git/sha "d25d292"}}

 :aliases
 {:test
  {:extra-paths ["src/test/clojure"]
   :extra-deps {io.github.dmiller/test-runner {:git/tag "v0.5.1clr" :git/sha "814e06f"}}
   :exec-fn cognitect.test-runner.api/test
   :exec-args {:dirs ["src/test/clojure"]}}}}
```

You would invoke test running via

```Clojure
cljr -X:test
```

## Notes

This is an alpha release.  Have at it.

We need some design thinking around tools and prepping.  Most of the library support on the JVM side are very specific to the JVM world:  Maven, jars, Java-ish things.
What is needed to support robust program development and installation in the CLR world.   Please weigh in.


# Copyright and License

Copyright © Rich Hickey and contributors

All rights reserved. The use and
distribution terms for this software are covered by the
[Eclipse Public License 1.0] which can be found in the file
epl-v10.html at the root of this distribution. By using this software
in any fashion, you are agreeing to be bound by the terms of this
license. You must not remove this notice, or any other, from this
software.

[Eclipse Public License 1.0]: https://opensource.org/license/epl-1-0