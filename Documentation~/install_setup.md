---
uid: install-setup
---

# Entities installation and setup

When you set up an Entities project, there are additional steps you must follow. This page contains information on the packages included in the Entities release, and how to install them.

## Unity Editor version

You must use Unity Editor versions 2020.3.30+ or 2021.3.4+ with entities 0.51.

## IDE support
Entities 0.51 uses the [Microsoft Source Generator](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) feature for its code generation. Because of this, you should use an IDE that's compatible with source generators. Previous IDE versions might experience slow-downs or mark valid code as errors. The following IDEs are compatible with source generators:

* Visual Studio 2022+
* Rider 2021.3.3+

## Package installation

The Entities package isn't listed in the Package Manager, even if you've enabled the **Preview Packages** setting. You can use the following ways to install the Entities package:

* Use **Add package from git URL** under the **+** menu at the top left of the package manager to add packages either by name (such as `com.unity.entities`), or by Git URL (but this option isn't available for DOTS packages). If you want to use a Git URL instead of just a name in the Package Manager, you must have the git command line tools installed.
* Directly edit the `Packages\manifest.json` file in the Unity project. You must add both the package name and its version to the file, which you can find by looking at the documentation of each package (such as `"com.unity.entities" : "x.x.x-preview.x"`).

For more information, see the documentation on [Installing hidden packages](https://docs.unity3d.com/Packages/Installation/manual/index.html).

## Entities 0.51 packages

The following table lists the ECS-based packages that have been tested together with Unity 2020.3.30+ and 2021.3.4+:

| **Package name** | **Version number** |
|---|---|
|Entities (this package)|0.51.0|
|[Hybrid Renderer](https://docs.unity3d.com/Packages/com.unity.rendering.hybrid@latest)|0.51.0|
|[Netcode](https://docs.unity3d.com/Packages/com.unity.netcode@latest)|	0.51.0|
|[Physics](https://docs.unity3d.com/Packages/com.unity.physics@latest)|	0.51.0|

The following packages are also automatically included when installing the Entities package:

|**Package name**|**Version number**|
|---|---|
|[Burst](https://docs.unity3d.com/Packages/com.unity.burst@latest)|	1.6.4|
|[Collections](https://docs.unity3d.com/Packages/com.unity.collections@latest)|	1.2.3|
|[Jobs](https://docs.unity3d.com/Packages/com.unity.jobs@latest)|0.51.0|
|[Mathematics](https://docs.unity3d.com/Packages/com.unity.mathematics@latest)|	1.2.5|

## IDE support
Entities 0.51 uses the [Microsoft Source Generator](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) feature for its code generation. Because of this, you should use an IDE that's compatible with source generators. Previous IDE versions might experience slow-downs or mark valid code as errors. The following IDEs are compatible with source generators:

* Visual Studio 2022+
* Rider 2021.3.3+

## Building your application

To build your application, you must install the [Entities Platforms package](https://docs.unity3d.com/Packages/com.unity.platforms@latest). For more information on how to build your application, see the documentation on [Building an Entities project](ecs_building_projects.md).

## Supported platforms

The following platforms are supported build targets for Entities projects:

* **Mobile:** Android, iOS **Note:** Hybrid Renderer only supports Vulkan on Android in 0.50 and 0.51.
* **Desktop:** Windows, macOS, Linux
* **Consoles:** Xbox (One, Series), Playstation (4, 5)

## Domain Reload setting

To get the best performance in your Entities project, you should disable Unity's [Domain Reload](https://docs.unity3d.com/Manual/ConfigurableEnterPlayMode.html) setting. To do this, go to **Edit &gt; Project Settings &gt; Editor** menu, and enable the **Enter Play Mode Options** setting, but leave the **Reload Domain** and **Reload Scene** boxes disabled.

> [!NOTE]
> If you disable **Domain Reloads** [be mindful of your use of static fields and static event handlers](https://docs.unity3d.com/Manual/DomainReloading.html).
