namespace Ember.Core
{
    /// <summary>
    /// 表现层命令种类。
    /// </summary>
    public enum PresentationCommandKind : byte
    {
        /// <summary>生成 GameObject（进入视口/被销毁清扫触发）。</summary>
        Spawn = 0,

        /// <summary>回收 GameObject（离开视口/实体消失触发）。</summary>
        Despawn = 1,
    }
}
