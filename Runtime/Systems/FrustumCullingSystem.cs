namespace Ember.Core
{
    /// <summary>
    /// 视锥剔除系统（Job·Burst）：对带 <see cref="WorldBounds"/> + <see cref="VisibilityState"/>
    /// 的实体做世界包围盒-视锥测试，结果写入 <see cref="VisibilityState"/>。
    /// 结构变更（<see cref="InView"/> 标签增删）由 <see cref="VisibilityApplySystem"/> 串行完成，
    /// 本系统因此可以保持并行调度。<see cref="CameraFrustum"/> 单例不存在时整帧跳过。
    /// </summary>
    public sealed class FrustumCullingSystem : JobSystem<FrustumCullingJob>
    {
        protected override void DeclareAccess(AccessBuilder access) => access.Read<WorldBounds>().Write<VisibilityState>();

        protected override FrustumCullingJob CompileJob(SystemContext ctx)
        {
            var job = new FrustumCullingJob
            {
                BoundsSlot = Slot<WorldBounds>(),
                StateSlot = Slot<VisibilityState>(),
                HasCamera = 0,
                Frustum = default,
            };
            
            if (ctx.World.TryGetSingleton<CameraFrustum>(out var owner))
            {
                job.Frustum = ctx.World.GetComponent<CameraFrustum>(owner);
                job.HasCamera = 1;
            }
            return job;
        }
    }
}
