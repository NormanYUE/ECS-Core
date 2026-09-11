using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间树操作视图（轻量 struct：World + 单例实体）。所有算法在此进行：
    /// 标量经组件 ref 读写，节点/元素/映射/栈经 <see cref="BufferSpan{T}"/> 操作，
    /// span 在每次公开调用内新鲜获取、且任何 buffer 扩容后立即重取
    /// （扩容会在 store 内搬移数据，旧 span 失效）。
    /// 经 <c>world.GetSpatialTree()</c> / <c>world.TryGetSpatialTree(out ...)</c> 获取；
    /// 无任何资源需要 Dispose——存储全部由 World 托管。
    /// </summary>
    public struct SpatialTreeView
    {
        private const int Null = -1;
        private const float k_InactiveAxisHalfExtent = 1e9f;

        private readonly World m_World;
        private readonly Entity m_Owner;

        internal SpatialTreeView(World world, Entity owner)
        {
            m_World = world;
            m_Owner = owner;
        }

        private ref SpatialTree State => ref m_World.GetComponent<SpatialTree>(m_Owner);

        private BufferSpan<TreeNodeElement> Nodes => m_World.GetBuffer<TreeNodeElement>(State.Nodes);
        private BufferSpan<TreeElement> Elems => m_World.GetBuffer<TreeElement>(State.Elems);
        private BufferSpan<int> Map => m_World.GetBuffer<int>(State.EntityMap);
        private BufferSpan<int> Stack => m_World.GetBuffer<int>(State.QueryStack);

        /// <summary>树中元素总数。</summary>
        public int Count => State.Count;

        /// <summary>根节点中心。</summary>
        public float3 RootCenter => State.RootCenter;

        /// <summary>根节点半范围。</summary>
        public float3 RootHalfExtent => State.RootHalfExtent;

        /// <summary>元素是否已登记（含实体槽版本校验）。</summary>
        public bool Contains(Entity entity) => TryLocate(entity, out _);

        /// <summary>
        /// 插入元素。实体已存在时抛异常（请改用 <see cref="Update"/>）。
        /// </summary>
        public void Insert(Entity entity, float3 center, float3 halfExtent)
        {
            if (TryLocate(entity, out _))
                throw new InvalidOperationException($"SpatialTree: entity {entity} already inserted");

            ref var state = ref State;
            int node = 0;
            int depth = 0;
            while (true)
            {
                var nodes = Nodes;
                if (nodes[node].FirstChild != Null)
                {
                    int slot = ComputeChildSlot(nodes, node, center, halfExtent);
                    if (slot >= 0)
                    {
                        node = nodes[node].FirstChild + slot;
                        depth++;
                        continue;
                    }
                    break; // 无法完整落入子节点 → 停留在当前节点
                }

                if (nodes[node].ElemCount < state.NodeCapacity || depth >= state.MaxDepth) break; // 叶子未满或已达最大深度

                Subdivide(node);
                // 细分可能扩容节点 buffer，循环顶部重取 span
            }

            int elem = AllocElement(entity, center, halfExtent);
            Attach(elem, node);
        }

        /// <summary>移除元素。不存在时返回 false。</summary>
        public bool Remove(Entity entity)
        {
            if (!TryLocate(entity, out int elem)) return false;
            int node = Elems[elem].Node;
            Unlink(elem, node);
            FreeElement(elem);
            Map[entity.Index] = 0;
            ref var state = ref State;
            state.Count--;
            TryCollapse(Nodes[node].Parent);
            return true;
        }

        /// <summary>
        /// 更新元素边界并标记本轮存活。新边界仍在原节点内时原地更新（O(1)，
        /// 最常见路径）；否则移除重插。实体不存在时等同 <see cref="Insert"/>。
        /// </summary>
        public void Update(Entity entity, float3 center, float3 halfExtent)
        {
            if (!TryLocate(entity, out int elem))
            {
                Insert(entity, center, halfExtent);
                return;
            }

            var elems = Elems;
            int node = elems[elem].Node;
            var nodes = Nodes;
            if (ContainedIn(center, halfExtent, nodes[node].Center, nodes[node].HalfExtent))
            {
                elems[elem].Center = center;
                elems[elem].HalfExtent = halfExtent;
                elems[elem].Stamp = State.TickStamp;
                return;
            }

            Remove(entity);
            Insert(entity, center, halfExtent); // Insert 内部已打 stamp
        }

        /// <summary>开启一轮标记清扫事务（每 tick 由维护系统调用一次，与 <see cref="EndTick"/> 配对）。</summary>
        public void BeginTick() => State.TickStamp++;

        /// <summary>
        /// 结束事务：移除本轮未被 <see cref="Update"/> 触及的实体。覆盖实体销毁、
        /// 包围组件被移除等一切消失路径，代价是移除延迟一帧。
        /// </summary>
        public void EndTick()
        {
            ref var state = ref State;
            int stamp = state.TickStamp;

            var stale = new NativeList<Entity>(16, Allocator.Temp);
            var elems = Elems;
            for (int i = 0; i < state.ElemHighWater; i++)
            {
                int s = elems[i].Stamp;
                if (s >= 0 && s != stamp) stale.Add(elems[i].Entity);
            }
            for (int i = 0; i < stale.Length; i++)
                Remove(stale[i]);
        }

        /// <summary>清空全部元素并保持树形收缩到根节点（buffer 容量保留，无分配）。</summary>
        public void Clear()
        {
            ref var state = ref State;
            m_World.ClearBuffer<TreeElement>(state.Elems);
            m_World.ClearBuffer<int>(state.EntityMap);
            m_World.ClearBuffer<TreeNodeElement>(state.Nodes);
            state.Count = 0;
            state.ElemHighWater = 0;
            state.FreeElemHead = Null;
            state.FreeBlockHead = Null;
            state.NodeHighWater = 0;
            AllocateBlock(out int root);
            InitNode(root, Null, state.RootCenter, state.RootHalfExtent);
        }

        /// <summary>AABB 范围查询（min/max 为闭区间），命中实体追加到调用方容器。</summary>
        public void QueryAABB(float3 min, float3 max, ref NativeList<Entity> results)
        {
            float3 queryCenter = (min + max) * 0.5f;
            float3 queryHalf = (max - min) * 0.5f;
            Query(min, max, queryCenter, queryHalf, false, 0f, ref results);
        }

        /// <summary>球体范围查询，命中实体追加到调用方容器。</summary>
        public void QuerySphere(float3 center, float radius, ref NativeList<Entity> results)
        {
            float3 half = new float3(radius);
            Query(center - half, center + half, center, half, true, radius * radius, ref results);
        }

        // ---- 内部：定位与初始化 ----

        internal void InitializeCore(in SpatialIndexConfig config)
        {
            ref var state = ref State;
            state.MaxDepth = config.MaxDepth;
            state.NodeCapacity = config.NodeCapacity;
            state.RootCenter = config.WorldCenter;
            switch (config.Dimension)
            {
                case SpatialDimension.QuadXY:
                    state.ChildCount = 4;
                    state.Axis1 = 1;
                    state.RootHalfExtent = new float3(config.WorldHalfExtent.x, config.WorldHalfExtent.x,
                        k_InactiveAxisHalfExtent);
                    break;
                case SpatialDimension.QuadXZ:
                    state.ChildCount = 4;
                    state.Axis1 = 2;
                    state.RootHalfExtent = new float3(config.WorldHalfExtent.x, k_InactiveAxisHalfExtent,
                        config.WorldHalfExtent.x);
                    break;
                default:
                    state.ChildCount = 8;
                    state.Axis1 = 1;
                    state.RootHalfExtent = config.WorldHalfExtent;
                    break;
            }

            state.FreeBlockHead = Null;
            state.NodeHighWater = 0;
            state.FreeElemHead = Null;
            state.ElemHighWater = 0;
            state.TickStamp = 0;
            state.Count = 0;

            AllocateBlock(out int root);
            InitNode(root, Null, state.RootCenter, state.RootHalfExtent);
            EnsureLength<int>(State.QueryStack, 64);
        }

        private bool TryLocate(Entity entity, out int elem)
        {
            var map = Map;
            int idx = entity.Index;
            if (idx >= map.Length)
            {
                elem = Null;
                return false;
            }
            int plus = map[idx];
            if (plus == 0)
            {
                elem = Null;
                return false;
            }
            elem = plus - 1;
            if (Elems[elem].Entity != entity)
            {
                elem = Null;
                return false;
            }
            return true;
        }

        private void EnsureLength<T>(BufferHandle handle, int length) where T : unmanaged
        {
            while (m_World.GetBufferLength<T>(handle) < length)
                m_World.AddBufferElement<T>(handle, default);
        }

        // ---- 内部：查询 ----

        private void Query(float3 min, float3 max, float3 queryCenter, float3 queryHalf, bool isSphere,
            float sphereRadiusSq, ref NativeList<Entity> results)
        {
            ref var state = ref State;
            if (state.Count == 0) return;

            int sp = 0;
            Stack[sp++] = 0;

            while (sp > 0)
            {
                var stack = Stack;
                int node = stack[--sp];

                var nodes = Nodes;
                var elems = Elems;
                for (int elem = nodes[node].ElemHead; elem != Null; elem = elems[elem].Next)
                {
                    if (!ElementIntersects(elems, elem, min, max, queryCenter, isSphere, sphereRadiusSq)) continue;
                    results.Add(elems[elem].Entity);
                }

                int first = nodes[node].FirstChild;
                if (first == Null) continue;

                for (int slot = 0; slot < state.ChildCount; slot++)
                {
                    int child = first + slot;
                    if (!AabbIntersects(nodes[child].Center, nodes[child].HalfExtent, queryCenter, queryHalf))
                        continue;
                    if (sp == stack.Length)
                    {
                        EnsureLength<int>(state.QueryStack, stack.Length * 2);
                        stack = Stack; // 扩容后重取
                    }
                    stack[sp++] = child;
                }
            }
        }

        private bool ElementIntersects(BufferSpan<TreeElement> elems, int elem, float3 min, float3 max,
            float3 queryCenter, bool isSphere, float sphereRadiusSq)
        {
            float3 c = elems[elem].Center;
            float3 e = elems[elem].HalfExtent;
            if (!isSphere) return AabbIntersects(c, e, queryCenter, (max - min) * 0.5f);
            float3 closest = math.clamp(queryCenter, c - e, c + e);
            return math.distancesq(closest, queryCenter) <= sphereRadiusSq;
        }

        private bool AabbIntersects(float3 c1, float3 e1, float3 c2, float3 e2)
            => math.all(math.abs(c1 - c2) <= e1 + e2);

        private bool ContainedIn(float3 c, float3 e, float3 nodeC, float3 nodeE)
            => math.all(c - e >= nodeC - nodeE) && math.all(c + e <= nodeC + nodeE);

        // ---- 内部：子槽位与边界（四叉/八叉统一）----

        private int ComputeChildSlot(BufferSpan<TreeNodeElement> nodes, int nodeIndex, float3 elemCenter,
            float3 elemHalfExtent)
        {
            float3 nodeC = nodes[nodeIndex].Center;
            float3 min = elemCenter - elemHalfExtent;
            float3 max = elemCenter + elemHalfExtent;
            ref var state = ref State;

            int slot = 0;
            if (elemCenter.x >= nodeC.x)
            {
                if (min.x < nodeC.x) return -1; // 跨中线，无法完整落入
                slot |= 1;
            }
            else if (max.x > nodeC.x) return -1;

            if (state.ChildCount == 8)
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
                int axis1 = state.Axis1;
                if (elemCenter[axis1] >= nodeC[axis1])
                {
                    if (min[axis1] < nodeC[axis1]) return -1;
                    slot |= 2;
                }
                else if (max[axis1] > nodeC[axis1]) return -1;
            }
            return slot;
        }

        private void GetChildBounds(BufferSpan<TreeNodeElement> nodes, int nodeIndex, int slot,
            out float3 center, out float3 halfExtent)
        {
            float3 nodeC = nodes[nodeIndex].Center;
            halfExtent = nodes[nodeIndex].HalfExtent;
            center = nodeC;
            ref var state = ref State;

            if (state.ChildCount == 8)
            {
                halfExtent *= 0.5f;
                center.x += (slot & 1) != 0 ? halfExtent.x : -halfExtent.x;
                center.y += (slot & 2) != 0 ? halfExtent.y : -halfExtent.y;
                center.z += (slot & 4) != 0 ? halfExtent.z : -halfExtent.z;
            }
            else
            {
                halfExtent.x *= 0.5f;
                halfExtent[state.Axis1] *= 0.5f; // 非激活轴保持超大范围
                center.x += (slot & 1) != 0 ? halfExtent.x : -halfExtent.x;
                center[state.Axis1] += (slot & 2) != 0 ? halfExtent[state.Axis1] : -halfExtent[state.Axis1];
            }
        }

        // ---- 内部：子划分与收缩 ----

        private void Subdivide(int node)
        {
            AllocateBlock(out int first);
            var nodes = Nodes; // AllocateBlock 可能扩容，此处重取
            nodes[node].FirstChild = first;
            ref var state = ref State;
            for (int slot = 0; slot < state.ChildCount; slot++)
            {
                GetChildBounds(nodes, node, slot, out float3 c, out float3 e);
                InitNode(first + slot, node, c, e);
            }

            // 下放能完整落入子节点的存量元素（Unlink/Link 各维护一次计数）
            nodes = Nodes;
            int elem = nodes[node].ElemHead;
            while (elem != Null)
            {
                var elems = Elems;
                int next = elems[elem].Next;
                int slot = ComputeChildSlot(nodes, node, elems[elem].Center, elems[elem].HalfExtent);
                if (slot >= 0)
                {
                    Unlink(elem, node);
                    Link(elem, first + slot);
                }
                elem = next;
            }
        }

        private void TryCollapse(int node)
        {
            ref var state = ref State;
            while (node != Null)
            {
                var nodes = Nodes;
                int first = nodes[node].FirstChild;
                if (first == Null) break;

                bool allEmptyLeaves = true;
                for (int slot = 0; slot < state.ChildCount; slot++)
                {
                    int child = first + slot;
                    if (nodes[child].FirstChild != Null || nodes[child].ElemCount != 0)
                    {
                        allEmptyLeaves = false;
                        break;
                    }
                }
                if (!allEmptyLeaves) break;

                FreeBlock(first);
                Nodes[node].FirstChild = Null;
                node = nodes[node].Parent;
            }
        }

        // ---- 内部：元素/节点的分配与链接 ----

        private int AllocElement(Entity entity, float3 center, float3 halfExtent)
        {
            ref var state = ref State;
            int elem;
            if (state.FreeElemHead != Null)
            {
                elem = state.FreeElemHead;
                state.FreeElemHead = Elems[elem].Next;
            }
            else
            {
                elem = state.ElemHighWater++;
                EnsureLength<TreeElement>(state.Elems, state.ElemHighWater);
            }

            var elems = Elems; // 扩容后重取
            elems[elem].Entity = entity;
            elems[elem].Center = center;
            elems[elem].HalfExtent = halfExtent;
            return elem;
        }

        private void FreeElement(int elem)
        {
            var elems = Elems;
            elems[elem].Next = State.FreeElemHead;
            elems[elem].Stamp = Null; // 空闲槽标记（清扫跳过）
            State.FreeElemHead = elem;
        }

        private void Attach(int elem, int node)
        {
            Link(elem, node);
            ref var state = ref State;
            var entity = Elems[elem].Entity;
            EnsureLength<int>(state.EntityMap, entity.Index + 1);
            Map[entity.Index] = elem + 1;
            state.Count++;
        }

        private void Link(int elem, int node)
        {
            var nodes = Nodes;
            var elems = Elems;
            int head = nodes[node].ElemHead;
            elems[elem].Next = head;
            elems[elem].Prev = Null;
            if (head != Null) elems[head].Prev = elem;
            nodes[node].ElemHead = elem;
            elems[elem].Node = node;
            nodes[node].ElemCount++;
        }

        private void Unlink(int elem, int node)
        {
            var nodes = Nodes;
            var elems = Elems;
            int prev = elems[elem].Prev;
            int next = elems[elem].Next;
            if (prev != Null) elems[prev].Next = next;
            else nodes[node].ElemHead = next;
            if (next != Null) elems[next].Prev = prev;
            nodes[node].ElemCount--;
        }

        private void AllocateBlock(out int blockStart)
        {
            ref var state = ref State;
            if (state.FreeBlockHead != Null)
            {
                blockStart = state.FreeBlockHead;
                state.FreeBlockHead = Nodes[blockStart].FirstChild;
                return;
            }

            blockStart = state.NodeHighWater;
            state.NodeHighWater += state.ChildCount;
            EnsureLength<TreeNodeElement>(state.Nodes, state.NodeHighWater);
        }

        private void FreeBlock(int blockStart)
        {
            Nodes[blockStart].FirstChild = State.FreeBlockHead;
            State.FreeBlockHead = blockStart;
        }

        private void InitNode(int node, int parent, float3 center, float3 halfExtent)
        {
            var nodes = Nodes;
            nodes[node].Parent = parent;
            nodes[node].FirstChild = Null;
            nodes[node].ElemHead = Null;
            nodes[node].ElemCount = 0;
            nodes[node].Center = center;
            nodes[node].HalfExtent = halfExtent;
        }
    }
}
