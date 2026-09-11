using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间索引维护系统（串行）。每 tick 读取 <see cref="WorldBounds"/>（由
    /// <see cref="WorldBoundsSystem"/> Job 计算），把实体世界包围盒增量维护进
    /// <see cref="SpatialTree"/> 单例组件（四叉/八叉，由 <see cref="SpatialIndexConfig"/>
    /// 单例决定；未配置时使用内置默认值）。消失实体（销毁/包围组件被移除）经树的
    /// 标记清扫在下一帧剔除。
    /// 查询由业务侧经标准单例 API 自取：<c>ref world.GetComponent<SpatialTree>(owner)</c>；
    /// 退出前对组件 ref 调用 <c>Dispose()</c> 释放原生容器。
    /// </summary>
    public sealed class SpatialIndexSystem : SystemBase
    {
        protected override void DeclareAccess(AccessBuilder access)
        {
            access.Read<WorldBounds>().Write<SpatialTree>().StructuralChanges(); // 首次 tick 创建单例实体
        }

        protected override void OnTick(SystemContext ctx)
        {
            var world = ctx.World;
            var owner = world.GetOrCreateSingleton<SpatialTree>();
            ref var tree = ref world.GetComponent<SpatialTree>(owner);

            if (!tree.IsInitialized)
            {
                var config = world.TryGetSingleton<SpatialIndexConfig>(out var cfgOwner)
                    ? world.GetComponent<SpatialIndexConfig>(cfgOwner)
                    : new SpatialIndexConfig
                    {
                        Dimension = SpatialDimension.Octree,
                        WorldCenter = float3.zero,
                        WorldHalfExtent = new float3(1000f),
                        MaxDepth = 8,
                        NodeCapacity = 8,
                    };
                tree.Initialize(config, Allocator.Persistent);
            }

            tree.BeginTick();
            foreach (var chunk in ctx.QueryChunks<WorldBounds>())
            {
                var bounds = chunk.Read<WorldBounds>();
                for (int row = 0; row < chunk.Count; row++)
                    tree.Update(chunk.EntityAt(row), bounds[row].Center, bounds[row].Extents);
            }
            tree.EndTick();
        }
    }
}
