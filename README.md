# Introduction
VsixUpdater is a tool to update VSIX packages to version 3 for Visual Studio 2017 compatibility - but unlike the VSSDK Nuget package it can be used with older versions of Visual Studio. As building with older versions is often required for backward compatibility reasons, this tool can be used to avoid multiple build setups to maintain compatibility from Visual Studio 2012 to 2017.

# Usage and best practices
VsixUpdater works as a custom MSBuild build task for VSIX projects. After adding the NuGet package to a VSIX project VsixUpdater will run after each build. It will scan the output folder for VSIX files, and update each of them.

The update will perform the follwing:
- Adds the proper installation targets for Visual Studio 2017 in the .vsixmanifest file.
- Adds the prerequisities section in the .vsixmanifest file.
- Adds the manifest.json and the catalog.json files required by VSIX V3.
- Compresses all parts of the package which are not already on maximum compression to reduce VSIX file size.

This tool will NOT unzip the VSIX file to a temporary directory, it will rather edit it in memory, then write it back to the original file. This ensures high performance on IO constrained systems.

It is recommended that you still set the prerequisities section as MSDN describes, as otherwise important components might be missing from the target machine. If you add the section beforehand, VsixUpdater will not touch it.

I did only test the tool on a handful of packages, it is possible that it breaks with some configurations, in this case I suggest you to raise an issue and/or download the sourcecode and debug it. If you raise an issue, make sure to reference the nuget package you have issues with if possible.

# Building the source
This tool is very simple and it does not require any special SDK to be installed, it should build with Visual Studio 2015 or later out of the box. On earlier versions of Visual Studio add the Microsoft.Net.Compilers NuGet package to support new C# features used in the code and it should build just as well.
