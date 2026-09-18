# Changelog

All notable changes to Ember Core Components.

## [2.1.6] — 修复 2.1.5 引入的 Clear 越界调用

### Fixed

- **`m_Infos` 未创建时调用 `Clear()` 会抛异常，2.1.5 因此每 tick 报错且运动仍然失效。**

  `NativeList<T>.Clear()` 第一行是 `AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion`，
  而该列表由 `FillInfos` 首次调用时才创建 —— 默认句柄直接抛
  「has been deallocated, it is not allowed to access it」，卡在清空这一步，
  永远走不到创建列表。加 `IsCreated` 守卫。

## [2.1.5] — 修复 MovementSystem 块表不清空导致运动失效

### Fixed

- **`MovementSystem` 的块信息列表 `m_Infos` 每 tick 不清空，运动静默失效。**

  `FillInfos` 是「追加」语义（线速度、角速度两次调用共用一个列表），而作业按
  `Schedule(total, …)` 只处理 `[0, total)` —— 列表不清空时，`total` 是**本 tick 新增**
  的块数，作业处理的却是**列表头部**那批，也就是第一帧记下的块指针。后果有二：

  - 本 tick 的块从来没进过作业，新出现的实体（例如战斗中途生成的单位）位置永远不动，
    而速度、动画状态一切正常，现象是「原地播放移动动画」；
  - 每 tick 泄漏 `chunkCount` 个条目（列表只增不减），且那些指针在块迁移 / 回收后
    已经悬空，作业会写进别人的内存。

  修复：`OnTick` 内收集前 `m_Infos.Clear()`。

## [2.1.4] — 包仓库迁移到 ECS-Core.git

### Changed

- **包仓库由 `Ember-Core.git` 迁到 `ECS-Core.git`，仓库根即 UPM 包根。**

  源码库与包库合并成一个仓库：源码库转公开直接当 UPM 包，独立的 DLL/源码副本包库删除。
  从此一份代码一条历史，不再有「同步到另一个仓库」这一步，也没有副本漂移的可能。

  布局按 Unity 包约定整理：

  | 旧 | 新 | 说明 |
  | --- | --- | --- |
  | `src/` | `Runtime/` | 配 `Ember.Core.Runtime.asmdef` |
  | `libs/` | `Libs~/` | `~` 后缀让 Unity 忽略；否则 `Unity.Burst.dll` 会被当包内插件导入，与 `com.unity.burst` 撞名 |
  | `tests/` | `tests/`（加 asmdef） | `defineConstraints = UNITY_INCLUDE_TESTS`，否则测试代码会被编进包 |
  | 包库的 `package.json` / README / CHANGELOG / LICENSE | 仓库根 | — |

  包内每个资源都补了 `.meta`。Unity 对不可变包目录里没有 `.meta` 的资源**直接忽略**，
  只丢一条警告 —— 源码包资源上百个，漏一个就少一个文件。

  **消费方需改 manifest URL**：

  ```
  - https://github.com/NormanYUE/Ember-Core.git
  + https://github.com/NormanYUE/ECS-Core.git
  ```

  改完要删掉 `Library/PackageCache`，否则 UPM 不会重新解析。

- 依赖提升：com.ember.ecs 1.13.0

## [2.1.3] — 改为源码包发布（不再发预编译 DLL）

### Changed

- **包内容由 `Runtime/*.dll` 改为 `Runtime/**/*.cs` 源码。**

  **为什么**：预编译 DLL 里的 `[BurstCompile]` 不会被 Unity 的 Burst ILPP 处理 ——
  那条流水线只跑 Unity 自己编译的程序集。实测 22 个 Job（Collision 19 / Navigation 1 /
  Core 2）在运行期报 `not a known Burst entry point`，退回托管路径。
  改成源码包后由 Unity 编译，这条流水线才成立。

  **附带解决**：
  - `#if ENABLE_UNITY_COLLECTIONS_CHECKS` 之类 Unity 侧符号在源码编译下**真正有定义**，
    不会再出现「包内那段其实是死代码」（`Ember.Collision` 曾因此把碰撞管线整个跑崩）
  - 不再需要在包库里维护 `.meta`（那是 0.3.1 / 0.3.2 两次事故的根源）
  - 交付的是可读、可调试、可步进的源码

  **消费方无需改动**：包库 URL 不变，公开 API 不变。

- `Runtime/Ember.core.Runtime.asmdef` 的 `precompiledReferences` 由指自己的 DLL
  改为 **`Ember.dll`**（框架仍以预编译形式提供）。`references` 显式列出 Unity 侧依赖
  （`Unity.Collections` / `Unity.Mathematics` / `Unity.Burst`）与上游 Ember 程序集。
- 补上 `com.unity.collections` 依赖声明 —— 之前是 DLL 包，编译期引用是 dotnet 侧的
  `libs/Unity.Collections.dll`，包本身不声明；改为源码包后必须声明。
