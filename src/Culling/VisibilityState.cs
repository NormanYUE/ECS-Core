namespace Ember.Core
{
    /// <summary>
    /// 可见性状态（Job 可写的字节标志）。<see cref="FrustumCullingSystem"/>（Job·Burst）
    /// 每帧写入；<see cref="VisibilityApplySystem"/> 据此在边沿增删 <see cref="InView"/> 标签。
    /// bit1 记录上一帧状态，供任何系统从纯组件数据推导进入/离开边沿
    /// （如表现层的 Spawn/Despawn），无需结构变更或历史缓存。
    /// 缺失本组件的实体会被 <see cref="SpatialSetupSystem"/> 自动补齐。
    /// </summary>
    public struct VisibilityState : IDataComponent
    {
        /// <summary>位标志。bit0 = 当前帧视口内；bit1 = 上一帧视口内。</summary>
        public byte Flags;

        /// <summary>当前帧视口内位。</summary>
        public const byte InViewBit = 1;

        /// <summary>上一帧视口内位。</summary>
        public const byte WasInViewBit = 2;

        /// <summary>实体当前是否在视口内。</summary>
        public bool InView
        {
            get => (Flags & InViewBit) != 0;
            set => Flags = value ? (byte)(Flags | InViewBit) : (byte)(Flags & ~InViewBit);
        }

        /// <summary>实体上一帧是否在视口内（由剔除 Job 在写入前自动移位）。</summary>
        public bool WasInView
        {
            get => (Flags & WasInViewBit) != 0;
            set => Flags = value ? (byte)(Flags | WasInViewBit) : (byte)(Flags & ~WasInViewBit);
        }

        /// <summary>本帧刚进入视口（当前在内、上一帧不在）。</summary>
        public bool EnteredView => InView && !WasInView;

        /// <summary>本帧刚离开视口（当前不在、上一帧在）。</summary>
        public bool ExitedView => !InView && WasInView;
    }
}
