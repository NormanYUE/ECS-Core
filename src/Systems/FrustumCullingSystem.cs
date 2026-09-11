using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 视锥剔除系统。读取 <see cref="CameraFrustum"/> 单例，对带
    /// <see cref="LocalToWorld"/> + <see cref="BoundingVolume"/> 的实体做
    /// 世界包围盒-视锥测试，仅在进出视口的边沿增删 <see cref="InView"/> 标记
    /// （结构变更与迁移数量正比于边沿实体数，而非全量实体数）。
    /// <see cref="CameraFrustum"/> 单例不存在时整帧跳过。
    /// </summary>
    public sealed class FrustumCullingSystem : SystemBase
    {
        private static readonly EntityQuery s_InViewQuery =
            EntityQuery.With<LocalToWorld, BoundingVolume, InView>();

        private static readonly EntityQuery s_OutOfViewQuery =
            EntityQuery.With<LocalToWorld, BoundingVolume>().None<InView>();

        protected override void DeclareAccess(AccessBuilder access)
            => access.Read<LocalToWorld>().Read<BoundingVolume>().Write<InView>().StructuralChanges();

        protected override void OnTick(SystemContext ctx)
        {
            if (!ctx.World.TryGetSingleton<CameraFrustum>(out var cameraOwner)) return;
            CameraFrustum frustum = ctx.World.GetComponent<CameraFrustum>(cameraOwner);

            // 已在视口内：离开者摘除标记
            foreach (var chunk in ctx.QueryChunks(s_InViewQuery))
            {
                var localToWorlds = chunk.Read<LocalToWorld>();
                var volumes = chunk.Read<BoundingVolume>();
                for (int row = 0; row < chunk.Count; row++)
                {
                    FrustumMath.TransformAABB(localToWorlds[row].Value,
                        volumes[row].Center, volumes[row].Extents,
                        out float3 center, out float3 extents);
                    if (!FrustumMath.IntersectsAABB(frustum, center, extents))
                        ctx.ECB.RemoveComponent<InView>(chunk.EntityAt(row));
                }
            }

            // 视口外：进入者挂上标记
            foreach (var chunk in ctx.QueryChunks(s_OutOfViewQuery))
            {
                var localToWorlds = chunk.Read<LocalToWorld>();
                var volumes = chunk.Read<BoundingVolume>();
                for (int row = 0; row < chunk.Count; row++)
                {
                    FrustumMath.TransformAABB(localToWorlds[row].Value,
                        volumes[row].Center, volumes[row].Extents,
                        out float3 center, out float3 extents);
                    if (FrustumMath.IntersectsAABB(frustum, center, extents))
                        ctx.ECB.AddComponent(chunk.EntityAt(row), new InView());
                }
            }
        }
    }
}
