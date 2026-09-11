using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 三维八叉树（基于 <see cref="SpatialTree"/> 引擎）。
    /// </summary>
    public sealed class Octree : SpatialTree
    {
        /// <param name="center">根节点中心。</param>
        /// <param name="halfExtent">根节点各轴半范围。</param>
        /// <param name="maxDepth">最大深度。</param>
        /// <param name="nodeCapacity">节点元素容量（超过才细分）。</param>
        public Octree(float3 center, float3 halfExtent, int maxDepth = 8, int nodeCapacity = 8)
            : base(center, halfExtent, maxDepth, nodeCapacity)
        {
        }

        protected override int ChildCount => 8;

        protected override int ComputeChildSlot(int nodeIndex, float3 elemCenter, float3 elemHalfExtent)
        {
            float3 nodeC = NodeCenterAt(nodeIndex);
            float3 min = elemCenter - elemHalfExtent;
            float3 max = elemCenter + elemHalfExtent;

            int slot = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                if (elemCenter[axis] >= nodeC[axis])
                {
                    if (min[axis] < nodeC[axis]) return -1; // 跨中线，无法完整落入
                    slot |= 1 << axis;
                }
                else if (max[axis] > nodeC[axis]) return -1;
            }
            return slot;
        }

        protected override void GetChildBounds(int nodeIndex, int slot, out float3 center, out float3 halfExtent)
        {
            float3 nodeC = NodeCenterAt(nodeIndex);
            halfExtent = NodeHalfExtentAt(nodeIndex) * 0.5f;

            center = nodeC;
            center.x += (slot & 1) != 0 ? halfExtent.x : -halfExtent.x;
            center.y += (slot & 2) != 0 ? halfExtent.y : -halfExtent.y;
            center.z += (slot & 4) != 0 ? halfExtent.z : -halfExtent.z;
        }
    }
}
