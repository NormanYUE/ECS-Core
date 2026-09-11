# Ember.Core

基于 [Ember ECS 框架](../Ember) 的 Unity 项目基础组件库。提供空间、运动、时间、状态标记等
通用组件，全部为 unmanaged struct，可直接用于 Chunk 存储与 Burst 编译的 Job 系统。

- 目标框架：`netstandard2.1`（Unity 2022.3+）
- 数学类型：`Unity.Mathematics`（`float3` / `quaternion` / `float4x4`）
- 组件注册：编译时由 `Ember.Generator` 源生成器自动扫描并生成注册清单，无需手工注册

## 组件清单

### Spatial（空间）

| 组件 | 说明 |
|---|---|
| `LocalTransform` | 本地变换：`Position` / `Rotation` / `Scale`（等比）。提供 `Identity`、`ToMatrix()`、`TransformPoint`、`InverseTransformPoint`、`TransformDirection` |
| `LocalToWorld` | 本地到世界的 `float4x4` 矩阵。提供 `Identity`、`Position` / `Right` / `Up` / `Forward`、`TransformPoint`、`Compose(local, parent)` |

层级关系直接使用框架内置的 `ParentComponent` / `ChildEntity`，本库不重复定义。

### Motion（运动）

| 组件 | 说明 |
|---|---|
| `LinearVelocity` | 线速度（米/秒），由移动系统积分到 `LocalTransform.Position` |
| `AngularVelocity` | 角速度，轴角向量表示（方向为旋转轴，模长为弧度/秒） |

### Timing（时间）

| 组件 | 说明 |
|---|---|
| `Lifetime` | 剩余存活秒数，由生命周期系统递减，归零销毁（子弹、特效等） |
| `Age` | 实体已存活秒数，用于渐强/衰减插值、成长阶段判定 |
| `WorldTime`（单例） | 全局时间状态：`TimeScale` / `DeltaTime` / `UnscaledDeltaTime` / `ElapsedTime` / `FrameCount`，供 Job 内或无法访问 `SystemContext.DeltaTime` 的场景读取 |

### State（状态标记，Tag）

| 组件 | 说明 |
|---|---|
| `Disabled` | 禁用标记。约定系统查询附带 `None<Disabled>` 跳过被禁用实体 |
| `Static` | 静态标记。约定移动/变换系统附带 `None<Static>` 跳过永不移动的实体 |
| `Prefab` | 预制体模板标记。模板实体不参与常规逻辑，仅作实例化来源 |

### Random（随机）

| 组件 | 说明 |
|---|---|
| `GlobalRandom`（单例） | 全局确定性随机源（`Unity.Mathematics.Random`）。值类型，取数需 `ref` 读写；并行随机流应按实体拆分独立组件 |

### Spatial Index（空间索引）

| 类型 | 说明 |
|---|---|
| `BoundingVolume` | 本地空间 AABB（`Center`/`Extents`），空间索引与视锥剔除的输入 |
| `WorldBounds` | 世界空间 AABB，由 `WorldBoundsSystem`（Job·Burst）每帧计算一次，索引与剔除共享 |
| `SpatialTree` | 0GC 空间划分树（四叉/八叉统一），纯非托管 struct **单例组件**：SoA 原生容器（NativeList/NativeParallelHashMap）+ 空闲链表，稳态零分配；插入/删除/移动 O(log n)~O(1)；`QueryAABB`/`QuerySphere` 填充调用方 `NativeList<Entity>`；四叉树支持 XY/XZ 平面；消失实体由标记清扫剔除（延迟一帧）；Burst 可编译形态 |
| `SpatialIndexConfig`（单例） | 索引配置：维度（QuadXY/QuadXZ/Octree）、世界范围、最大深度、节点容量 |
| `SpatialSetupSystem` | 串行。为新实体自动补 `WorldBounds` / `VisibilityState`（每实体一次），**必须先于其他空间系统注册** |
| `WorldBoundsSystem` | **Job·Burst**。`LocalToWorld`+`BoundingVolume` → `WorldBounds` |
| `SpatialIndexSystem` | 串行。读 `WorldBounds` 增量维护树；实体销毁自动剔除 |
| `SpatialIndex` | 静态注册表，`SpatialIndex.GetTree(world)` 供任意代码做范围查询 |

### Culling（视锥剔除）

