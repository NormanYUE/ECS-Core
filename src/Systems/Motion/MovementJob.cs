using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 运动积分作业（每个 Chunk 一次并行执行）：
    /// 位置 <c>Position += LinearVelocity * dt</c>；
    /// 旋转按角速度轴角积分（方向为旋转轴，模长为弧度/秒）。
    /// 不含 Static / Disabled 的排除 —— 由 <see cref="MovementSystem"/> 的
    /// 查询掩码在 Chunk 粒度保证（Tag 只占 Archetype 掩码、没有列）。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct MovementJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<MovementChunkInfo> Infos;

        /// <summary>缩放后的时间步长（WorldTime.DeltaTime）。</summary>
        public float DeltaTime;

        public void Execute(int chunkIndex)
        {
            unsafe
            {
                MovementChunkInfo info = Infos[chunkIndex];
                int count = info.Count;
                if (count <= 0 || DeltaTime <= 0f) return;

                var transforms = (LocalTransform*)info.TransformPtr;

                if (info.LinearPtr != 0)
                {
                    var velocities = (LinearVelocity*)info.LinearPtr;
                    for (int row = 0; row < count; row++)
                    {
                        float3 displacement = velocities[row].Value * DeltaTime;
                        if (math.any(displacement != float3.zero))
                        {
                            var t = transforms[row];
                            t.Position += displacement;
                            transforms[row] = t;
                        }
                    }
                }

                if (info.AngularPtr != 0)
                {
                    var angulars = (AngularVelocity*)info.AngularPtr;
                    for (int row = 0; row < count; row++)
                    {
                        float3 axisAngle = angulars[row].Value * DeltaTime;
                        float magnitude = math.length(axisAngle);
                        if (magnitude <= 1e-9f) continue;

                        quaternion delta = quaternion.AxisAngle(axisAngle / magnitude, magnitude);
                        var t = transforms[row];
                        t.Rotation = math.normalize(math.mul(t.Rotation, delta));
                        transforms[row] = t;
                    }
                }
            }
        }
    }
}
