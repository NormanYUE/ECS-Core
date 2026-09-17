using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 表现层同步作业（Burst 可编译）。把可见实体的 <see cref="LocalToWorld"/>
    /// 分解为 TRS，按 <see cref="PresentationLink.Slot"/> 写入同步数组，
    /// 供 TransformWriteJob 批量应用到 GameObject。无 Entity 访问需求，可完全并行。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct PresentationSyncJob : IEmberChunkJob
    {
        /// <summary>
        /// 0 = 命令单例未初始化，整帧跳过。
        /// </summary>
        public byte HasChannel;

        public int L2WSlot;
        public int LinkSlot;

        [NativeDisableParallelForRestriction] public NativeList<float3> SyncPositions;
        [NativeDisableParallelForRestriction] public NativeList<quaternion> SyncRotations;
        [NativeDisableParallelForRestriction] public NativeList<float3> SyncScales;

        public void Execute(ChunkJobMeta meta, int chunkIndex)
        {
            if (HasChannel == 0) return;

            for (int i = 0; i < meta.EntityCount; i++)
            {
                int slot = meta.Read<PresentationLink>(LinkSlot, i).Slot;
                TransformDecompose.Decompose(meta.Read<LocalToWorld>(L2WSlot, i).Value, out float3 pos, out quaternion rot, out float3 scale);
                SyncPositions[slot] = pos;
                SyncRotations[slot] = rot;
                SyncScales[slot] = scale;
            }
        }
    }
}
