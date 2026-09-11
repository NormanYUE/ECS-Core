using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Core.Tests
{
    [TestFixture]
    public class TransformMathTests
    {
        [Test]
        public void LocalTransform_Identity_HasOriginIdentityRotationUnitScale()
        {
            var t = LocalTransform.Identity;

            Assert.That(t.Position.Equals(float3.zero), Is.True);
            Assert.That(t.Rotation.Equals(quaternion.identity), Is.True);
            Assert.That(t.Scale, Is.EqualTo(1f));
        }

        [Test]
        public void LocalTransform_ToMatrix_MatchesTrs()
        {
            var t = new LocalTransform(new float3(1f, 2f, 3f), quaternion.RotateZ(math.radians(30f)), 2f);

            var expected = float4x4.TRS(t.Position, t.Rotation, new float3(t.Scale));

            Assert.That(t.ToMatrix().Equals(expected), Is.True);
        }

        [Test]
        public void LocalTransform_TransformPoint_Identity_ReturnsSamePoint()
        {
            var point = new float3(1f, 2f, 3f);

            Assert.That(LocalTransform.Identity.TransformPoint(point).Equals(point), Is.True);
        }

        [Test]
        public void LocalTransform_TransformPoint_AppliesScaleThenRotationThenTranslation()
        {
            var t = new LocalTransform(new float3(10f, 0f, 0f), quaternion.identity, 2f);

            var result = t.TransformPoint(new float3(1f, 1f, 1f));

            Assert.That(result.Equals(new float3(12f, 2f, 2f)), Is.True);
        }

        [Test]
        public void LocalTransform_InverseTransformPoint_Roundtrips()
        {
            var t = new LocalTransform(new float3(3f, -2f, 5f), quaternion.RotateY(math.radians(45f)), 1.5f);
            var point = new float3(7f, 1f, -4f);

            var roundtripped = t.InverseTransformPoint(t.TransformPoint(point));

            Assert.That(math.distance(roundtripped, point), Is.LessThan(1e-4f));
        }

        [Test]
        public void LocalTransform_TransformDirection_AppliesRotationOnly()
        {
            var t = new LocalTransform(new float3(100f, 0f, 0f), quaternion.RotateZ(math.radians(90f)), 5f);

            var result = t.TransformDirection(new float3(1f, 0f, 0f));

            Assert.That(math.distance(result, new float3(0f, 1f, 0f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void LocalToWorld_Identity_IsIdentityMatrix()
        {
            Assert.That(LocalToWorld.Identity.Value.Equals(float4x4.identity), Is.True);
        }

        [Test]
        public void LocalToWorld_Compose_CombinesParentAndLocal()
        {
            var parent = new LocalToWorld { Value = float4x4.Translate(new float3(10f, 0f, 0f)) };
            var local = LocalTransform.FromPosition(new float3(1f, 2f, 3f));

            var composed = LocalToWorld.Compose(local, parent);

            Assert.That(math.distance(composed.Position, new float3(11f, 2f, 3f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void LocalToWorld_Axes_ReadMatrixColumns()
        {
            var l2w = new LocalToWorld { Value = float4x4.TRS(new float3(5f, 6f, 7f), quaternion.identity, new float3(1f)) };

            Assert.That(l2w.Position.Equals(new float3(5f, 6f, 7f)), Is.True);
            Assert.That(l2w.Right.Equals(new float3(1f, 0f, 0f)), Is.True);
            Assert.That(l2w.Up.Equals(new float3(0f, 1f, 0f)), Is.True);
            Assert.That(l2w.Forward.Equals(new float3(0f, 0f, 1f)), Is.True);
        }
    }
}