| 类型 | 说明 |
|---|---|
| `CameraFrustum`（单例） | 6 个归一化视锥平面（法线朝内），由桥接代码每帧从相机 VP 矩阵写入 |
| `VisibilityState` | Job 可写的可见性字节标志（bit0=当前视口内，bit1=上一帧视口内；`EnteredView`/`ExitedView` 边沿属性） |
| `InView`（标签） | 视口内查询过滤标记，仅在进出视口边沿增删（结构变更正比于边沿实体数） |
| `FrustumMath` | 纯数学：`FromViewProjection` 平面提取（Gribb-Hartmann）、球/AABB 视锥测试、`TransformAABB` 世界盒换算（Burst 兼容） |
| `FrustumCullingSystem` | **Job·Burst**。包围盒-视锥测试写入 `VisibilityState`；无相机单例时整帧跳过 |
| `VisibilityApplySystem` | 串行。diff 状态与标签，边沿增删 `InView`（Job 无法做结构变更，拆出此系统保住并行） |

### Presentation（GameObject 表现层）

| 类型 | 说明 |
|---|---|
| `PresentationPrefab` | 预制体 Id（int）。托管引用进不了组件，Id 由业务侧在池中注册映射 |
| `PresentationLink` | 实体↔同步槽位（桥分配/移除，业务勿动） |
| `PresentationCommands`（单例） | 命令队列 + 存活跟踪 + TRS 同步数组，纯非托管原生容器 |
| `TransformDecompose` | 纯数学：世界矩阵 → TRS 分解（正交基假设，忽略 shear） |
| `PresentationCommandSystem` | 串行。视口边沿产 Spawn/Despawn 命令 + 盖戳清扫销毁实体（回收延迟一帧）。串行原因：框架 chunk job 无 Entity 访问器，命令必须带实体 Id |
| `PresentationSyncSystem` | **Job·Burst**。可见实体 `LocalToWorld` → TRS 写同步槽位（每帧热路径） |
| `PresentationSystemGroup` | 表现层系统组；须在 `SpatialSystemGroup` 之后注册 |
| `IGameObjectPool` / `GameObjectPool` | 池接口（业务可换实现）/ Core 默认池（分桶栈 + Prewarm） |
| `GameObjectPresentation` | 托管桥：每帧 Tick 后 `Sync()` drain 命令 + `TransformAccessArray`+`IJobParallelForTransform`(Burst) 批量回写 GO Transform；`Dispose()` 统一释放 |

### 系统管线与注册顺序

管线已封装为 `SpatialSystemGroup`，业务侧一行接入（组内注册顺序即管线顺序，依赖图再按读写冲突自动分层）：

```csharp
manager.GetTicker(updateIdx).Register<SpatialSystemGroup>();
```

```
SpatialSetupSystem        (串行, 补齐组件, 结构变更屏障)
WorldBoundsSystem         (Job·Burst, 算世界AABB)
FrustumCullingSystem      (Job·Burst, 写 VisibilityState)
VisibilityApplySystem     (串行, 边沿增删 InView 标签)
SpatialIndexSystem        (串行, 维护空间树)
```

GameObject 表现层另成一组，依赖剔除产物，须在其后注册：

```csharp
manager.GetTicker(updateIdx).Register<PresentationSystemGroup>();
```

```
PresentationCommandSystem  (串行, 边沿产命令 + 销毁清扫)
PresentationSyncSystem     (Job·Burst, TRS 写同步槽位)
```

GameObject 操作不在系统内——业务侧每帧 Tick 后驱动桥：

```csharp
var pool = new GameObjectPool();
pool.RegisterPrefab(1, enemyPrefabGo);
pool.Prewarm(1, 64); // 可选：预热消除实例化尖峰
var presentation = new GameObjectPresentation(world, pool); // 默认池可省参

// 每帧：manager.Tick(...); 之后
presentation.Sync();

// 退出前（销毁 manager 之前）
presentation.Dispose();
```

业务侧自定义池：实现 `IGameObjectPool` 注入构造；或完全自写消费者直接 drain
`PresentationCommands` 单例命令队列。

依赖图按读写冲突声明自动排序上下游；两个串行系统的屏障语义保证 Job 系统在其间并行调度。
Burst 为**显式策略**（`[assembly: EmberJobCompilation(EmberJobCompilationMode.Burst)]`）：
Unity.Burst 不可用时编译期报 EMBER005、运行时 BuildAccess 失败，绝不静默回退托管调度。
生成的 `WorldBoundsSystemBurstJob` / `FrustumCullingSystemBurstJob` 调度器随 DLL 分发，
消费工程只需安装 `com.unity.burst`（包依赖已声明）。

