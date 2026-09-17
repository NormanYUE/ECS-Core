using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 角速度。轴角向量表示：方向为旋转轴，模长为角速度（弧度/秒）。
    /// </summary>
    public struct AngularVelocity : IDataComponent
    {
        /// <summary>轴角向量形式的角速度。</summary>
        public float3 Value;

        public AngularVelocity(float3 value) => Value = value;
    }
}
