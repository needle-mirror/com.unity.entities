---
uid: ecs-overview
---
# Entities overview
The Entities package adds functionality to your Unity project that lets you use Unity's Entity Component System (ECS). The ECS system organizes your project in a data-oriented way, as opposed to the traditional object-oriented way.

## Entity Component System

ECS is the core of the Unity Data-Oriented Tech Stack (DOTS). As the name indicates, ECS has three principal parts:

* [Entities](ecs_entities.md): The entities, or things, that populate your application. An Entity has neither behavior nor data; instead, it identifies which pieces of data belong together.
* [Components](ecs_components.md): The data associated with Entities, but organized by the data itself rather than by Entity. This difference in organization is one of the key differences  between an object-oriented and a data-oriented design.
* [Systems](ecs_systems.md): The logic that transforms the component data from its current state to its next state. For example, a system might use an Entity's velocity multiplied by the time interval since the previous frame to update the positions of all moving Entities.

## Further information

* [Package installation and setup](install_setup.md)
* [Entities core concepts](ecs_core.md)
