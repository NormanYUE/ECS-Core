namespace Ember.Core
{
    /// <summary>
    /// 空间/可见性组件补齐系统（串行）。为带 <see cref="LocalToWorld"/> +
    /// <see cref="BoundingVolume"/> 但缺 <see cref="WorldBounds"/> 或
    /// <see cref="VisibilityState"/> 的实体自动补组件（每实体一生一次），
    /// 使用方无需手工添加。<b>必须先于</b> <see cref="WorldBoundsSystem"/>、
    /// <see cref="FrustumCullingSystem"/> 注册。
    /// </summary>
    public sealed class SpatialSetupSystem : SystemBase
    {
        private readonly EntityQuery m_MissingBounds = EntityQuery.With<LocalToWorld, BoundingVolume>().None<WorldBounds>();

        private readonly EntityQuery m_MissingVisibility = EntityQuery.With<LocalToWorld, BoundingVolume>().None<VisibilityState>();

        protected override void DeclareAccess(AccessBuilder access) => access.Write<WorldBounds>().Write<VisibilityState>().StructuralChanges();

        protected override void OnTick(SystemContext ctx)
        {
            foreach (var chunk in ctx.QueryChunks(m_MissingBounds)) {
                for (int row = 0; row < chunk.Count; row++) {
                    ctx.ECB.AddComponent(chunk.EntityAt(row), new WorldBounds());
                }
            }

            foreach (var chunk in ctx.QueryChunks(m_MissingVisibility)) {
                for (int row = 0; row < chunk.Count; row++) {
                    ctx.ECB.AddComponent(chunk.EntityAt(row), new VisibilityState());
                }
            }
        }
    }
}