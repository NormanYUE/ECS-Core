# Ember Core Components Manual

[中文](README.md)

Foundational component library for the Ember ECS framework. Provides spatial transforms,
motion, timing, state tags, and deterministic random as unmanaged structs stored directly in
chunk columns and compatible with Burst-compiled job systems. Components are registered
automatically at compile time by the Ember source generator — no manual registration.

## Installation

This package depends on `com.ember.ecs` (the Ember ECS framework). Install both via the
Unity Package Manager:

1. Open your project's `Packages/manifest.json`
2. Add the dependencies (use the actual released versions):

```json
{
  "dependencies": {
    "com.ember.ecs": "1.10.1",
    "com.ember.core": "0.1.0"
  }
}
```

3. When installing via Git URL: UPM does not resolve transitive Git dependencies, so make sure
   `com.ember.ecs` is declared explicitly in the manifest.
4. Save and let Unity resolve; `Unity.Mathematics` is pulled in transitively.

Requires Unity 2022.3 or newer.

## Component Reference

All components live in the `Ember.Core` namespace. Entity hierarchy (parent/child) is provided
by the framework's built-in `ParentComponent` / `ChildEntity` and is intentionally not
duplicated here.

### Spatial

| Component | Kind | Description |
|---|---|---|
| `LocalTransform` | Data | Local transform: `Position` (float3) / `Rotation` (quaternion) / `Scale` (uniform float). Acts as world transform when the entity has no parent. Helpers: `ToMatrix()`, `TransformPoint`, `InverseTransformPoint`, `TransformDirection` |
| `LocalToWorld` | Data | Local-to-world `float4x4` matrix. Helpers: `Position` / `Right` / `Up` / `Forward`, `TransformPoint` |

### Motion

| Component | Kind | Description |
|---|---|---|
| `LinearVelocity` | Data | Linear velocity `Value` (float3) in meters/second, integrated into `LocalTransform.Position` by movement systems |
| `AngularVelocity` | Data | Angular velocity `Value` (float3) as an axis-angle vector: direction = rotation axis, magnitude = radians/second |

The companion `MovementSystem` (Burst job) integrates both into `LocalTransform`: position via
`Position += LinearVelocity.Value * dt`, rotation integrated as an axis-angle delta and renormalized after
the quaternion multiply. Both queries carry `None<Static>` and `None<Disabled>`, so excluded entities are
never integrated.

### Timing

| Component | Kind | Description |
|---|---|---|
| `Lifetime` | Data | Remaining seconds `Remaining`, counted down by a lifetime system; entity is destroyed at zero (bullets, VFX, etc.) |
| `Age` | Data | Elapsed seconds `Elapsed` since creation; useful for fade in/out interpolation and growth stages |
| `WorldTime` | Singleton | Global time state: `TimeScale` / `DeltaTime` / `UnscaledDeltaTime` / `ElapsedTime` / `FrameCount`. Read it from jobs or anywhere `SystemContext.DeltaTime` is unavailable. This library does not advance it — the consumer writes it each frame; `MovementSystem` prefers it (honoring `TimeScale`) and falls back to the current tick's delta when the singleton is absent |

### State Tags

| Component | Kind | Description |
|---|---|---|
| `Disabled` | Tag | Disabled marker. Convention: systems add `None<Disabled>` to skip disabled entities; cheaper than destruction for temporary deactivation |
| `Static` | Tag | Static marker. Convention: movement/transform systems add `None<Static>` to skip immovable entities, saving integration and matrix recomputation |
| `Prefab` | Tag | Prefab template marker. Template entities stay out of regular logic (queries add `None<Prefab>`) and serve only as instantiation sources |

### Random

| Component | Kind | Description |
|---|---|---|
| `GlobalRandom` | Singleton | Global deterministic random source (`Unity.Mathematics.Random`). It is a value type whose state advances per draw — access it by `ref`. For parallel random streams, give each entity its own random component instead of sharing this singleton |

### Spatial Index

| Type | Description |
|---|---|
| `BoundingVolume` | Local-space AABB (`Center`/`Extents`); input for indexing and culling |
| `WorldBounds` | World-space AABB, recomputed every frame by `WorldBoundsSystem` (Burst job) |
| `SpatialIndexConfig` | Singleton: dimension (QuadXY/QuadXZ/Octree), root extent, max depth, node capacity; built-in defaults apply when absent |
| `SpatialTree` | Singleton: blittable spatial tree (unified quadtree/octree, zero steady-state GC), scalars + World-managed buffer handles — **no Dispose needed**, auto-freed with the World |
| `SpatialTreeView` | Operation view: get it via `world.GetSpatialTree()` / `world.TryGetSpatialTree(out view)`; `Insert`/`Remove`/`Update`/`QueryAABB`/`QuerySphere` (fill a caller-provided `NativeList<Entity>`) |

