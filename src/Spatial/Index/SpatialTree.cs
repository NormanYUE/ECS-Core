using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间划分树（四叉/八叉统一实现），纯非托管 struct，以单例组件形态存放：
    /// 一个 World 一棵，由 <see cref="SpatialIndexSystem"/> 首次 tick 时创建并初始化，
    /// 业务侧退出前（销毁 ECSManager 之前）对组件 ref 调用 <see cref="Dispose"/> 释放原生容器。
    ///
    /// 存储为 SoA 原生容器 + 空闲链表 + 碰撞分配，稳态零 GC；非线程安全，设计上由
    /// 单个系统串行维护。节点按 ChildCount 成块分配（子节点索引连续）；元素通过
    /// Entity 哈希直索引，插入/删除/移动均为 O(log n) 或 O(1)。AABB 元素若无法完整
    /// 落入任一子节点则停留在当前节点；越出根范围的元素保留在根节点。
    ///
    /// struct 语义：必须通过 ref 使用（<c>ref world.GetComponent<SpatialTree>(owner)</c>），
    /// 值拷贝会丢失对内部容器的写入。全部为 Burst 可编译形态。
    ///
    /// 移除采用标记清扫：<see cref="BeginTick"/> 开启一轮事务，<see cref="Update"/>
    /// 标记存活，<see cref="EndTick"/> 移除本轮未出现的实体——覆盖实体销毁、组件
    /// 被移除等一切消失路径，代价是移除延迟一帧。
    /// </summary>
    public struct SpatialTree : ISingletonComponent, IDisposable
    {
        private const int Null = -1;
        private const float k_InactiveAxisHalfExtent = 1e9f;

        // ---- 配置 ----
        private int m_MaxDepth;
        private int m_NodeCapacity;
        private int m_ChildCount; // 四叉=4，八叉=8
        private int m_Axis1;      // 四叉第二激活轴（XY=1，XZ=2）；八叉不用
        private float3 m_RootCenter;
        private float3 m_RootHalfExtent;

        // ---- 节点存储（块 = ChildCount 个连续节点）----
        private NativeList<int> m_NodeParent;
        private NativeList<int> m_NodeFirstChild; // -1 = 叶子；释放块时用作空闲链表 next
        private NativeList<int> m_NodeElemHead;
        private NativeList<int> m_NodeElemCount;
        private NativeList<float3> m_NodeCenter;
        private NativeList<float3> m_NodeHalfExtent;
        private int m_FreeBlockHead;
        private int m_NodeHighWater; // 已碰撞分配的最大节点索引（不含）

        // ---- 元素存储 ----
        private NativeList<Entity> m_ElemEntity;
        private NativeList<float3> m_ElemCenter;
        private NativeList<float3> m_ElemHalfExtent;
        private NativeList<int> m_ElemNext;
        private NativeList<int> m_ElemPrev;
        private NativeList<int> m_ElemNode;
        private NativeList<int> m_ElemStamp; // 标记清扫：元素最后被 Update 触及的 tick 号
        private int m_FreeElemHead;
        private int m_ElemHighWater;

        // ---- 索引与暂存 ----
        private NativeParallelHashMap<Entity, int> m_ByEntity;
        private NativeList<int> m_QueryStack;      // 查询栈复用，避免每次查询分配
        private NativeList<Entity> m_SweepScratch; // 清扫收集缓冲

        private int m_TickStamp;

        /// <summary>
        /// 树中元素总数。
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// 是否已初始化。单例组件的默认值为未初始化，需 <see cref="Initialize"/> 一次。
        /// </summary>
        public bool IsInitialized => m_ByEntity.IsCreated;

        /// <summary>
        /// 根节点中心。
        /// </summary>
        public float3 RootCenter => m_RootCenter;

        /// <summary>
        /// 根节点半范围。
        /// </summary>
        public float3 RootHalfExtent => m_RootHalfExtent;

        /// <summary>
        /// 按配置分配原生容器并建树。仅可对未初始化的树调用一次。
        /// </summary>
        public void Initialize(in SpatialIndexConfig config, Allocator allocator)
        {
            if (IsInitialized) throw new InvalidOperationException("SpatialTree: already initialized");
            if (config.MaxDepth < 1) throw new ArgumentOutOfRangeException(nameof(config), "MaxDepth must be >= 1");
            if (config.NodeCapacity < 1) throw new ArgumentOutOfRangeException(nameof(config), "NodeCapacity must be >= 1");

            m_MaxDepth = config.MaxDepth;
            m_NodeCapacity = config.NodeCapacity;
            m_RootCenter = config.WorldCenter;

            switch (config.Dimension)
            {
                case SpatialDimension.QuadXY:
                    m_ChildCount = 4;
                    m_Axis1 = 1;
                    m_RootHalfExtent = new float3(config.WorldHalfExtent.x, config.WorldHalfExtent.x,
                        k_InactiveAxisHalfExtent);
                    break;
                case SpatialDimension.QuadXZ:
                    m_ChildCount = 4;
                    m_Axis1 = 2;
                    m_RootHalfExtent = new float3(config.WorldHalfExtent.x, k_InactiveAxisHalfExtent,
                        config.WorldHalfExtent.x);
                    break;
                default:
                    m_ChildCount = 8;
                    m_Axis1 = 1;
                    m_RootHalfExtent = config.WorldHalfExtent;
                    break;
            }

            int initialNodes = m_ChildCount * 4;
            m_NodeParent = NewList<int>(initialNodes, allocator);
            m_NodeFirstChild = NewList<int>(initialNodes, allocator);
            m_NodeElemHead = NewList<int>(initialNodes, allocator);
            m_NodeElemCount = NewList<int>(initialNodes, allocator);
            m_NodeCenter = NewList<float3>(initialNodes, allocator);
            m_NodeHalfExtent = NewList<float3>(initialNodes, allocator);

            const int initialElems = 64;
            m_ElemEntity = NewList<Entity>(initialElems, allocator);
            m_ElemCenter = NewList<float3>(initialElems, allocator);
            m_ElemHalfExtent = NewList<float3>(initialElems, allocator);
            m_ElemNext = NewList<int>(initialElems, allocator);
            m_ElemPrev = NewList<int>(initialElems, allocator);
            m_ElemNode = NewList<int>(initialElems, allocator);
            m_ElemStamp = NewList<int>(initialElems, allocator);

            m_ByEntity = new NativeParallelHashMap<Entity, int>(initialElems, allocator);
            m_QueryStack = NewList<int>(64, allocator);
            m_SweepScratch = new NativeList<Entity>(64, allocator);

            m_FreeBlockHead = Null;
            m_NodeHighWater = 0;
            m_FreeElemHead = Null;
            m_ElemHighWater = 0;
            m_TickStamp = 0;
            Count = 0;

            AllocateBlock(out int root);
            InitNode(root, Null, m_RootCenter, m_RootHalfExtent);
        }

        /// <summary>
        /// 释放全部原生容器。业务侧在销毁 ECSManager 前对单例组件调用一次。
        /// </summary>
        public void Dispose()
        {
            if (!IsInitialized) return;
            m_NodeParent.Dispose();
            m_NodeFirstChild.Dispose();
            m_NodeElemHead.Dispose();
            m_NodeElemCount.Dispose();
            m_NodeCenter.Dispose();
            m_NodeHalfExtent.Dispose();
            m_ElemEntity.Dispose();
            m_ElemCenter.Dispose();
            m_ElemHalfExtent.Dispose();
            m_ElemNext.Dispose();
            m_ElemPrev.Dispose();
            m_ElemNode.Dispose();
            m_ElemStamp.Dispose();
            m_ByEntity.Dispose();
            m_QueryStack.Dispose();
            m_SweepScratch.Dispose();
        }

        /// <summary>
        /// 元素是否已登记。
        /// </summary>
        public bool Contains(Entity entity) => IsInitialized && m_ByEntity.ContainsKey(entity);

        /// <summary>
        /// 插入元素。实体已存在时抛异常（请改用 <see cref="Update"/>）。
        /// </summary>
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

                if (m_NodeElemCount[node] < m_NodeCapacity || depth >= m_MaxDepth) break; // 叶子未满或已达最大深度 → 插入此处

                Subdivide(node, depth);
                // 细分后当前节点有了子节点，回到循环重新选择
            }

            int elem = AllocElement(entity, center, halfExtent);
            m_ElemStamp[elem] = m_TickStamp; // 新插入视为本轮存活
            Attach(elem, node);
        }

        /// <summary>
        /// 移除元素。不存在时返回 false。
        /// </summary>
        public bool Remove(Entity entity)
        {
            if (!m_ByEntity.TryGetValue(entity, out int elem)) return false;
            int node = m_ElemNode[elem];
            Unlink(elem, node);
            FreeElement(elem);
            m_ByEntity.Remove(entity);
            Count--;
            TryCollapse(m_NodeParent[node]);
            return true;
        }

        /// <summary>
        /// 更新元素边界并标记本轮存活。新边界仍在原节点内时原地更新（O(1)，
        /// 最常见路径）；否则移除重插。实体不存在时等同 <see cref="Insert"/>。
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
                m_ElemStamp[elem] = m_TickStamp;
                return;
            }

            Remove(entity);
            Insert(entity, center, halfExtent); // Insert 内部已打 stamp
        }

        /// <summary>
        /// 开启一轮标记清扫事务（每 tick 由维护系统调用一次，与 <see cref="EndTick"/> 配对）。
        /// </summary>
        public void BeginTick() => m_TickStamp++;

        /// <summary>
        /// 结束事务：移除本轮未被 <see cref="Update"/> 触及的实体。覆盖实体销毁、
        /// 包围组件被移除等一切消失路径，代价是移除延迟一帧。
        /// </summary>
        public void EndTick()
        {
            m_SweepScratch.Clear();
            foreach (var kv in m_ByEntity)
            {
                if (m_ElemStamp[kv.Value] != m_TickStamp)
                    m_SweepScratch.Add(kv.Key);
            }
            for (int i = 0; i < m_SweepScratch.Length; i++)
                Remove(m_SweepScratch[i]);
        }

        /// <summary>
        /// 清空全部元素并保持树形收缩到根节点（容器容量保留，无分配）。
        /// </summary>
        public void Clear()
        {
            m_ByEntity.Clear();
            Count = 0;
            m_ElemHighWater = 0;
            m_FreeElemHead = Null;
            m_FreeBlockHead = Null;
            m_NodeHighWater = m_ChildCount; // 保留根块
            InitNode(0, Null, m_RootCenter, m_RootHalfExtent);
        }

        /// <summary>
        /// AABB 范围查询（min/max 为闭区间），命中实体追加到调用方容器。
        /// </summary>
        public void QueryAABB(float3 min, float3 max, ref NativeList<Entity> results)
        {
            float3 queryCenter = (min + max) * 0.5f;
            float3 queryHalf = (max - min) * 0.5f;
            Query(min, max, queryCenter, queryHalf, false, 0f, ref results);
        }

        /// <summary>
        /// 球体范围查询，命中实体追加到调用方容器。
        /// </summary>
        public void QuerySphere(float3 center, float radius, ref NativeList<Entity> results)
        {
            float3 half = new float3(radius);
            Query(center - half, center + half, center, half, true, radius * radius, ref results);
        }

        private void Query(float3 min, float3 max, float3 queryCenter, float3 queryHalf, bool isSphere,
            float sphereRadiusSq, ref NativeList<Entity> results)
        {
            if (Count == 0) return;

            int sp = 0;
            m_QueryStack[sp++] = 0;

            while (sp > 0)
            {
                int node = m_QueryStack[--sp];

                for (int elem = m_NodeElemHead[node]; elem != Null; elem = m_ElemNext[elem])
                {
                    if (!ElementIntersects(elem, min, max, queryCenter, isSphere, sphereRadiusSq)) continue;
                    results.Add(m_ElemEntity[elem]);
                }

                int first = m_NodeFirstChild[node];
                if (first == Null) continue;

                for (int slot = 0; slot < m_ChildCount; slot++)
                {
                    int child = first + slot;
                    if (!AabbIntersects(m_NodeCenter[child], m_NodeHalfExtent[child], queryCenter, queryHalf))
                        continue;
                    if (sp == m_QueryStack.Length)
                        m_QueryStack.Resize(m_QueryStack.Length * 2, NativeArrayOptions.UninitializedMemory);
                    m_QueryStack[sp++] = child;
                }
            }
        }

        private bool ElementIntersects(int elem, float3 min, float3 max, float3 queryCenter, bool isSphere,
            float sphereRadiusSq)
        {
            float3 c = m_ElemCenter[elem];
            float3 e = m_ElemHalfExtent[elem];
            if (!isSphere) return AabbIntersects(c, e, queryCenter, (max - min) * 0.5f);
            float3 closest = math.clamp(queryCenter, c - e, c + e);
            return math.distancesq(closest, queryCenter) <= sphereRadiusSq;
        }

        private bool AabbIntersects(float3 c1, float3 e1, float3 c2, float3 e2)
            => math.all(math.abs(c1 - c2) <= e1 + e2);

        private bool ContainedIn(float3 c, float3 e, float3 nodeC, float3 nodeE)
            => math.all(c - e >= nodeC - nodeE) && math.all(c + e <= nodeC + nodeE);

        // ---- 子槽位与边界（四叉/八叉统一）----

        private int ComputeChildSlot(int nodeIndex, float3 elemCenter, float3 elemHalfExtent)
        {
            float3 nodeC = m_NodeCenter[nodeIndex];
            float3 min = elemCenter - elemHalfExtent;
            float3 max = elemCenter + elemHalfExtent;

            int slot = 0;
            if (elemCenter.x >= nodeC.x)
            {
                if (min.x < nodeC.x) return -1; // 跨中线，无法完整落入
                slot |= 1;
            }
            else if (max.x > nodeC.x) return -1;

            if (m_ChildCount == 8)
            {
                if (elemCenter.y >= nodeC.y)
                {
                    if (min.y < nodeC.y) return -1;
                    slot |= 2;
                }
                else if (max.y > nodeC.y) return -1;

                if (elemCenter.z >= nodeC.z)
                {
                    if (min.z < nodeC.z) return -1;
                    slot |= 4;
                }
                else if (max.z > nodeC.z) return -1;
            }
            else
            {
                int axis1 = m_Axis1;
                if (elemCenter[axis1] >= nodeC[axis1])
                {
                    if (min[axis1] < nodeC[axis1]) return -1;
                    slot |= 2;
                }
                else if (max[axis1] > nodeC[axis1]) return -1;
            }
            return slot;
        }

        private void GetChildBounds(int nodeIndex, int slot, out float3 center, out float3 halfExtent)
        {
            float3 nodeC = m_NodeCenter[nodeIndex];
            halfExtent = m_NodeHalfExtent[nodeIndex];
            center = nodeC;

            if (m_ChildCount == 8)
            {
                halfExtent *= 0.5f;
                center.x += (slot & 1) != 0 ? halfExtent.x : -halfExtent.x;
                center.y += (slot & 2) != 0 ? halfExtent.y : -halfExtent.y;
                center.z += (slot & 4) != 0 ? halfExtent.z : -halfExtent.z;
            }
            else
            {
                halfExtent.x *= 0.5f;
                halfExtent[m_Axis1] *= 0.5f; // 非激活轴保持超大范围
                center.x += (slot & 1) != 0 ? halfExtent.x : -halfExtent.x;
                center[m_Axis1] += (slot & 2) != 0 ? halfExtent[m_Axis1] : -halfExtent[m_Axis1];
            }
        }

        // ---- 子划分与收缩 ----

        private void Subdivide(int node, int depth)
        {
            AllocateBlock(out int first);
            m_NodeFirstChild[node] = first;
            for (int slot = 0; slot < m_ChildCount; slot++)
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
                for (int slot = 0; slot < m_ChildCount; slot++)
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
            m_NodeHighWater += m_ChildCount;
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

        private NativeList<T> NewList<T>(int length, Allocator allocator) where T : unmanaged
        {
            var list = new NativeList<T>(length, allocator);
            list.Resize(length, NativeArrayOptions.UninitializedMemory);
            return list;
        }

        private void GrowElements()
        {
            int capacity = m_ElemEntity.Length * 2;
            m_ElemEntity.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_ElemCenter.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_ElemHalfExtent.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_ElemNext.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_ElemPrev.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_ElemNode.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_ElemStamp.Resize(capacity, NativeArrayOptions.UninitializedMemory);
        }

        private void GrowNodes()
        {
            int capacity = m_NodeParent.Length * 2;
            m_NodeParent.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_NodeFirstChild.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_NodeElemHead.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_NodeElemCount.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_NodeCenter.Resize(capacity, NativeArrayOptions.UninitializedMemory);
            m_NodeHalfExtent.Resize(capacity, NativeArrayOptions.UninitializedMemory);
        }
    }
}
