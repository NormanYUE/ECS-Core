using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>空间索引维度模式。</summary>
    public enum SpatialDimension : byte
    {
        /// <summary>XY 平面四叉树（2D）。</summary>
        QuadXY = 0,

        /// <summary>XZ 平面四叉树（3D 俯视/地面场景）。</summary>
        QuadXZ = 1,

        /// <summary>三维八叉树。</summary>
        Octree = 2,
    }

    /// <summary>
    /// 空间索引配置（单例）。<see cref="SpatialIndexSystem"/> 在首次 tick 时读取并建树；
    /// 不存在该单例时使用 <see cref="Default"/>。
    /// </summary>
    public struct SpatialIndexConfig : ISingletonComponent
    {
        /// <summary>维度模式。</summary>
        public SpatialDimension Dimension;

        /// <summary>索引覆盖的世界中心。</summary>
        public float3 WorldCenter;

        /// <summary>索引覆盖的世界半范围（四叉树仅激活轴有效）。</summary>
        public float3 WorldHalfExtent;

        /// <summary>树最大深度。</summary>
        public int MaxDepth;

        /// <summary>节点元素容量（超过才细分）。</summary>
        public int NodeCapacity;

        /// <summary>默认配置：八叉树、原点为中心、1000 半范围、深度 8、容量 8。</summary>
        public static SpatialIndexConfig Default => new SpatialIndexConfig
        {
            Dimension = SpatialDimension.Octree,
            WorldCenter = float3.zero,
            WorldHalfExtent = new float3(1000f),
            MaxDepth = 8,
            NodeCapacity = 8,
        };
    }
}
