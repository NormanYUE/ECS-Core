# Changelog

All notable changes to Ember Core Components.

## [2.1.6] — Fix the out-of-bounds Clear introduced in 2.1.5

### Fixed

- **Calling `Clear()` on an uncreated `m_Infos` threw, so 2.1.5 logged an error every tick and
  motion still did not work.**

  `NativeList<T>.Clear()` starts with `AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion`, but
  the list is only created by the first `FillInfos` call — the default handle throws
  "has been deallocated, it is not allowed to access it" and the code never reaches the creation
  step. Guarded with `IsCreated`.

## [2.1.5] — Fix MovementSystem silently dropping motion

### Fixed

- **`MovementSystem` never cleared its chunk-info list `m_Infos`, which silently disabled motion.**

  `FillInfos` appends (the linear and angular queries share one list), while the job is scheduled
  as `Schedule(total, …)` and therefore only processes `[0, total)`. With the list never cleared,
  `total` is the number of chunks **added this tick** but the job consumed the **head** of the list
  — the chunk pointers captured on the very first tick. Two consequences:

  - The current tick's chunks never reached the job, so entities that appeared later (units spawned
    mid-battle, for example) never moved, while velocity and animation state stayed correct — the
    symptom is a walk animation playing in place.
  - `chunkCount` entries leaked every tick (the list only ever grew), and once chunks migrated or
    were recycled those pointers dangled, letting the job write into unrelated memory.

  Fix: `m_Infos.Clear()` before gathering, inside `OnTick`.

## [2.1.4] — Package repository moved to ECS-Core.git

### Changed

- **The package repository moved from `Ember-Core.git` to `ECS-Core.git`, and the repository
  root is now the UPM package root.**

  The source repository and the package repository merged into one: the source repository went
  public and is now itself the UPM package, while the separate package repository was deleted.
  One codebase, one history — no sync step, no chance of the copy drifting.

  The layout follows Unity's package conventions:

  | Before | After | Why |
  | --- | --- | --- |
  | `src/` | `Runtime/` | pairs with `Ember.Core.Runtime.asmdef` |
  | `libs/` | `Libs~/` | the `~` suffix makes Unity ignore it; otherwise `Unity.Burst.dll` is imported as a package plugin and collides with `com.unity.burst` |
  | `tests/` | `tests/` (now with an asmdef) | `defineConstraints = UNITY_INCLUDE_TESTS`, otherwise the tests get compiled into the package |
  | the package repo's `package.json` / README / CHANGELOG / LICENSE | repository root | — |

  Every asset now has a `.meta`. Unity **silently ignores** assets without one inside an immutable
  package folder, emitting only a console warning — a source package has hundreds of assets, and
  each missing `.meta` is a missing file.

  **Consumers must update their manifest URL**:

  ```
  - https://github.com/NormanYUE/Ember-Core.git
  + https://github.com/NormanYUE/ECS-Core.git
  ```

  Delete `Library/PackageCache` afterwards, or UPM will not re-resolve.

- Dependencies raised: com.ember.ecs 1.13.0

## [2.1.3] — Ships as a source package (no more precompiled DLL)

### Changed

- **The package now carries `Runtime/**/*.cs` instead of `Runtime/*.dll`.**

  **Why**: `[BurstCompile]` inside a precompiled DLL is never processed by Unity's Burst ILPP —
  that pipeline only runs over assemblies Unity itself compiles. In practice 22 jobs (Collision 19,
  Navigation 1, Core 2) reported `not a known Burst entry point` at runtime and fell back to the
  managed path. As a source package Unity compiles them, and the pipeline applies.

  **Also fixed by this**:
  - Unity-side symbols such as `#if ENABLE_UNITY_COLLECTIONS_CHECKS` are **actually defined** when
    compiling source, so a block inside such a guard can no longer be dead code (that is exactly how
    `Ember.Collision` shipped a broken pipeline)
  - No more hand-maintained `.meta` files in the package repo (the root cause of the 0.3.1 / 0.3.2
    incidents)
  - Consumers get readable, debuggable, steppable source

  **No consumer change required**: the package URL is unchanged and so is the public API.

- `Runtime/Ember.core.Runtime.asmdef` now points `precompiledReferences` at **`Ember.dll`**
  (the framework still ships precompiled) instead of its own DLL. `references` explicitly lists the
  Unity-side dependencies (`Unity.Collections` / `Unity.Mathematics` / `Unity.Burst`) and the
  upstream Ember assemblies.
- Added the missing `com.unity.collections` dependency — a DLL package compiled against dotnet-side
  `libs/Unity.Collections.dll` never had to declare it; a source package must.
