using Unity.Burst;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 世界包围盒计算作业
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct WorldBoundsJob : IEmberChunkJob
    {
        public int LocalToWorldSlot;
        public int VolumeSlot;
        public int BoundsSlot;

        public void Execute(ChunkJobMeta meta, int chunkIndex)
        {
            for (int i = 0; i < meta.EntityCount; i++) {
                var localToWorld = meta.Read<LocalToWorld>(LocalToWorldSlot, i);
                var volume = meta.Read<BoundingVolume>(VolumeSlot, i);
                FrustumMath.TransformAABB(localToWorld.Value, volume.Center, volume.Extents, out float3 center, out float3 extents);
                ref var bounds = ref meta.Ref<WorldBounds>(BoundsSlot, i);
                bounds.Center = center;
                bounds.Extents = extents;
            }
        }
    }
}