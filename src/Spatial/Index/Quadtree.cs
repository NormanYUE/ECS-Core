using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>四叉树工作平面。</summary>
    public enum QuadtreePlane : byte
    {
        /// <summary>XY 平面（Unity 2D 惯例，忽略 Z）。</summary>
        XY = 0,

        /// <summary>XZ 平面（3D 俯视/地面场景，忽略 Y）。</summary>
        XZ = 1,
    }

    /// <summary>
    /// 二维四叉树（基于 <see cref="SpatialTree"/> 引擎）。非激活轴使用超大半范围，
    /// 因此越界元素仍能被查询到。
    /// </summary>
    public sealed class Quadtree : SpatialTree
    {
        private readonly QuadtreePlane m_Plane;

        /// <param name="plane">工作平面。</param>
        /// <param name="center">根节点中心（仅激活轴有效）。</param>
        /// <param name="halfExtent">根节点激活轴半范围（如 500 表示覆盖 1000x1000 区域）。</param>
        /// <param name="maxDepth">最大深度。</param>
        /// <param name="nodeCapacity">节点元素容量（超过才细分）。</param>
        public Quadtree(QuadtreePlane plane, float3 center, float halfExtent, int maxDepth = 8, int nodeCapacity = 8)
            : base(center, BuildHalfExtent(plane, halfExtent), maxDepth, nodeCapacity)
        {
            m_Plane = plane;
        }

        protected override int ChildCount => 4;

        private static float3 BuildHalfExtent(QuadtreePlane plane, float halfExtent)
        {
            float3 h = new float3(halfExtent);
            if (plane == QuadtreePlane.XY) h.z = InactiveAxisHalfExtent;
            else h.y = InactiveAxisHalfExtent;
            return h;
        }

        /// <summary>第二激活轴（XY 平面为 Y=1，XZ 平面为 Z=2）。</summary>
        private int SecondAxis => m_Plane == QuadtreePlane.XY ? 1 : 2;

        protected override int ComputeChildSlot(int nodeIndex, float3 elemCenter, float3 elemHalfExtent)
        {
            int axis1 = SecondAxis;
            float3 nodeC = NodeCenterAt(nodeIndex);
            float3 min = elemCenter - elemHalfExtent;
            float3 max = elemCenter + elemHalfExtent;

            int slot = 0;
            if (elemCenter.x >= nodeC.x)
            {
                if (min.x < nodeC.x) return -1; // 跨中线，无法完整落入
                slot |= 1;
            }
            else if (max.x > nodeC.x) return -1;

            if (elemCenter[axis1] >= nodeC[axis1])
            {
                if (min[axis1] < nodeC[axis1]) return -1;
                slot |= 2;
            }
            else if (max[axis1] > nodeC[axis1]) return -1;

            return slot;
        }

        protected override void GetChildBounds(int nodeIndex, int slot, out float3 center, out float3 halfExtent)
        {
            int axis1 = SecondAxis;
            float3 nodeC = NodeCenterAt(nodeIndex);
            halfExtent = NodeHalfExtentAt(nodeIndex);
            halfExtent.x *= 0.5f;
            halfExtent[axis1] *= 0.5f; // 非激活轴保持不变（超大范围）

            center = nodeC;
            center.x += (slot & 1) != 0 ? halfExtent.x : -halfExtent.x;
            center[axis1] += (slot & 2) != 0 ? halfExtent[axis1] : -halfExtent[axis1];
        }
    }
}