- Dependencies raised: com.ember.ecs 1.13.0

### Notes

- Sync tool: `Ember.Framework/tools/deploy-package-sources.py` (source repo → package repo, with
  deterministic `.meta` generation). See the root `CLAUDE.md` for the release flow.
- Not verified: Unity runtime. The cross-package precompiled reference (`Ember.dll`) in the asmdef
  still needs to resolve inside Unity.

## [2.1.2] — Depends on Ember 1.13.0

### Changed
- Dependency `com.ember.ecs` raised from 1.12.0 to 1.13.0. No source change in this package
  (`SpatialTree`'s buffers always set their length via `ClearBuffer` + `EnsureLength`, so it never
  hit the zero-length `CreateBuffer` trap); the bump exists so the exact-version chain stays
  consistent — UPM resolves exact versions, and a downstream package asking for 1.13.0 cannot be
  satisfied by a package still pinned to 1.12.0.

## [2.1.1] — Fix: unregistered component type when systems are constructed before the World

### Fixed
- **Registering `SpatialSystemGroup` in the documented assembly order no longer throws
  `unregistered component type`.**

  `SystemTicker.Register` **constructs systems immediately**, and in the documented example that happens
  before `ECSManager.Start()` — and therefore before the `World` exists. Component type registration,
  however, only happened inside the `World` constructor. Any system whose field initializer builds an
  `EntityQuery` (`EntityQuery.With<T>()` / `ComponentMask.With<T>()` read `ComponentTypeRegistry` on the
  spot) therefore threw on the first run in a fresh AppDomain.

  Affected: `SpatialSetupSystem`, `PresentationCommandSystem`, `VisibilityApplySystem`, `MovementSystem`.
  Their query fields lost the initializer and `readonly` and are now built in `OnCreate()`, which
  `SystemTicker.Init` calls after the `World` is constructed; `BuildAccess` runs right after, and
  `DeclareAccess` does not depend on those fields.

### Changed
- Dependency `com.ember.ecs` raised from 1.11.0 to 1.12.0 (the framework adds
  `World.EnsureComponentTypesRegistered()` and makes `Register` / `ApplyProfile` trigger registration
  before constructing systems).

### Notes
- Both sides changed deliberately: the framework covers the `SystemTicker` assembly path, and the
  package stops depending on a global side effect at construction time. No path that constructs systems
  before a `World` (editor tooling, pure-logic tests without a World) can hit this any more.

## [2.1.0] — Motion Integration System

### Added
- **`MovementSystem` (Burst job)**: integrates `LinearVelocity` / `AngularVelocity` into `LocalTransform`.
  Position: `Position += LinearVelocity.Value * dt`. Rotation: integrated as an axis-angle delta and
  renormalized after the quaternion multiply. Both queries (linear / angular) carry `None<Static>` and
  `None<Disabled>`, so excluded entities are never integrated.
  - Zero steady-state GC: chunk column pointers are collected into a reused list that only grows when the
    chunk count grows; the self-scheduled job is `Complete()`d inside `OnTick`, honoring the framework rule
    that jobs must not be suspended across ticks.
- **`MotionSystemGroup`**: register the whole motion step with one line,
  `manager.GetTicker(idx).Register<MotionSystemGroup>()`.

### Changed
- The time source is the `WorldTime` singleton when present (honoring `TimeScale`), and falls back to the
  current tick's delta otherwise — projects that never maintain `WorldTime` no longer see motion silently
  stop.
- Dependency `com.ember.ecs` raised from 1.10.1 to 1.11.0 (`SpatialTreeView` now grows via a single
  `World.ResizeBuffer` call instead of an `AddBufferElement` loop).
- Test suite grew to 62 tests: 25 pass on CLI; 37 native-container tests run in the Unity Test Runner.

## [2.0.0] — Spatial Tree Storage Migrated to World-Managed Buffers (Dispose-free)

### Breaking
- **`SpatialTree` no longer owns native containers and no longer exposes `Dispose()` / `Initialize(Allocator)`.**
  Tree data now lives in World-managed buffers (auto-freed on `World.Dispose()`); the component itself keeps
  only scalar state and buffer handles.
  - Old: `ref var tree = ref world.GetComponent<SpatialTree>(owner); ... tree.Dispose();`
  - New: `if (world.TryGetSpatialTree(out var tree)) { tree.QuerySphere(center, r, ref buffer); }`
    Nothing to release; reclaimed with `ECSManager.Dispose()`.
