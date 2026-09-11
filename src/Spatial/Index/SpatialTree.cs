using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间划分树引擎（四叉/八叉共用核心）。SoA 预分配数组 + 空闲链表 + 碰撞分配，
    /// 稳态运行零 GC；非线程安全，设计上由单个系统串行维护。
    ///
    /// 节点按 ChildCount 成块分配（子节点索引连续）；元素通过 Entity 字典直索引，
    /// 插入/删除/移动均为 O(log n) 或 O(1)。AABB 元素若无法完整落入任一子节点，
    /// 则停留在当前节点；越出根范围的元素保留在根节点。
    /// </summary>
    public abstract class SpatialTree
    {
        protected const int Null = -1;
        private const float k_InactiveAxisHalfExtent = 1e9f;

        private readonly int m_MaxDepth;
        private readonly int m_NodeCapacity;

        // ---- 节点存储（块 = ChildCount 个连续节点）----
        private int[] m_NodeParent;
        private int[] m_NodeFirstChild; // -1 = 叶子；释放块时用作空闲链表 next
        private int[] m_NodeElemHead;
        private int[] m_NodeElemCount;
        private float3[] m_NodeCenter;
        private float3[] m_NodeHalfExtent;
        private int m_FreeBlockHead = Null;
        private int m_NodeHighWater; // 已碰撞分配的最大节点索引（不含）

        // ---- 元素存储 ----
        private Entity[] m_ElemEntity;
        private float3[] m_ElemCenter;
        private float3[] m_ElemHalfExtent;
        private int[] m_ElemNext;
        private int[] m_ElemPrev;
        private int[] m_ElemNode;
        private int m_FreeElemHead = Null;
        private int m_ElemHighWater;

        // ---- 查询栈（复用，避免每次查询分配）----
        private int[] m_QueryStack = new int[64];

        private readonly Dictionary<Entity, int> m_ByEntity = new Dictionary<Entity, int>();

        /// <summary>树中元素总数。</summary>
        public int Count { get; private set; }

        /// <summary>每个节点的子节点数（四叉=4，八叉=8）。</summary>
        protected abstract int ChildCount { get; }

        /// <summary>根节点中心。</summary>
        public float3 RootCenter => m_NodeCenter[0];

        /// <summary>根节点半范围。</summary>
        public float3 RootHalfExtent => m_NodeHalfExtent[0];

        protected SpatialTree(float3 rootCenter, float3 rootHalfExtent, int maxDepth, int nodeCapacity)
        {
            if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth), "maxDepth must be >= 1");
            if (nodeCapacity < 1) throw new ArgumentOutOfRangeException(nameof(nodeCapacity), "nodeCapacity must be >= 1");
            m_MaxDepth = maxDepth;
            m_NodeCapacity = nodeCapacity;

            int blockNodes = ChildCount;
            m_NodeParent = new int[blockNodes * 4];
            m_NodeFirstChild = new int[blockNodes * 4];
            m_NodeElemHead = new int[blockNodes * 4];
            m_NodeElemCount = new int[blockNodes * 4];
            m_NodeCenter = new float3[blockNodes * 4];
            m_NodeHalfExtent = new float3[blockNodes * 4];

            m_ElemEntity = new Entity[64];
            m_ElemCenter = new float3[64];
            m_ElemHalfExtent = new float3[64];
            m_ElemNext = new int[64];
            m_ElemPrev = new int[64];
            m_ElemNode = new int[64];

            AllocateBlock(out int root);
            InitNode(root, Null, rootCenter, rootHalfExtent);
        }

        /// <summary>非激活轴（四叉树忽略的深度轴）使用的超大半范围。</summary>
        protected static float InactiveAxisHalfExtent => k_InactiveAxisHalfExtent;

        /// <summary>读取节点中心（子类计算子槽位/边界用）。</summary>
        protected float3 NodeCenterAt(int node) => m_NodeCenter[node];

        /// <summary>读取节点半范围（子类计算子槽位/边界用）。</summary>
        protected float3 NodeHalfExtentAt(int node) => m_NodeHalfExtent[node];

        /// <summary>
        /// 计算元素完整落入的子节点槽位 [0, ChildCount)；
        /// 跨越或越出所有子节点时返回 -1（元素应停留在当前节点）。
        /// </summary>
        protected abstract int ComputeChildSlot(int nodeIndex, float3 elemCenter, float3 elemHalfExtent);

        /// <summary>计算子节点边界。</summary>
        protected abstract void GetChildBounds(int nodeIndex, int slot, out float3 center, out float3 halfExtent);

        /// <summary>元素是否已登记。</summary>
        public bool Contains(Entity entity) => m_ByEntity.ContainsKey(entity);

        /// <summary>插入元素。实体已存在时抛异常（请改用 <see cref="Update"/>）。</summary>
        public void Insert(Entity entity, float3 center, float3 halfExtent)
        {
            if (m_ByEntity.ContainsKey(entity))
                throw new InvalidOperationException($"SpatialTree: entity {entity} already inserted");

            int node = 0;
            int depth = 0;
            while (true)
            {
                if (m_NodeFirstChild[node] != Null)
                {
                    int slot = ComputeChildSlot(node, center, halfExtent);
                    if (slot >= 0)
                    {
                        node = m_NodeFirstChild[node] + slot;
                        depth++;
                        continue;
                    }
                    break; // 无法完整落入子节点 → 停留在当前节点
                }

                if (m_NodeElemCount[node] < m_NodeCapacity || depth >= m_MaxDepth)
                    break; // 叶子未满或已达最大深度 → 插入此处

                Subdivide(node, depth);
                // 细分后当前节点有了子节点，回到循环重新选择
            }

            Attach(AllocElement(entity, center, halfExtent), node);
        }

        /// <summary>移除元素。不存在时返回 false。</summary>
        public bool Remove(Entity entity)
        {
            if (!m_ByEntity.TryGetValue(entity, out int elem)) return false;
            int node = m_ElemNode[elem];
            Detach(elem, node);
            FreeElement(elem);
            m_ByEntity.Remove(entity);
            Count--;
            TryCollapse(m_NodeParent[node]);
            return true;
        }

        /// <summary>
        /// 更新元素边界。新边界仍在原节点内时原地更新（O(1)，最常见路径）；
        /// 否则移除重插。实体不存在时等同 <see cref="Insert"/>。
        /// </summary>
        public void Update(Entity entity, float3 center, float3 halfExtent)
        {
            if (!m_ByEntity.TryGetValue(entity, out int elem))
            {
                Insert(entity, center, halfExtent);
                return;
            }

            int node = m_ElemNode[elem];
            if (ContainedIn(center, halfExtent, m_NodeCenter[node], m_NodeHalfExtent[node]))
            {
                m_ElemCenter[elem] = center;
                m_ElemHalfExtent[elem] = halfExtent;
                return;
            }

            Remove(entity);
            Insert(entity, center, halfExtent);
        }

        /// <summary>清空全部元素并保持树形收缩到根节点（数组容量保留，无分配）。</summary>
        public void Clear()
        {
            m_ByEntity.Clear();
            Count = 0;
            m_ElemHighWater = 0;
            m_FreeElemHead = Null;
            m_FreeBlockHead = Null;
            m_NodeHighWater = ChildCount; // 保留根块
            InitNode(0, Null, m_NodeCenter[0], m_NodeHalfExtent[0]);
        }

        /// <summary>AABB 范围查询（min/max 为闭区间）。</summary>
        public void QueryAABB<TVisitor>(float3 min, float3 max, ref TVisitor visitor)
            where TVisitor : ISpatialVisitor
        {
            float3 queryCenter = (min + max) * 0.5f;
            float3 queryHalf = (max - min) * 0.5f;
            Query(min, max, queryCenter, queryHalf, isSphere: false, sphereRadiusSq: 0f, ref visitor);
        }

        /// <summary>球体范围查询。</summary>
        public void QuerySphere<TVisitor>(float3 center, float radius, ref TVisitor visitor)
            where TVisitor : ISpatialVisitor
        {
            float3 half = new float3(radius);
            Query(center - half, center + half, center, half, isSphere: true, sphereRadiusSq: radius * radius,
                ref visitor);
        }

        /// <summary>AABB 查询并把结果填充到调用方列表（返回命中数）。</summary>
        public int QueryAABB(float3 min, float3 max, List<Entity> results)
        {
            var visitor = new EntityListVisitor(results);
            QueryAABB(min, max, ref visitor);
            return results.Count;
        }

        /// <summary>球体查询并把结果填充到调用方列表（返回命中数）。</summary>
        public int QuerySphere(float3 center, float radius, List<Entity> results)
        {
            var visitor = new EntityListVisitor(results);
            QuerySphere(center, radius, ref visitor);
            return results.Count;
        }

        private void Query<TVisitor>(float3 min, float3 max, float3 queryCenter, float3 queryHalf,
            bool isSphere, float sphereRadiusSq, ref TVisitor visitor)
            where TVisitor : ISpatialVisitor
        {
            if (Count == 0) return;

            int[] stack = m_QueryStack;
            int sp = 0;
            stack[sp++] = 0;

            while (sp > 0)
            {
                int node = stack[--sp];

                for (int elem = m_NodeElemHead[node]; elem != Null; elem = m_ElemNext[elem])
                {
                    if (!ElementIntersects(elem, min, max, queryCenter, isSphere, sphereRadiusSq)) continue;
                    if (!visitor.Visit(m_ElemEntity[elem], m_ElemCenter[elem], m_ElemHalfExtent[elem]))
                        return;
                }

                int first = m_NodeFirstChild[node];
                if (first == Null) continue;

                for (int slot = 0; slot < ChildCount; slot++)
                {
                    int child = first + slot;
                    if (!AabbIntersects(m_NodeCenter[child], m_NodeHalfExtent[child], queryCenter, queryHalf))
                        continue;
                    if (sp == stack.Length)
                    {
                        Array.Resize(ref m_QueryStack, stack.Length * 2);
                        stack = m_QueryStack;
                    }
                    stack[sp++] = child;
                }
            }
        }

        private bool ElementIntersects(int elem, float3 min, float3 max, float3 queryCenter,
            bool isSphere, float sphereRadiusSq)
        {
            float3 c = m_ElemCenter[elem];
            float3 e = m_ElemHalfExtent[elem];
            if (!isSphere)
                return AabbIntersects(c, e, queryCenter, (max - min) * 0.5f);

            float3 closest = math.clamp(queryCenter, c - e, c + e);
            return math.distancesq(closest, queryCenter) <= sphereRadiusSq;
        }

        internal static bool AabbIntersects(float3 c1, float3 e1, float3 c2, float3 e2)
            => math.all(math.abs(c1 - c2) <= e1 + e2);

        internal static bool ContainedIn(float3 c, float3 e, float3 nodeC, float3 nodeE)
            => math.all(c - e >= nodeC - nodeE) && math.all(c + e <= nodeC + nodeE);

        // ---- 子划分与收缩 ----

        private void Subdivide(int node, int depth)
        {
            AllocateBlock(out int first);
            m_NodeFirstChild[node] = first;
            for (int slot = 0; slot < ChildCount; slot++)
            {
                GetChildBounds(node, slot, out float3 c, out float3 e);
                InitNode(first + slot, node, c, e);
            }

            // 下放能完整落入子节点的存量元素
            int elem = m_NodeElemHead[node];
            while (elem != Null)
            {
                int next = m_ElemNext[elem];
                int slot = ComputeChildSlot(node, m_ElemCenter[elem], m_ElemHalfExtent[elem]);
                if (slot >= 0)
                {
                    Unlink(elem, node);
                    m_NodeElemCount[node]--;
                    Link(elem, first + slot);
                }
                elem = next;
            }
        }

        private void TryCollapse(int node)
        {
            while (node != Null && m_NodeFirstChild[node] != Null)
            {
                int first = m_NodeFirstChild[node];
                bool allEmptyLeaves = true;
                for (int slot = 0; slot < ChildCount; slot++)
                {
                    int child = first + slot;
                    if (m_NodeFirstChild[child] != Null || m_NodeElemCount[child] != 0)
                    {
                        allEmptyLeaves = false;
                        break;
                    }
                }
                if (!allEmptyLeaves) break;

                FreeBlock(first);
                m_NodeFirstChild[node] = Null;
                node = m_NodeParent[node];
            }
        }

        // ---- 元素/节点的分配与链接 ----

        private int AllocElement(Entity entity, float3 center, float3 halfExtent)
        {
            int elem;
            if (m_FreeElemHead != Null)
            {
                elem = m_FreeElemHead;
                m_FreeElemHead = m_ElemNext[elem];
            }
            else
            {
                elem = m_ElemHighWater++;
                if (elem >= m_ElemEntity.Length) GrowElements();
            }

            m_ElemEntity[elem] = entity;
            m_ElemCenter[elem] = center;
            m_ElemHalfExtent[elem] = halfExtent;
            return elem;
        }

        private void FreeElement(int elem)
        {
            m_ElemNext[elem] = m_FreeElemHead;
            m_FreeElemHead = elem;
        }

        private void Attach(int elem, int node)
        {
            Link(elem, node);
            m_ByEntity.Add(m_ElemEntity[elem], elem);
            Count++;
        }

        private void Detach(int elem, int node) => Unlink(elem, node);

        private void Link(int elem, int node)
        {
            int head = m_NodeElemHead[node];
            m_ElemNext[elem] = head;
            m_ElemPrev[elem] = Null;
            if (head != Null) m_ElemPrev[head] = elem;
            m_NodeElemHead[node] = elem;
            m_ElemNode[elem] = node;
            m_NodeElemCount[node]++;
        }

        private void Unlink(int elem, int node)
        {
            int prev = m_ElemPrev[elem];
            int next = m_ElemNext[elem];
            if (prev != Null) m_ElemNext[prev] = next;
            else m_NodeElemHead[node] = next;
            if (next != Null) m_ElemPrev[next] = prev;
            m_NodeElemCount[node]--;
        }

        private void AllocateBlock(out int blockStart)
        {
            if (m_FreeBlockHead != Null)
            {
                blockStart = m_FreeBlockHead;
                m_FreeBlockHead = m_NodeFirstChild[blockStart];
                return;
            }

            blockStart = m_NodeHighWater;
            m_NodeHighWater += ChildCount;
            if (m_NodeHighWater > m_NodeParent.Length) GrowNodes();
        }

        private void FreeBlock(int blockStart)
        {
            m_NodeFirstChild[blockStart] = m_FreeBlockHead;
            m_FreeBlockHead = blockStart;
        }

        private void InitNode(int node, int parent, float3 center, float3 halfExtent)
        {
            m_NodeParent[node] = parent;
            m_NodeFirstChild[node] = Null;
            m_NodeElemHead[node] = Null;
            m_NodeElemCount[node] = 0;
            m_NodeCenter[node] = center;
            m_NodeHalfExtent[node] = halfExtent;
        }

        private void GrowElements()
        {
            int capacity = m_ElemEntity.Length * 2;
            Array.Resize(ref m_ElemEntity, capacity);
            Array.Resize(ref m_ElemCenter, capacity);
            Array.Resize(ref m_ElemHalfExtent, capacity);
            Array.Resize(ref m_ElemNext, capacity);
            Array.Resize(ref m_ElemPrev, capacity);
            Array.Resize(ref m_ElemNode, capacity);
        }

        private void GrowNodes()
        {
            int capacity = m_NodeParent.Length * 2;
            Array.Resize(ref m_NodeParent, capacity);
            Array.Resize(ref m_NodeFirstChild, capacity);
            Array.Resize(ref m_NodeElemHead, capacity);
            Array.Resize(ref m_NodeElemCount, capacity);
            Array.Resize(ref m_NodeCenter, capacity);
            Array.Resize(ref m_NodeHalfExtent, capacity);
        }
    }
}
