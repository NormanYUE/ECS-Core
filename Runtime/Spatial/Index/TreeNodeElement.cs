using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间树节点元素（<see cref="SpatialTree"/> 的内部存储，AoS 布局）。
    /// 存放于 World 托管 buffer，随 World 释放；业务代码不应直接使用。
    /// </summary>
    internal struct TreeNodeElement
    {
        /// <summary>父节点索引（-1 = 根）。</summary>
        public int Parent;

        /// <summary>首个子节点索引（-1 = 叶子）；块释放时复用为空闲链表 next。</summary>
        public int FirstChild;

        /// <summary>元素链表头（-1 = 空）。</summary>
        public int ElemHead;

        /// <summary>节点内元素数。</summary>
        public int ElemCount;

        /// <summary>节点中心。</summary>
        public float3 Center;

        /// <summary>节点半范围。</summary>
        public float3 HalfExtent;
    }
}
