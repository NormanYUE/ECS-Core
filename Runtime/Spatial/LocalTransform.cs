using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 本地空间变换（相对父实体）。对应 Unity Transform 的
    /// localPosition / localRotation / localScale（等比缩放）。
    /// 无父实体时即世界变换。
    /// </summary>
    public struct LocalTransform : IDataComponent
    {
        /// <summary>本地位置。</summary>
        public float3 Position;

        /// <summary>本地旋转。</summary>
        public quaternion Rotation;

        /// <summary>等比缩放（XYZ 同值）。</summary>
        public float Scale;

        public LocalTransform(float3 position, quaternion rotation, float scale = 1f)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        /// <summary>转换为 4x4 变换矩阵（T * R * S）。</summary>
        public float4x4 ToMatrix() => float4x4.TRS(Position, Rotation, new float3(Scale));

        /// <summary>把本地空间点变换到父空间（先缩放、再旋转、后平移）。</summary>
        public float3 TransformPoint(float3 point) => Position + math.mul(Rotation, point * Scale);

        /// <summary>把父空间点逆变换回本地空间。</summary>
        public float3 InverseTransformPoint(float3 point)
            => math.mul(math.inverse(Rotation), point - Position) / Scale;

        /// <summary>把本地方向向量变换到父空间（仅旋转，不含缩放和平移）。</summary>
        public float3 TransformDirection(float3 direction) => math.mul(Rotation, direction);
    }
}
