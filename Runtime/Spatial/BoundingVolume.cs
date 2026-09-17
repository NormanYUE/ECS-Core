using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 本地空间轴对齐包围盒（AABB）。配合 <see cref="LocalToWorld"/> 由系统在
    /// 运行时换算世界包围盒，用于空间索引与视锥剔除。
    /// </summary>
    public struct BoundingVolume : IDataComponent
    {
        /// <summary>本地空间中心。</summary>
        public float3 Center;

        /// <summary>本地空间半范围（各轴全长的一半）。</summary>
        public float3 Extents;

        public BoundingVolume(float3 center, float3 extents)
        {
            Center = center;
            Extents = extents;
        }

        /// <summary>包围球半径（包围盒外接球），供球体粗略剔除使用。</summary>
        public float BoundingRadius => math.length(Extents);
    }
}