- 依赖提升：com.ember.ecs 1.13.0

### Notes

- 同步工具：`Ember.Framework/tools/deploy-package-sources.py`（源码库 → 包库，
  确定性生成 `.meta`）。发布流程见根目录 `CLAUDE.md`。
- 未验证：Unity 运行期。asmdef 的跨包预编译引用（`Ember.dll`）需要在 Unity 里确认能解析。

## [2.1.2] — 依赖指向 Ember 1.13.0

### Changed
- 依赖 `com.ember.ecs` 由 1.12.0 提升至 1.13.0。本包源码未变（`SpatialTree` 的 buffer 一直用
  `ClearBuffer` + `EnsureLength` 设定长度，没有踩 `CreateBuffer` 长度为 0 的坑）；
  提升版本只为让依赖链上的精确版本一致 —— UPM 按精确版本解析，下游包要求 1.13.0 时
  本包不能还停在 1.12.0。

## [2.1.1] — 修复：系统构造早于 World 创建时抛未注册组件

### Fixed
- **按官方装配顺序注册 `SpatialSystemGroup` 不再抛 `unregistered component type`。**

  `SystemTicker.Register` 会**立即构造**系统，而它在官方示例里早于 `ECSManager.Start()`，
  也就早于 `World` 创建；但组件类型注册原本只发生在 `World` 构造函数里。系统的字段初始化器
  一旦构造 `EntityQuery`（`EntityQuery.With<T>()` / `ComponentMask.With<T>()` 当场读
  `ComponentTypeRegistry`），全新 AppDomain 的第一次运行就必然抛异常。

  受影响：`SpatialSetupSystem`、`PresentationCommandSystem`、`VisibilityApplySystem`、
  `MovementSystem`。四者的 query 字段去掉初始化器与 `readonly`，改在 `OnCreate()` 内构造——
  `OnCreate` 由 `SystemTicker.Init` 调用，晚于 `World` 构造；`BuildAccess` 紧随其后，
  `DeclareAccess` 不依赖这些字段。

### Changed
- 依赖 `com.ember.ecs` 由 1.11.0 提升至 1.12.0（框架侧新增
  `World.EnsureComponentTypesRegistered()`，并让 `Register` / `ApplyProfile` 在构造系统前触发注册）。

### Notes
- 两处都改是刻意的：框架侧保证了经由 `SystemTicker` 的装配路径，包侧则不再依赖「构造期读全局状态」
  这一副作用。任何在 `World` 之前构造系统的路径（编辑器工具、不建 World 的纯逻辑单测）都不会再踩坑。

## [2.1.0] — 运动积分系统

### Added
- **`MovementSystem`（Job·Burst）**：把 `LinearVelocity` / `AngularVelocity` 积分到 `LocalTransform`。
  位置 `Position += LinearVelocity.Value * dt`；旋转按角速度轴角积分，四元数乘后归一化。
  两条查询（线速度 / 角速度）均带 `None<Static>` 与 `None<Disabled>`，被排除的实体不参与积分。
  - 稳态 0GC：Chunk 列指针收进复用列表，仅在 Chunk 数增长时扩容；自调度 Job 在 `OnTick` 内
    `Complete()`，满足框架「不允许跨 tick 挂起 Job」的约束。
- **`MotionSystemGroup`**：一行注册运动积分，`manager.GetTicker(idx).Register<MotionSystemGroup>()`。

### Changed
- 时间来源优先取 `WorldTime` 单例（遵守 `TimeScale`）；单例不存在时退回本次 Tick 的步长，
  未维护 `WorldTime` 的工程不再出现运动静默失效。
- 依赖 `com.ember.ecs` 由 1.10.1 提升至 1.11.0（`SpatialTreeView` 改用 `World.ResizeBuffer` 一次扩容，
  取代逐元素 `AddBufferElement` 循环）。
- 测试扩充至 62 项：CLI 25 项通过；37 项原生容器测试在 Unity Test Runner 中执行。

## [2.0.0] — 空间树存储迁移至 World 托管缓冲（免 Dispose）

### Breaking
- **`SpatialTree` 不再持有原生容器，也不再提供 `Dispose()` / `Initialize(Allocator)`。**
  树数据改存 World 托管 buffer（随 `World.Dispose()` 自动释放），组件本体只保留标量状态与 buffer 句柄。
  - 旧：`ref var tree = ref world.GetComponent<SpatialTree>(owner); ... tree.Dispose();`
  - 新：`if (world.TryGetSpatialTree(out var tree)) { tree.QuerySphere(center, r, ref buffer); }`
    退出时随 `ECSManager.Dispose()` 一并回收，无需任何释放调用。
- 树操作统一改经 `SpatialTreeView`（`world.GetSpatialTree()` / `world.TryGetSpatialTree`）：
  `Insert` / `Remove` / `Update` / `QueryAABB` / `QuerySphere` / `BeginTick` / `EndTick` / `Clear`。
