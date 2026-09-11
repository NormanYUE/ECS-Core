namespace Ember.Core
{
    /// <summary>
    /// 实体已存活时间（秒）。由计时系统累加，常用于
    /// 渐强/衰减插值、成长阶段判定等。
    /// </summary>
    public struct Age : IDataComponent
    {
        /// <summary>已存活秒数。</summary>
        public float Elapsed;
    }
}