## 系统使用示例

```csharp
// 启动：一行注册整条空间/剔除管线
manager.GetTicker(updateIdx).Register<SpatialSystemGroup>();

// 配置空间索引（可选，不配置时使用内置默认值）
var cfg = world.GetOrCreateSingleton<SpatialIndexConfig>();
world.SetComponent(cfg, new SpatialIndexConfig
{
    Dimension = SpatialDimension.QuadXZ,
    WorldCenter = float3.zero,
    WorldHalfExtent = new float3(500f),
    MaxDepth = 8,
    NodeCapacity = 8,
});

// 相机桥接：每帧写入视锥
var cam = world.GetOrCreateSingleton<CameraFrustum>();
world.SetComponent(cam, FrustumMath.FromViewProjection(Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix));

// 游戏代码：范围查询（树是 SpatialTree 单例组件，必须经 ref 使用；
// 首帧系统未运行时单例尚未初始化，用 IsInitialized 判断）
if (world.TryGetSingleton<SpatialTree>(out var treeOwner))
{
    ref var tree = ref world.GetComponent<SpatialTree>(treeOwner);
    if (tree.IsInitialized)
    {
        var buffer = new NativeList<Entity>(256, Allocator.TempJob);
        tree.QuerySphere(explosionCenter, radius, ref buffer);
        // ... 用完 buffer.Dispose()
    }
}

// 退出前（销毁 ECSManager 之前）：释放树的原生容器
if (world.TryGetSingleton<SpatialTree>(out var teardownOwner))
{
    ref var tree = ref world.GetComponent<SpatialTree>(teardownOwner);
    if (tree.IsInitialized) tree.Dispose();
}
```

## 使用示例

```csharp
using Ember;
using Ember.Core;
using Unity.Mathematics;

var world = new World();
var entity = world.CreateEntity();

world.AddComponent(entity, new LocalTransform(new float3(0f, 1f, 0f), quaternion.identity, 1f));
world.AddComponent(entity, new LinearVelocity(new float3(0f, 0f, 5f)));
world.AddComponent(entity, new Lifetime(3f));

var timeOwner = world.GetOrCreateSingleton<WorldTime>();
```

## 构建与测试

```bash
DOTNET=/Users/norman/.dotnet/dotnet

$DOTNET build Ember.Core.sln -c Release
$DOTNET test tests/Ember.Core.Tests/Ember.Core.Tests.csproj -c Release
```

说明：依赖 `Unity.Collections` 原生容器的集成测试（创建 `World` 实体）在纯 .NET CLI 下
自动跳过，在 Unity Test Runner 中会真正执行——与 Ember 框架自身测试约定一致。

## 引用配置

csproj 通过以下 MSBuild 属性定位依赖，默认值指向本机相邻仓库，可按需覆盖：

| 属性 | 默认值 |
|---|---|
| `EmberFrameworkDir` | `../Ember`（提供 `libs/` 下的 Unity 程序集） |
| `EmberRuntimeDll` | `../Ember.Package/Runtime/Ember.dll` |
| `EmberGeneratorDll` | `../Ember.Package/RoslynAnalyzers/Ember.Generator.dll` |

在 Unity 工程中使用时改为 asmdef 引用 `Ember.Runtime`，Unity 程序集由引擎提供，
源生成器经 `RoslynAnalyzers` 文件夹自动生效。

## 新增组件约定

1. 定义 unmanaged struct，且**恰好**实现一种组件接口：
   `IDataComponent` / `ITagComponent` / `ISingletonComponent` / `IBufferElement`
2. Tag 组件不得包含实例字段
3. 命名空间统一 `Ember.Core`，按分类放入 `src/<Category>/`
4. 源生成器会自动完成注册；若新增程序集，需在首个 `World` 创建前加载
5. **静态禁令**：组件、系统与普通类不得声明静态成员/方法。纯函数数学集中于工具类
   （如 `FrustumMath`），World 级全局数据结构（如空间树）以单例组件存放，业务侧经
   标准单例 API 自取。`const` 编译期字面量不受此限。
6. **一文件一类型**：每个文件只定义一个顶层类型（class/struct/enum/interface），
   文件名与类型名一致；连 Job 作业与其宿主系统、枚举与其使用方也必须拆分为独立文件。
