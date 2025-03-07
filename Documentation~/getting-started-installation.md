# Installation

To use the Entities package, you must have a supported version of Unity installed. The Entities package is compatible with Unity 2022.3 and later.

Install the Entities package [through the Package Manager window](xref:um-upm-ui-actions). Use the `com.unity.entities` name, if you choose the [add the package by name](xref:upm-ui-quick) option.

## IDE support
The Entities package uses [Roslyn source generators](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview). Because of this, you should use an IDE that's compatible with source generators. Previous IDE versions might experience slow-downs or mark valid code as errors. The following IDEs are compatible with source generators:

* Visual Studio 2022+
* Rider 2021.3.3+

## Disable domain reloading

To get the best performance in your Entities project, disable Unity's [Domain Reload](xref:um-configurable-enter-play-mode) setting. For information on how to disable domain reloading, refer to the documentation on [Configure Play mode settings](xref:um-configurable-enter-play-mode).

For information about the effects of disabling domain reload, refer to the documentation on [Details of disabling domain and scene reload](xref:um-configurable-enter-play-mode-details).
