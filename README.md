# Ember Core Components 使用手册

[English](README_EN.md)

Ember ECS 框架的基础组件库。提供空间变换、运动、时间、状态标记与确定性随机等通用组件，
全部为 unmanaged struct，直接存入 Chunk 列，兼容 Burst 编译的 Job 系统。
组件由 Ember 源生成器在编译时自动注册，无需手工登记。

## 安装

本包依赖 `com.ember.ecs`（Ember ECS 框架）。两者均通过 Unity Package Manager 安装：

1. 打开项目的 `Packages/manifest.json`
2. 添加依赖（版本号以实际发布为准）：

```json
{
  "dependencies": {
    "com.ember.ecs": "1.10.1",
    "com.ember.core": "0.1.0"
  }
}
```

3. 若通过 Git URL 安装：UPM 不解析传递 Git 依赖，请确保 `com.ember.ecs` 已在 manifest 中显式声明。
4. 保存后 Unity 自动解析；`Unity.Mathematics` 由框架依赖自动带入。

要求 Unity 2022.3 或更高版本。

## 组件清单

命名空间均为 `Ember.Core`。实体层级关系（父/子）请直接使用框架内置的
`ParentComponent` / `ChildEntity`，本库不重复定义。

### Spatial（空间）

| 组件 | 类型 | 说明 |
|---|---|---|
| `LocalTransform` | Data | 本地变换：`Position` (float3) / `Rotation` (quaternion) / `Scale` (float 等比)。无父实体时即世界变换。辅助：`ToMatrix()`、`TransformPoint`、`InverseTransformPoint`、`TransformDirection` |
| `LocalToWorld` | Data | 本地到世界的 `float4x4` 矩阵。辅助：`Position` / `Right` / `Up` / `Forward`、`TransformPoint` |

### Motion（运动）

| 组件 | 类型 | 说明 |
|---|---|---|
| `LinearVelocity` | Data | 线速度 `Value` (float3)，米/秒。由移动系统积分到 `LocalTransform.Position` |
| `AngularVelocity` | Data | 角速度 `Value` (float3)，轴角向量：方向为旋转轴，模长为角速度（弧度/秒） |

配套积分系统 `MovementSystem`（Job·Burst）把两者积分到 `LocalTransform`：
位置 `Position += LinearVelocity.Value * dt`；旋转按轴角积分，四元数乘后归一化。
查询带 `None<Static>` 与 `None<Disabled>`，被排除的实体不参与积分。

### Timing（时间）

| 组件 | 类型 | 说明 |
|---|---|---|
| `Lifetime` | Data | 剩余存活秒数 `Remaining`。由生命周期系统递减，归零后销毁实体（子弹、特效等） |
| `Age` | Data | 已存活秒数 `Elapsed`。用于渐强/衰减插值、成长阶段判定 |
| `WorldTime` | Singleton | 全局时间状态：`TimeScale` / `DeltaTime` / `UnscaledDeltaTime` / `ElapsedTime` / `FrameCount`。供 Job 内或无法访问 `SystemContext.DeltaTime` 的场景读取。本库不代管它的推进，由使用方每帧写入；`MovementSystem` 优先读它（遵守 `TimeScale`），单例缺席时退回本次 Tick 的步长 |

### State（状态标记）

| 组件 | 类型 | 说明 |
|---|---|---|
| `Disabled` | Tag | 禁用标记。约定：系统查询附带 `None<Disabled>` 跳过被禁用实体；比销毁更适合临时摘除行为 |
| `Static` | Tag | 静态标记。约定：移动/变换系统附带 `None<Static>` 跳过永不移动的实体，节省积分与矩阵重算 |
| `Prefab` | Tag | 预制体模板标记。模板实体不参与常规逻辑（常规查询附带 `None<Prefab>`），仅作实例化来源 |

### Random（随机）

| 组件 | 类型 | 说明 |
|---|---|---|
| `GlobalRandom` | Singleton | 全局确定性随机源（`Unity.Mathematics.Random`）。值类型，每次取数推进状态，须以 `ref` 读写。并行随机流应按实体拆分独立随机组件，不要共用本单例 |

### Spatial Index（空间索引）

