using Unity.Burst;

namespace Ember.Core
{
    /// <summary>
    /// 视锥剔除作业（Burst 可编译）。相机单例缺失时 HasCamera=0，整帧跳过。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct FrustumCullingJob : IEmberChunkJob
    {
        /// <summary>
        /// 本帧视锥（CompileJob 时从单例拷贝进作业字段）。
        /// </summary>
        public CameraFrustum Frustum;

        /// <summary>
        /// 0 = 无相机，跳过执行。
        /// </summary>
        public byte HasCamera;

        public int BoundsSlot;
        public int StateSlot;

        public void Execute(ChunkJobMeta meta, int chunkIndex)
        {
            if (HasCamera == 0) return;

            for (int i = 0; i < meta.EntityCount; i++)
            {
                var bounds = meta.Read<WorldBounds>(BoundsSlot, i);
                bool visible = FrustumMath.IntersectsAABB(Frustum, bounds.Center, bounds.Extents);
                ref var state = ref meta.Ref<VisibilityState>(StateSlot, i);
                state.InView = visible;
            }
        }
    }
}
