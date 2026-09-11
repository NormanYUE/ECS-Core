using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Core.Tests
{
    [TestFixture]
    public class FrustumMathTests
    {
        /// <summary>OpenGL 风格正交投影（z∈[-1,1]）：x∈[-10,10]、y∈[-5,5]、深度 z∈[-100,-0.1]。</summary>
        private static float4x4 OrthographicVP(float l, float r, float b, float t, float n, float f)
        {
            float sx = 2f / (r - l);
            float sy = 2f / (t - b);
            float sz = -2f / (f - n);
            return new float4x4(
                new float4(sx, 0f, 0f, 0f),
                new float4(0f, sy, 0f, 0f),
                new float4(0f, 0f, sz, 0f),
                new float4(-(r + l) / (r - l), -(t + b) / (t - b), -(f + n) / (f - n), 1f));
        }

        /// <summary>OpenGL 风格透视投影：近 n、远 f、近平面半宽 r、半高 t。</summary>
        private static float4x4 PerspectiveVP(float n, float f, float r, float t)
        {
            return new float4x4(
                new float4(n / r, 0f, 0f, 0f),
                new float4(0f, n / t, 0f, 0f),
                new float4(0f, 0f, -(f + n) / (f - n), -1f),
                new float4(0f, 0f, -2f * f * n / (f - n), 0f));
        }

        [Test]
        public void FromViewProjection_Ortho_ExtractsAxisPlanes()
        {
            var frustum = FrustumMath.FromViewProjection(OrthographicVP(-10f, 10f, -5f, 5f, 0.1f, 100f));

            Assert.That(math.distance(frustum.Left.xyz, new float3(1f, 0f, 0f)), Is.LessThan(1e-3f));
            Assert.That(frustum.Left.w, Is.EqualTo(10f).Within(1e-2f));
            Assert.That(math.distance(frustum.Right.xyz, new float3(-1f, 0f, 0f)), Is.LessThan(1e-3f));
            Assert.That(frustum.Right.w, Is.EqualTo(10f).Within(1e-2f));
            Assert.That(math.distance(frustum.Bottom.xyz, new float3(0f, 1f, 0f)), Is.LessThan(1e-3f));
            Assert.That(frustum.Bottom.w, Is.EqualTo(5f).Within(1e-2f));
            Assert.That(math.distance(frustum.Top.xyz, new float3(0f, -1f, 0f)), Is.LessThan(1e-3f));
            Assert.That(frustum.Top.w, Is.EqualTo(5f).Within(1e-2f));
        }

        [Test]
        public void IntersectsSphere_Ortho_ClassifiesCorrectly()
        {
            var frustum = FrustumMath.FromViewProjection(OrthographicVP(-10f, 10f, -5f, 5f, 0.1f, 100f));

            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(0f, 0f, -10f), 1f), Is.True);
            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(10.5f, 0f, -10f), 1f), Is.True); // 部分相交
            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(12f, 0f, -10f), 1f), Is.False); // 右侧外
            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(0f, 0f, 5f), 1f), Is.False); // 相机后方
            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(0f, 0f, -200f), 1f), Is.False); // 远平面外
            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(0f, -5.5f, -10f), 1f), Is.True); // 下边沿
        }

        [Test]
        public void IntersectsAABB_Ortho_ClassifiesCorrectly()
        {
            var frustum = FrustumMath.FromViewProjection(OrthographicVP(-10f, 10f, -5f, 5f, 0.1f, 100f));

            Assert.That(FrustumMath.IntersectsAABB(frustum, float3.zero, new float3(1f)), Is.True);
            Assert.That(FrustumMath.IntersectsAABB(frustum, new float3(11f, 0f, -5f), new float3(2f)), Is.True); // 搭边
            Assert.That(FrustumMath.IntersectsAABB(frustum, new float3(20f, 0f, -5f), new float3(2f)), Is.False);
            Assert.That(FrustumMath.IntersectsAABB(frustum, new float3(0f, 0f, 10f), new float3(1f)), Is.False); // 近平面前
        }

        [Test]
        public void IntersectsSphere_Perspective_RespectsNearFarAndSides()
        {
            var frustum = FrustumMath.FromViewProjection(PerspectiveVP(0.5f, 50f, 1f, 1f));

            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(0f, 0f, -5f), 1f), Is.True);
            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(0f, 0f, 2f), 0.5f), Is.False); // 相机后方
            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(50f, 0f, -10f), 0.5f), Is.False); // 右侧外
            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(0f, 0f, -100f), 1f), Is.False); // 远超远平面
            Assert.That(FrustumMath.IntersectsSphere(frustum, new float3(0f, 0f, -0.35f), 0.1f), Is.False); // 近平面前
        }

        [Test]
        public void TransformAABB_Identity_KeepsBox()
        {
            FrustumMath.TransformAABB(float4x4.identity, new float3(1f, 2f, 3f), new float3(0.5f),
                out float3 c, out float3 e);

            Assert.That(c.Equals(new float3(1f, 2f, 3f)), Is.True);
            Assert.That(e.Equals(new float3(0.5f)), Is.True);
        }

        [Test]
        public void TransformAABB_Rotation45_ExpandsDiagonally()
        {
            var matrix = float4x4.RotateY(math.radians(45f));
            FrustumMath.TransformAABB(matrix, float3.zero, new float3(1f, 0f, 0f), out _, out float3 e);

            float half = math.SQRT2 * 0.5f;
            Assert.That(e.x, Is.EqualTo(half).Within(1e-4f));
            Assert.That(e.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(e.z, Is.EqualTo(half).Within(1e-4f));
        }

        [Test]
        public void TransformAABB_ScaleAndTranslate_AppliesBoth()
        {
            var matrix = float4x4.TRS(new float3(10f, 0f, 0f), quaternion.identity, new float3(2f));
            FrustumMath.TransformAABB(matrix, new float3(1f, 0f, 0f), new float3(0.5f, 1f, 2f),
                out float3 c, out float3 e);

            Assert.That(c.Equals(new float3(12f, 0f, 0f)), Is.True);
            Assert.That(e.Equals(new float3(1f, 2f, 4f)), Is.True);
        }
    }
}
