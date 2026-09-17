using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间树单例组件：只存标量与 World 托管 buffer 句柄，纯 blittable，
    /// <b>无需 Dispose</b>——全部存储都在 World 的 buffer store 里，随 World.Dispose 自动释放。
    /// 一个 World 一棵，由 <see cref="SpatialIndexSystem"/> 首次 tick 时创建并初始化。
    ///
    /// 所有操作经 <see cref="SpatialTreeView"/> 进行（<c>world.GetSpatialTree()</c> 获取）：
    /// 组件自身不持容器、不做算法。实体→元素索引采用 Entity.Index 直索引稀疏映射
    ///（O(1)，元素内版本号校验槽复用），节点/元素按 ChildCount 块分配 + 空闲链表，
    /// 稳态零 GC；消失实体由标记清扫剔除（延迟一帧）。
    /// </summary>
    public struct SpatialTree : ISingletonComponent
    {
        /// <summary>节点存储（TreeNodeElement）。</summary>
        internal BufferHandle Nodes;

        /// <summary>元素存储（TreeElement）。</summary>
        internal BufferHandle Elems;

        /// <summary>实体直索引映射（int：元素索引+1，0=未登记）。</summary>
        internal BufferHandle EntityMap;

        /// <summary>查询栈暂存（int）。</summary>
        internal BufferHandle QueryStack;

        /// <summary>最大深度。</summary>
        public int MaxDepth;

        /// <summary>节点元素容量（超过才细分）。</summary>
        public int NodeCapacity;

        /// <summary>每个节点的子节点数（四叉=4，八叉=8）。</summary>
        public int ChildCount;

        /// <summary>四叉第二激活轴（XY=1，XZ=2）；八叉不用。</summary>
        public int Axis1;

        /// <summary>根节点中心。</summary>
        public float3 RootCenter;

        /// <summary>根节点半范围。</summary>
        public float3 RootHalfExtent;

        /// <summary>空闲节点块链表头（-1 = 无）。</summary>
        public int FreeBlockHead;

        /// <summary>已碰撞分配的最大节点索引（不含）。</summary>
        public int NodeHighWater;

        /// <summary>空闲元素链表头（-1 = 无）。</summary>
        public int FreeElemHead;

        /// <summary>已碰撞分配的最大元素索引（不含）。</summary>
        public int ElemHighWater;

        /// <summary>当前 tick 戳（标记清扫用）。</summary>
        public int TickStamp;

        /// <summary>树中元素总数。</summary>
        public int Count;

        /// <summary>是否已初始化（buffer 句柄已分配）。</summary>
        public readonly bool IsInitialized => !Nodes.IsNull;
    }
}
