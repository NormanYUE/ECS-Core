using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace Ember.Core
{
    /// <summary>
    /// Transform 批量回写作业（Burst 可编译）。把 <see cref="PresentationCommands"/>
    /// 同步数组中的 TRS 应用到 TransformAccessArray 登记的 GameObject Transform，
    /// 主线程零逐 GO 写。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct TransformWriteJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<quaternion> Rotations;
        [ReadOnly] public NativeArray<float3> Scales;

        public void Execute(int index, TransformAccess transform)
        {
            transform.SetPositionAndRotation(Positions[index], Rotations[index]);
            transform.localScale = Scales[index];
        }
    }
}
