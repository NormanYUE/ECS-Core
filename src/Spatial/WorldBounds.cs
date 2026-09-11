using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 世界空间轴对齐包围盒（AABB）。由 <see cref="WorldBoundsSystem"/>（Job·Burst）
    /// 每帧从 <see cref="LocalToWorld"/> + <see cref="BoundingVolume"/> 计算写入，
    /// 是空间索引与视锥剔除的共同输入——TransformAABB 每帧每实体只算一次。
    /// 缺失本组件的实体会被 <see cref="SpatialSetupSystem"/> 自动补齐。
    /// </summary>
    public struct WorldBounds : IDataComponent
    {
        /// <summary>世界空间中心。</summary>
        public float3 Center;

        /// <summary>世界空间半范围。</summary>
        public float3 Extents;

        public WorldBounds(float3 center, float3 extents)
        {
            Center = center;
            Extents = extents;
        }
    }
}
