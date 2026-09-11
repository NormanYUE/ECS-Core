using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间树元素（<see cref="SpatialTree"/> 的内部存储，AoS 布局）。
    /// 存放于 World 托管 buffer，随 World 释放；业务代码不应直接使用。
    /// </summary>
    internal struct TreeElement
    {
        /// <summary>元素对应实体（含版本，用于直索引映射的槽复用校验）。</summary>
        public Entity Entity;

        /// <summary>元素中心。</summary>
        public float3 Center;

        /// <summary>元素半范围。</summary>
        public float3 HalfExtent;

        /// <summary>同节点链表 next（-1 = 尾）；空闲时复用为空闲链表 next。</summary>
        public int Next;

        /// <summary>同节点链表 prev（-1 = 头）。</summary>
        public int Prev;

        /// <summary>所属节点索引。</summary>
        public int Node;

        /// <summary>标记清扫戳：最后被 Update 触及的 tick 号；-1 = 空闲槽。</summary>
        public int Stamp;
    }
}
