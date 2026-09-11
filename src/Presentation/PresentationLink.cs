namespace Ember.Core
{
    /// <summary>
    /// 表现层链接：实体 ↔ GameObject 同步槽位。由表现层桥在 Spawn 时分配并挂载，
    /// Despawn 时移除；业务代码不应手动增删。
    /// 槽位即 <see cref="PresentationCommands"/> 同步数组与 TransformAccessArray 的下标，
    /// 回收时以 swap-back 复用。
    /// </summary>
    public struct PresentationLink : IDataComponent
    {
        /// <summary>同步槽位下标。</summary>
        public int Slot;

        public PresentationLink(int slot)
        {
            Slot = slot;
        }
    }
}
