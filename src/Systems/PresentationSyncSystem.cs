namespace Ember.Core
{
    /// <summary>
    /// 表现层同步系统（Job·Burst）。每帧把已 Spawn 实体（带 <see cref="PresentationLink"/>）
    /// 的世界变换并行写入 <see cref="PresentationCommands"/> 同步数组。
    /// 命令单例缺失时整帧跳过。
    /// </summary>
    public sealed class PresentationSyncSystem : JobSystem<PresentationSyncJob>
    {
        protected override void DeclareAccess(AccessBuilder access) => access.Read<LocalToWorld>().Read<PresentationLink>().Write<PresentationCommands>();

        protected override PresentationSyncJob CompileJob(SystemContext ctx)
        {
            var job = new PresentationSyncJob
            {
                L2WSlot = Slot<LocalToWorld>(),
                LinkSlot = Slot<PresentationLink>(),
                HasChannel = 0,
                SyncPositions = default,
                SyncRotations = default,
                SyncScales = default,
            };

            if (ctx.World.TryGetSingleton<PresentationCommands>(out var owner))
            {
                ref var cmds = ref ctx.World.GetComponent<PresentationCommands>(owner);
                if (cmds.IsInitialized)
                {
                    job.HasChannel = 1;
                    job.SyncPositions = cmds.SyncPositions;
                    job.SyncRotations = cmds.SyncRotations;
                    job.SyncScales = cmds.SyncScales;
                }
            }
            return job;
        }
    }
}