- 实体映射由 `NativeParallelHashMap` 换为直索引稀疏映射（按 `Entity.Index` 直接寻址，
  元素内版本校验槽位复用），查找 O(1) 且常数更低。

### Added
- `SpatialTreeView`（树操作视图）与 `SpatialTreeExtensions`（`GetSpatialTree` / `TryGetSpatialTree` / `EnsureSpatialTree`）。
- 新增实体槽复用防护测试（旧 Index + 新 Version 不得命中旧映射）。

### Fixed
- 修复 `Subdivide` 中元素计数双重递减（`Unlink` 已递减后又手动递减），
  计数漂移会干扰叶容量判断与空块收缩。

### Changed
- `SpatialIndexSystem` 不再需要 `Allocator.Persistent` 初始化，首 tick 自动建树。
- 测试扩充至 52 项：CLI 25 项通过；27 项原生容器测试在 Unity Test Runner 中执行。

## [1.0.0] — 空间索引、视锥剔除与 GameObject 表现层

### Breaking
- 移除静态辅助 `LocalTransform.Identity` / `LocalTransform.FromPosition` / `LocalToWorld.Identity` / `LocalToWorld.Compose`（静态禁令）。迁移：直接构造 `new LocalTransform(position, quaternion.identity, 1f)`；层级组合用 `math.mul(parent.Value, local.ToMatrix())`。

### Added
- **空间索引**：`BoundingVolume` / `WorldBounds` 组件；`SpatialTree` 纯非托管单例组件（四叉/八叉统一，`NativeList`/`NativeParallelHashMap` 存储，稳态 0GC），`QueryAABB`/`QuerySphere` 填充调用方 `NativeList<Entity>`；消失实体经标记清扫剔除（延迟一帧）；`SpatialIndexConfig` 单例配置维度/根范围/深度/容量。
- **视锥剔除**：`CameraFrustum` 单例（桥接代码每帧写入）、`VisibilityState`（bit0=当前帧、bit1=上一帧，`EnteredView`/`ExitedView` 边沿属性）、`InView` 标签（仅边沿增删）；`FrustumMath` 纯数学（Gribb-Hartmann 平面提取、球/AABB 测试、世界盒换算）；`WorldBoundsSystem` 与 `FrustumCullingSystem` 为 Burst Job。
- **系统组**：`SpatialSystemGroup` 一行接入整条空间/剔除管线（补齐→世界包围盒→剔除→标签应用→空间索引）。
- **GameObject 表现层**：`PresentationPrefab`（预制体 Id）/ `PresentationLink` / `PresentationCommands` 单例命令通道；视口边沿驱动 Spawn/Despawn，销毁实体盖戳清扫（回收延迟一帧）；`PresentationSyncSystem`（Job·Burst）并行写 TRS 同步槽位；托管桥 `GameObjectPresentation` drain 命令并经 `TransformAccessArray` + `IJobParallelForTransform`（Burst）批量回写 GameObject Transform；`IGameObjectPool` 支持业务侧池注入，`GameObjectPool` 为默认池实现（分桶栈 + Prewarm 预热）。
- **显式 Burst 策略**：程序集级 `EmberJobCompilationMode.Burst`，作业 Burst 编译显式可见；新增 `com.unity.burst` 1.8.13 包依赖。

### Changed
- 测试扩充至 51 项：CLI 25 项通过；26 项依赖 Unity 原生容器/引擎 API 的测试在 Unity Test Runner 中执行。

## [0.1.0] — 首次发布

### Added
- **空间组件**：`LocalTransform`（位置/旋转/等比缩放，含 `Identity`、`ToMatrix`、`TransformPoint` 等数学辅助）与 `LocalToWorld`（世界矩阵，含 `Compose` 层级组合与坐标轴访问）。
- **运动组件**：`LinearVelocity`（米/秒）与 `AngularVelocity`（轴角向量，弧度/秒）。
- **时间组件**：`Lifetime`（剩余存活秒数）、`Age`（已存活秒数）与 `WorldTime` 单例（`TimeScale` / `DeltaTime` / `UnscaledDeltaTime` / `ElapsedTime` / `FrameCount`）。
- **状态标记**：`Disabled` / `Static` / `Prefab` 三个 Tag 组件及配套查询约定（`None<Disabled>` 等）。
- **随机组件**：`GlobalRandom` 单例，基于 `Unity.Mathematics.Random` 的确定性随机源。
- 全部组件为 unmanaged struct，兼容 Burst 编译的 Job 系统；由 Ember 源生成器自动注册。
- NUnit 测试项目：纯数学与确定性测试 11 项；依赖 Unity 原生容器的 World 集成测试 5 项（CLI 下自动跳过，Unity Test Runner 中执行）。
