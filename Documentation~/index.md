# Entities overview

The Entities package, part of Unity's Data-Oriented Technology Stack (DOTS), provides a data-oriented implementation of the Entity Component System (ECS) architecture.

See the [DOTS Guide and Samples](https://github.com/Unity-Technologies/EntityComponentSystemSamples) for introductory material, including tutorials, samples, and videos.

![](images/entities-splash-image.png)

## Package installation

To use the Entities package, you must have Unity version 2022.2.0b8 and later installed.

To install the package, open the Package Manager window (**Window &gt; Package Manager**) and perform one of the following options:

* [Add the package by its name](xref:upm-ui-quick)
* [Add the package from its Git URL](xref:upm-ui-giturl)

## Known issues

* Calling `SystemAPI` methods from static methods in a system causes the following runtime error:
    ```
    No suitable code replacement generated, this is either due to generators failing, or lack of support in your current context.
    ```
* Blob Assets don't support methods with yield return.

## Additional resources

* [Getting started](getting-started.md)
* [Upgrade guide](upgrade-guide.md)
* [What's new](whats-new.md)
* [ECS packages](ecs-packages.md)
