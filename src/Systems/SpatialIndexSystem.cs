using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间索引维护系统（串行）。每 tick 读取 <see cref="WorldBounds"/>（由
    /// <see cref="WorldBoundsSystem"/> Job 计算），把实体世界包围盒增量维护进
    /// <see cref="SpatialTree"/>（存储由 World 托管 buffer 承载，随 World 自动释放，
    /// 无需任何 Dispose）。消失实体（销毁/包围组件被移除）经树的标记清扫在下一帧剔除。
    /// 查询由业务侧自取：<c>world.GetSpatialTree().QueryAABB(...)</c>。
    /// </summary>
    public sealed class SpatialIndexSystem : SystemBase
    {
        protected override void DeclareAccess(AccessBuilder access) => access.Read<WorldBounds>().Write<SpatialTree>().StructuralChanges(); // 首次 tick 创建单例实体

        protected override void OnTick(SystemContext ctx)
        {
            var world = ctx.World;
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

            var tree = world.EnsureSpatialTree(config);

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
