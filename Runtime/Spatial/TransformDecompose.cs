using Unity.Burst;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 变换分解纯函数（工具类，静态豁免）。从世界矩阵提取 TRS，
    /// 供表现层把实体变换同步给 GameObject。
    /// 假设基向量正交（无 shear）；非正交矩阵的旋转结果未定义。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static class TransformDecompose
    {
        /// <summary>
        /// 把 4x4 世界矩阵分解为位置、旋转、缩放。
        /// 缩放取各基向量长度（总是正值）；旋转由前/上方向重建。
        /// </summary>
        public static void Decompose(in float4x4 m, out float3 position, out quaternion rotation, out float3 scale)
        {
            position = m.c3.xyz;
            scale = new float3(math.length(m.c0.xyz), math.length(m.c1.xyz), math.length(m.c2.xyz));
            var up = m.c1.xyz / scale.y;
            var forward = m.c2.xyz / scale.z;
            var right = math.normalize(math.cross(up, forward));
            up = math.normalize(math.cross(forward, right));
            rotation = quaternion.LookRotationSafe(forward, up);
        }
    }
}
