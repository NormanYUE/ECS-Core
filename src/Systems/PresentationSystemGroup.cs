namespace Ember.Core
{
    /// <summary>
    /// 表现层系统组。依赖剔除管线产物（<see cref="VisibilityState"/> 边沿位），
    /// 须在 <see cref="SpatialSystemGroup"/> 之后注册：
    /// <code>
    /// manager.GetTicker(updateIdx).Register&lt;SpatialSystemGroup&gt;();
    /// manager.GetTicker(updateIdx).Register&lt;PresentationSystemGroup&gt;();
    /// </code>
    /// 组内顺序即管线顺序：① 命令生产（串行，边沿+清扫）→ ② TRS 同步（Job·Burst）。
    /// GameObject 的生成/回收/应用变换不在组内——由业务侧的
    /// <see cref="GameObjectPresentation"/> 桥在每帧 Tick 后驱动。
    /// </summary>
    public sealed class PresentationSystemGroup : SystemGroup
    {
        // 框架将基类无参 Configure 标记 Obsolete 以强制显式重写（未来大版本改为 abstract）；
        // 重写 Obsolete 成员触发 CS0672，此处属框架过渡期的预期用法，抑制之。
#pragma warning disable CS0672
        public override void Configure(SystemTicker ticker)
#pragma warning restore CS0672
        {
            ticker.Register<PresentationCommandSystem>();
            ticker.Register<PresentationSyncSystem>();
        }
    }
}
