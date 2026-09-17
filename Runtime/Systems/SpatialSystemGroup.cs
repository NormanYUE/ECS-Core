namespace Ember.Core
{
    /// <summary>
    /// 空间与视锥剔除系统组。把完整管线封装为一次注册，业务侧只需：
    /// <code>manager.GetTicker(updateIdx).Register&lt;SpatialSystemGroup&gt;();</code>
    /// 组内注册顺序即管线顺序（依赖图再按读写冲突自动分层）：
    /// ① 补齐组件 → ② 世界包围盒（Job·Burst）→ ③ 视锥剔除（Job·Burst）→
    /// ④ 可见性标签应用 → ⑤ 空间索引维护。
    /// </summary>
    public sealed class SpatialSystemGroup : SystemGroup
    {
        // 框架将基类无参 Configure 标记 Obsolete 以强制显式重写（未来大版本改为 abstract）；
        // 重写 Obsolete 成员触发 CS0672，此处属框架过渡期的预期用法，抑制之。
#pragma warning disable CS0672
        public override void Configure(SystemTicker ticker)
#pragma warning restore CS0672
        {
            ticker.Register<SpatialSetupSystem>();
            ticker.Register<WorldBoundsSystem>();
            ticker.Register<FrustumCullingSystem>();
            ticker.Register<VisibilityApplySystem>();
            ticker.Register<SpatialIndexSystem>();
        }
    }
}