### Frustum Culling

| Type | Description |
|---|---|
| `CameraFrustum` | Singleton: six normalized frustum planes, written per frame by camera bridge code |
| `VisibilityState` | Visibility byte (bit0 = in view this frame, bit1 = last frame; `EnteredView`/`ExitedView` edge properties) |
| `InView` | Tag for filtering visible entities; added/removed on edges only |
| `FrustumMath` | Pure math utilities: `FromViewProjection` plane extraction, sphere/AABB frustum tests (Burst-compatible) |

### GameObject Presentation

| Type | Description |
|---|---|
| `PresentationPrefab` | Prefab id (int); managed references cannot live in components, so ids map to prefabs in the pool registry |
| `PresentationLink` | Entity ↔ sync slot (managed by the bridge — do not add/remove manually) |
| `PresentationCommands` | Singleton: Spawn/Despawn command queue + TRS sync arrays (native container channel) |
| `IGameObjectPool` / `GameObjectPool` | Pool interface (inject your own implementation) / default Core pool (bucketed stacks + `Prewarm`) |
| `GameObjectPresentation` | Managed bridge: call `Sync()` after every tick to spawn/recycle/batch-write transforms; call `Dispose()` before teardown |

The pipelines ship as three system groups:

```csharp
manager.GetTicker(fixedIdx).Register<MotionSystemGroup>();         // velocity → LocalTransform — register before anything that reads LocalTransform
manager.GetTicker(updateIdx).Register<SpatialSystemGroup>();       // setup → bounds → culling → tags → spatial index
manager.GetTicker(updateIdx).Register<PresentationSystemGroup>();  // presentation (Burst sync) — register after culling
```

## Usage Example

```csharp
using Ember;
using Ember.Core;
using Unity.Mathematics;

public sealed class SpawnSystem : SystemBase
{
    protected override void DeclareAccess(AccessBuilder access)
    {
        access.Write<LocalTransform>().Write<LinearVelocity>().Write<Lifetime>()
              .StructuralChanges();
    }

    protected override void OnTick(SystemContext ctx)
    {
        var entity = ctx.ECB.CreateEntity(new ComponentMask());
        ctx.ECB.AddComponent(entity, new LocalTransform(new float3(0f, 1f, 0f), quaternion.identity, 1f));
        ctx.ECB.AddComponent(entity, new LinearVelocity(new float3(0f, 0f, 5f)));
        ctx.ECB.AddComponent(entity, new Lifetime(3f));
    }
}
```

Reading the global time singleton:

```csharp
var owner = World.GetOrCreateSingleton<WorldTime>();
ref var time = ref World.GetComponent<WorldTime>(owner);
time.ElapsedTime += time.DeltaTime;
```

Movement query convention that skips disabled and static entities:

```csharp
var query = EntityQuery.With<LocalTransform, LinearVelocity>().None<Disabled, Static>();
```

GameObject presentation wiring (spawn/recycle/sync are viewport-driven):

```csharp
// Startup
var pool = new GameObjectPool();
pool.RegisterPrefab(1, enemyPrefabGo);
pool.Prewarm(1, 64);                       // optional: pre-warm to remove instantiation spikes
var presentation = new GameObjectPresentation(world, pool);

// Entity side: attach a prefab id to join the presentation layer
world.AddComponent(entity, new PresentationPrefab(1));

// Every frame, after manager.Tick(...)
presentation.Sync();                       // drain commands + batch transform write (Burst)

// Before disposing the manager
presentation.Dispose();
```

Custom pools: implement `IGameObjectPool` and inject it into the `GameObjectPresentation`
constructor, or write your own consumer that drains the `PresentationCommands` singleton
command queue directly.

## Component Design Conventions

1. Components are unmanaged structs implementing **exactly one** component interface:
   `IDataComponent` / `ITagComponent` / `ISingletonComponent` / `IBufferElement`.
2. Tag components carry no instance fields; they occupy archetype mask bits only.
3. Components hold data and pure math helpers only — all behavior lives in systems.
4. Component assemblies must be loaded before the first `World` is created; the registry
   seals afterwards.

## Testing

The source repository ships an NUnit test project under `tests/Ember.Core.Tests`. Pure math
and determinism tests run on any .NET environment; World integration tests that require
`Unity.Collections` native containers execute inside the Unity Test Runner and skip
automatically under a plain .NET CLI — the same convention as the Ember framework tests.