| 类型 | 说明 |
|---|---|
| `BoundingVolume` | 本地 AABB（`Center`/`Extents`），空间索引与剔除的输入 |
| `WorldBounds` | 世界 AABB，由 `WorldBoundsSystem`（Job·Burst）每帧换算 |
| `SpatialIndexConfig` | Singleton：维度（QuadXY/QuadXZ/Octree）、根范围、最大深度、节点容量；不配置时用内置默认值 |
| `SpatialTree` | Singleton：blittable 空间树（四叉/八叉统一，0GC 稳态），标量 + World 托管 buffer 句柄——**无需 Dispose**，随 World 自动释放 |
| `SpatialTreeView` | 树操作视图：`world.GetSpatialTree()` / `world.TryGetSpatialTree(out view)` 获取；`Insert`/`Remove`/`Update`/`QueryAABB`/`QuerySphere`（填充调用方 `NativeList<Entity>`） |

### Culling（视锥剔除）

| 类型 | 说明 |
|---|---|
| `CameraFrustum` | Singleton：6 个归一化视锥平面，桥接代码每帧从相机 VP 矩阵写入 |
| `VisibilityState` | 可见性字节（bit0=当前帧在内，bit1=上一帧在内；`EnteredView`/`ExitedView` 边沿属性） |
| `InView` | Tag：视口内过滤标记，仅边沿增删 |
| `FrustumMath` | 纯数学工具：`FromViewProjection` 平面提取、球/AABB 视锥测试（Burst 兼容） |

### Presentation（GameObject 表现层）

| 类型 | 说明 |
|---|---|
| `PresentationPrefab` | 预制体 Id（int）；托管引用进不了组件，Id 在池中注册映射 |
| `PresentationLink` | 实体↔同步槽位（桥自动增删，业务勿动） |
| `PresentationCommands` | Singleton：Spawn/Despawn 命令队列 + TRS 同步数组（原生容器通道） |
| `IGameObjectPool` / `GameObjectPool` | 池接口（业务可注入自实现）/ Core 默认池（分桶失活栈 + `Prewarm` 预热） |
| `GameObjectPresentation` | 托管桥：每帧 Tick 后 `Sync()` 生成/回收/批量回写 Transform；退出前 `Dispose()` |

系统管线已封装为三个组（业务侧各行接入）：

```csharp
manager.GetTicker(fixedIdx).Register<MotionSystemGroup>();         // 速度→LocalTransform 积分——须在读 LocalTransform 的下游之前
manager.GetTicker(updateIdx).Register<SpatialSystemGroup>();       // 补齐→包围盒→剔除→标签→空间索引
manager.GetTicker(updateIdx).Register<PresentationSystemGroup>();  // 表现层命令(Job·Burst 同步)——须在剔除之后
```

## 使用示例

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

读取全局时间单例：

```csharp
var owner = World.GetOrCreateSingleton<WorldTime>();
ref var time = ref World.GetComponent<WorldTime>(owner);
time.ElapsedTime += time.DeltaTime;
```

排除禁用实体的移动系统查询约定：

```csharp
var query = EntityQuery.With<LocalTransform, LinearVelocity>().None<Disabled, Static>();
```

GameObject 表现层接入（生成/回收/同步由视口驱动）：

```csharp
// 启动
var pool = new GameObjectPool();
pool.RegisterPrefab(1, enemyPrefabGo);
pool.Prewarm(1, 64);                       // 可选：预热消除实例化尖峰
var presentation = new GameObjectPresentation(world, pool);

// 实体侧：挂上预制体 Id 即纳入表现层
world.AddComponent(entity, new PresentationPrefab(1));

// 每帧：manager.Tick(...) 之后
presentation.Sync();                       // drain 命令 + Burst 批量回写 Transform

// 退出前（销毁 manager 之前）
presentation.Dispose();
```

业务侧自定义池：实现 `IGameObjectPool` 并注入 `GameObjectPresentation` 构造；
或完全自写消费者直接 drain `PresentationCommands` 单例命令队列。

## 组件设计约定

1. 组件为 unmanaged struct，且**恰好**实现一种组件接口：
   `IDataComponent` / `ITagComponent` / `ISingletonComponent` / `IBufferElement`。
2. Tag 组件不含实例字段，只占 Archetype 掩码位。
3. 组件只存数据与纯数学辅助方法；行为逻辑一律放在 System 中。
4. 组件程序集必须在首个 `World` 创建前加载完毕（注册表随后封闭）。

## 测试

源码仓库内含 NUnit 测试项目 `tests/Ember.Core.Tests`。纯数学与确定性测试可在任意
.NET 环境运行；依赖 `Unity.Collections` 原生容器的 World 集成测试在 Unity Test Runner
中执行，纯 .NET CLI 下自动跳过（与 Ember 框架测试约定一致）。
