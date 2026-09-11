using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 视锥与包围体数学。全部为纯数学静态方法，零分配、Burst 兼容。
    /// </summary>
    public static class FrustumMath
    {
        /// <summary>
        /// 从 ViewProjection 矩阵提取视锥（Gribb-Hartmann 法，OpenGL 深度约定 z∈[-1,1]，
        /// 即 Unity <c>Camera.projectionMatrix * worldToCameraMatrix</c> 的产物）。
        /// 平面法线朝内且已归一化。
        /// </summary>
        public static CameraFrustum FromViewProjection(float4x4 vp)
        {
            float4 row0 = new float4(vp.c0.x, vp.c1.x, vp.c2.x, vp.c3.x);
            float4 row1 = new float4(vp.c0.y, vp.c1.y, vp.c2.y, vp.c3.y);
            float4 row2 = new float4(vp.c0.z, vp.c1.z, vp.c2.z, vp.c3.z);
            float4 row3 = new float4(vp.c0.w, vp.c1.w, vp.c2.w, vp.c3.w);

            var frustum = new CameraFrustum
            {
                Left = NormalizePlane(row3 + row0),
                Right = NormalizePlane(row3 - row0),
                Bottom = NormalizePlane(row3 + row1),
                Top = NormalizePlane(row3 - row1),
                Near = NormalizePlane(row3 + row2),
                Far = NormalizePlane(row3 - row2),
            };
            return frustum;
        }

        /// <summary>球体与视锥相交测试（含完全在内与部分相交）。</summary>
        public static bool IntersectsSphere(in CameraFrustum frustum, float3 center, float radius)
        {
            for (int i = 0; i < CameraFrustum.PlaneCount; i++)
            {
                float4 plane = frustum.GetPlane(i);
                if (math.dot(plane.xyz, center) + plane.w < -radius)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// AABB 与视锥相交测试（p-vertex 法：把半范围投影到平面法线得到等效半径，
        /// 保守精确，不会误剔可见盒体，极端角度可能略多保留）。
        /// </summary>
        public static bool IntersectsAABB(in CameraFrustum frustum, float3 center, float3 extents)
        {
            for (int i = 0; i < CameraFrustum.PlaneCount; i++)
            {
                float4 plane = frustum.GetPlane(i);
                float radius = math.dot(extents, math.abs(plane.xyz));
                if (math.dot(plane.xyz, center) + plane.w < -radius)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 把本地 AABB 经任意仿射矩阵换算为世界 AABB（abs 轴投影法，
        /// 结果是旋转后盒体的紧致包围盒，不会漏包）。
        /// </summary>
        public static void TransformAABB(in float4x4 matrix, float3 center, float3 extents,
            out float3 worldCenter, out float3 worldExtents)
        {
            worldCenter = math.transform(matrix, center);
            worldExtents = math.abs(matrix.c0.xyz) * extents.x
                         + math.abs(matrix.c1.xyz) * extents.y
                         + math.abs(matrix.c2.xyz) * extents.z;
        }

        private static float4 NormalizePlane(float4 plane)
        {
            float invLength = math.rsqrt(math.dot(plane.xyz, plane.xyz));
            return plane * invLength;
        }
    }
}
