using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 线速度（米/秒）。由移动系统积分到 LocalTransform.Position。
    /// </summary>
    public struct LinearVelocity : IDataComponent
    {
        /// <summary>速度向量。</summary>
        public float3 Value;

        public LinearVelocity(float3 value) => Value = value;
    }
}
