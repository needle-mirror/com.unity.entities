# Building ILSpy

Building this executable to be able to run cross-platform is a bit tricky.  Here is what worked for me:

1. Clone the [ILSpy repo](https://github.com/icsharpcode/ILSpy)
2. Open up the ILSpy.sln (in root)
3. Add the ICSharpCode.Decompiler.Console project to the solution
4. Change the framework to .Net Framework v.4.7.2 (you may have to edit some target files)
5. Turn off nuget package generation
6. Build release of the console application
7. Download [ILMerge](https://github.com/dotnet/ILMerge) and use the command line application to bundle the built exe and dlls into one executable (you will have to use the /ndebug option as it can't handle the .net framework)
8. Copy the resulting executable here

