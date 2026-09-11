using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间索引维护系统。每 tick 遍历带 <see cref="LocalToWorld"/> + <see cref="BoundingVolume"/>
    /// 的实体，把世界包围盒增量维护进空间树（四叉/八叉，由 <see cref="SpatialIndexConfig"/>
    /// 单例决定；未配置时使用 <see cref="SpatialIndexConfig.Default"/>）。
    /// 实体销毁或包围组件被移除时自动从树中剔除。
    /// 其他代码经 <see cref="SpatialIndex"/> 取树做范围查询。
    /// </summary>
    public sealed class SpatialIndexSystem : SystemBase
    {
        private World m_World;

        /// <summary>当前维护的空间树（首次 tick 后可用）。</summary>
        public SpatialTree Tree { get; private set; }

        protected override void DeclareAccess(AccessBuilder access)
            => access.Read<LocalToWorld>().Read<BoundingVolume>();

        public override void OnDestroy()
        {
            if (m_World != null)
            {
                SpatialIndex.Unregister(m_World, this);
                m_World = null;
            }
            Tree = null;
        }

        protected override void OnTick(SystemContext ctx)
        {
            if (Tree == null && !TryCreateTree(ctx)) return;

            foreach (var chunk in ctx.QueryChunks<LocalToWorld, BoundingVolume>())
            {
                var localToWorlds = chunk.Read<LocalToWorld>();
                var volumes = chunk.Read<BoundingVolume>();
                for (int row = 0; row < chunk.Count; row++)
                {
                    FrustumMath.TransformAABB(localToWorlds[row].Value,
                        volumes[row].Center, volumes[row].Extents,
                        out float3 center, out float3 extents);
                    Tree.Update(chunk.EntityAt(row), center, extents);
                }
            }
        }

        protected override void OnEntityDestroyed(Entity entity) => Tree?.Remove(entity);

        protected override void OnComponentRemoved(Entity entity, ComponentTypeId typeId)
        {
            // 移除开销是一次 O(1) 字典探测，无条件执行以避免依赖组件类型比对
            Tree?.Remove(entity);
        }

        private bool TryCreateTree(SystemContext ctx)
        {
            SpatialIndexConfig config;
            if (ctx.World.TryGetSingleton<SpatialIndexConfig>(out var owner))
                config = ctx.World.GetComponent<SpatialIndexConfig>(owner);
            else
                config = SpatialIndexConfig.Default;

            switch (config.Dimension)
            {
                case SpatialDimension.QuadXY:
                    Tree = new Quadtree(QuadtreePlane.XY, config.WorldCenter, config.WorldHalfExtent.x,
                        config.MaxDepth, config.NodeCapacity);
                    break;
                case SpatialDimension.QuadXZ:
                    Tree = new Quadtree(QuadtreePlane.XZ, config.WorldCenter, config.WorldHalfExtent.x,
                        config.MaxDepth, config.NodeCapacity);
                    break;
                default:
                    Tree = new Octree(config.WorldCenter, config.WorldHalfExtent,
                        config.MaxDepth, config.NodeCapacity);
                    break;
            }

            m_World = ctx.World;
            SpatialIndex.Register(ctx.World, this);
            return true;
        }
    }
}
