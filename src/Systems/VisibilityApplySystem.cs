namespace Ember.Core
{
    /// <summary>
    /// 可见性标签应用系统（串行）。读取 <see cref="VisibilityState"/>（由
    /// <see cref="FrustumCullingSystem"/> Job 写入），与 <see cref="InView"/> 标签现状做 diff，
    /// 仅在进出视口的边沿通过 ECB 增删标签——结构变更数量正比于边沿实体数而非全量实体数。
    /// 需在 <see cref="FrustumCullingSystem"/> 之后注册（读写冲突声明会自动排序）。
    /// </summary>
    public sealed class VisibilityApplySystem : SystemBase
    {
        private readonly EntityQuery m_InViewQuery = EntityQuery.With<VisibilityState, InView>();
        private readonly EntityQuery m_OutOfViewQuery = EntityQuery.With<VisibilityState>().None<InView>();
        private readonly EntityQuery m_StaleTagQuery = EntityQuery.With<InView>().None<VisibilityState>();

        protected override void DeclareAccess(AccessBuilder access) => access.Read<VisibilityState>().Write<InView>().StructuralChanges();

        protected override void OnTick(SystemContext ctx)
        {
            foreach (var chunk in ctx.QueryChunks(m_InViewQuery)) {
                var states = chunk.Read<VisibilityState>();
                for (int row = 0; row < chunk.Count; row++) {
                    if (!states[row].InView) {
                        ctx.ECB.RemoveComponent<InView>(chunk.EntityAt(row));
                    }
                }
            }

            foreach (var chunk in ctx.QueryChunks(m_OutOfViewQuery)) {
                var states = chunk.Read<VisibilityState>();
                for (int row = 0; row < chunk.Count; row++) {
                    if (states[row].InView) {
                        ctx.ECB.AddComponent(chunk.EntityAt(row), new InView());
                    }
                }
            }
            
            foreach (var chunk in ctx.QueryChunks(m_StaleTagQuery)) {
                for (int row = 0; row < chunk.Count; row++) {
                    ctx.ECB.RemoveComponent<InView>(chunk.EntityAt(row));
                }
            }
        }
    }
}