- All tree operations now go through `SpatialTreeView` (`world.GetSpatialTree()` / `world.TryGetSpatialTree`):
  `Insert` / `Remove` / `Update` / `QueryAABB` / `QuerySphere` / `BeginTick` / `EndTick` / `Clear`.
- The entity map switched from `NativeParallelHashMap` to a direct-index sparse map (addressed by `Entity.Index`
  with in-element version validation for slot reuse) — O(1) lookup at a lower constant.

### Added
- `SpatialTreeView` (operation view) and `SpatialTreeExtensions` (`GetSpatialTree` / `TryGetSpatialTree` / `EnsureSpatialTree`).
- New entity-slot-reuse guard test (stale index + new version must not hit an old mapping).

### Fixed
- Fixed a double decrement of element counts in `Subdivide` (decremented once by `Unlink` and again manually),
  which skewed leaf-capacity checks and empty-block collapse.

### Changed
- `SpatialIndexSystem` no longer needs `Allocator.Persistent` initialization; the tree is created on first tick.
- Test suite grew to 52 tests: 25 pass on CLI; 27 native-container tests run in the Unity Test Runner.

## [1.0.0] — Spatial Index, Frustum Culling & GameObject Presentation

### Breaking
- Removed static helpers `LocalTransform.Identity` / `LocalTransform.FromPosition` / `LocalToWorld.Identity` / `LocalToWorld.Compose` (no-statics rule). Migration: construct directly via `new LocalTransform(position, quaternion.identity, 1f)`; combine hierarchies with `math.mul(parent.Value, local.ToMatrix())`.

### Added
- **Spatial index**: `BoundingVolume` / `WorldBounds` components; `SpatialTree` as a fully unmanaged singleton component (unified quadtree/octree, backed by `NativeList`/`NativeParallelHashMap`, zero steady-state GC); `QueryAABB`/`QuerySphere` fill a caller-provided `NativeList<Entity>`; vanished entities are removed by mark-and-sweep (one frame latency); `SpatialIndexConfig` singleton configures dimension/root extent/depth/capacity.
- **Frustum culling**: `CameraFrustum` singleton (written per frame by bridge code), `VisibilityState` (bit0 = current frame, bit1 = previous frame, with `EnteredView`/`ExitedView` edge properties), `InView` tag (added/removed on edges only); `FrustumMath` pure math (Gribb-Hartmann plane extraction, sphere/AABB tests, world-bounds transform); `WorldBoundsSystem` and `FrustumCullingSystem` run as Burst jobs.
- **System groups**: `SpatialSystemGroup` wires the entire spatial/culling pipeline in one registration (setup → world bounds → culling → tag apply → spatial index).
- **GameObject presentation**: `PresentationPrefab` (prefab id) / `PresentationLink` / `PresentationCommands` singleton command channel; viewport edges drive Spawn/Despawn, destroyed entities are reclaimed via mark-and-sweep (one frame latency); `PresentationSyncSystem` (Burst job) writes TRS sync slots in parallel; the managed `GameObjectPresentation` bridge drains commands and applies transforms in batch via `TransformAccessArray` + `IJobParallelForTransform` (Burst); `IGameObjectPool` allows injecting a business-side pool, with `GameObjectPool` as the default implementation (bucketed stacks + Prewarm).
- **Explicit Burst policy**: assembly-level `EmberJobCompilationMode.Burst`; added `com.unity.burst` 1.8.13 package dependency.

### Changed
- Test suite grew to 51 tests: 25 pass on CLI; 26 tests requiring Unity native containers/engine APIs run in the Unity Test Runner.

## [0.1.0] — Initial Release

### Added
- **Spatial components**: `LocalTransform` (position/rotation/uniform scale with math helpers such as `Identity`, `ToMatrix`, `TransformPoint`) and `LocalToWorld` (world matrix with `Compose` hierarchy combination and axis accessors).
- **Motion components**: `LinearVelocity` (meters/second) and `AngularVelocity` (axis-angle vector, radians/second).
- **Timing components**: `Lifetime` (remaining seconds), `Age` (elapsed seconds), and the `WorldTime` singleton (`TimeScale` / `DeltaTime` / `UnscaledDeltaTime` / `ElapsedTime` / `FrameCount`).
- **State tags**: `Disabled` / `Static` / `Prefab` tag components with query conventions (`None<Disabled>`, etc.).
- **Random component**: `GlobalRandom` singleton, a deterministic random source based on `Unity.Mathematics.Random`.
- All components are unmanaged structs compatible with Burst-compiled job systems and are registered automatically by the Ember source generator.
- NUnit test project: 11 pure math/determinism tests plus 5 World integration tests requiring Unity native containers (auto-skipped on CLI, executed in the Unity Test Runner).
