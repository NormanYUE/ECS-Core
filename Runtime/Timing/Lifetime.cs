namespace Ember.Core
{
    /// <summary>
    /// 剩余存活时间（秒）。由生命周期系统递减，归零后销毁实体。
    /// 适用于子弹、特效、临时标记等短生命周期实体。
    /// </summary>
    public struct Lifetime : IDataComponent
    {
        /// <summary>剩余秒数。</summary>
        public float Remaining;

        public Lifetime(float seconds) => Remaining = seconds;
    }
}
