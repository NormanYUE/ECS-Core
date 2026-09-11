using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Core.Tests
{
    [TestFixture]
    public class TransformDecomposeTests
    {
        [Test]
        public void Decompose_Identity_GivesOriginIdentityUnitScale()
        {
            TransformDecompose.Decompose(float4x4.identity, out float3 pos, out quaternion rot,
                out float3 scale);

            Assert.That(pos.Equals(float3.zero), Is.True);
            Assert.That(scale.Equals(new float3(1f)), Is.True);
            var v = math.mul(rot, new float3(1f, 2f, 3f));
            Assert.That(math.distance(v, new float3(1f, 2f, 3f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void Decompose_TranslationAndUniformScale_RoundTrips()
        {
            var m = float4x4.TRS(new float3(10f, -5f, 3f), quaternion.identity, new float3(2f));
            TransformDecompose.Decompose(m, out float3 pos, out quaternion rot, out float3 scale);

            Assert.That(math.distance(pos, new float3(10f, -5f, 3f)), Is.LessThan(1e-4f));
            Assert.That(math.distance(scale, new float3(2f)), Is.LessThan(1e-4f));
            var v = math.mul(rot, new float3(0f, 1f, 0f));
            Assert.That(math.distance(v, new float3(0f, 1f, 0f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void Decompose_RotationAndNonUniformScale_RoundTrips()
        {
            var rotation = quaternion.RotateY(math.radians(90f));
            var m = float4x4.TRS(new float3(1f, 2f, 3f), rotation, new float3(1f, 2f, 3f));
            TransformDecompose.Decompose(m, out float3 pos, out quaternion rot, out float3 scale);

            Assert.That(math.distance(scale, new float3(1f, 2f, 3f)), Is.LessThan(1e-4f));

            // 逐点比对：分解重组的矩阵与原矩阵对点的变换一致（四元数符号无关紧要）
            var recomposed = float4x4.TRS(pos, rot, scale);
            var point = new float3(0.5f, -1f, 2f);
            var a = math.transform(m, point);
            var b = math.transform(recomposed, point);
            Assert.That(math.distance(a, b), Is.LessThan(1e-3f));
        }

        [Test]
        public void Decompose_AxisAlignedRotations_RecoverBasis()
        {
            // 绕 Z 轴 90 度：+X 应转到 +Y
            var m = float4x4.TRS(float3.zero, quaternion.RotateZ(math.PI / 2f), new float3(1f));
            TransformDecompose.Decompose(m, out _, out quaternion rot, out _);

            var x = math.mul(rot, new float3(1f, 0f, 0f));
            Assert.That(math.distance(x, new float3(0f, 1f, 0f)), Is.LessThan(1e-4f));
        }
    }
}
