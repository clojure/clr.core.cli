# Preparing a distribution

## Dependencies

Clojure.Cljr depends on several ClojureCLR libraries.  There are not pulled in as NuGet packages, but are included in the distribution zip files.  The libraries are:

- clr.tools.gitlibs
- clr.tools.deps
- clr.tools.deps.cli

If there have been any changes to these libraries, the source files need to be copied into the project in the appropriate directories.

## Set the version

Edit Cljr.csproj and set the version number in the `Version` property.  This is the version number that will be used in the NuGet package.

## Build and test

There is a test project, Cljr.Tests. This mostly tests parsing.

To really test, set the command line parameters for debugging.  Good luck.

Beyond that, the best test is to use the tool.  I typically do the build, which creates the package.
Then go into the `nupkg` directory and install the package with 

```
dotnet tool install -g --add-source . Clojure.Cljr` --version 0.1.0-alphaX
```

whatever version is current.  Then go into a few of the other libraries and run `cljr -X:test`.

## Publish

```
dotnet nuget push Clojure.Cljr.WHATEVER -s nuget.org
```

