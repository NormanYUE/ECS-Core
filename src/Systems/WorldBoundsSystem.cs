namespace Ember.Core
{
    /// <summary>
    /// 世界包围盒系统（Job·Burst）：把 <see cref="LocalToWorld"/> + <see cref="BoundingVolume"/>
    /// 换算为 <see cref="WorldBounds"/>。需在 <see cref="SpatialSetupSystem"/> 之后注册，
    /// 依赖图的读写冲突声明会自动把下游（剔除/索引）排在本系统之后。
    /// </summary>
    public sealed class WorldBoundsSystem : JobSystem<WorldBoundsJob>
    {
        protected override void DeclareAccess(AccessBuilder access) => access.Read<LocalToWorld>().Read<BoundingVolume>().Write<WorldBounds>();

        protected override WorldBoundsJob CompileJob(SystemContext ctx) => new()
        {
            LocalToWorldSlot = Slot<LocalToWorld>(),
            VolumeSlot = Slot<BoundingVolume>(),
            BoundsSlot = Slot<WorldBounds>(),
        };
    }
}