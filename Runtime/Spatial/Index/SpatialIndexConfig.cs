using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间索引配置（单例）。<see cref="SpatialIndexSystem"/> 在首次 tick 时读取并建树；
    /// 不存在该单例时使用内置默认值（八叉树、原点中心、1000 半范围、深度 8、容量 8）。
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
    }
}
