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

## 使用示例

```csharp
using Ember;
using Ember.Core;
using Unity.Mathematics;

var world = new World();
var entity = world.CreateEntity();

world.AddComponent(entity, LocalTransform.FromPosition(new float3(0f, 1f, 0f)));
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
