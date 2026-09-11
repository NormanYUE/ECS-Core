using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 本地到世界空间的变换矩阵。通常由变换层级系统根据
    /// LocalTransform + 父级 LocalToWorld 计算并写入。
    /// </summary>
    public struct LocalToWorld : IDataComponent
    {
        /// <summary>本地到世界的 4x4 矩阵。</summary>
        public float4x4 Value;

        /// <summary>世界空间位置（矩阵平移列）。</summary>
        public float3 Position => Value.c3.xyz;

        /// <summary>世界空间右方向（+X）。</summary>
        public float3 Right => Value.c0.xyz;

        /// <summary>世界空间上方向（+Y）。</summary>
        public float3 Up => Value.c1.xyz;

        /// <summary>世界空间前方向（+Z）。</summary>
        public float3 Forward => Value.c2.xyz;

        /// <summary>把本地空间点变换到世界空间。</summary>
        public float3 TransformPoint(float3 point) => math.transform(Value, point);
    }
}
