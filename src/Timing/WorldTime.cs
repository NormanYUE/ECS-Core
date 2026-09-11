namespace Ember.Core
{
    /// <summary>
    /// 全局时间状态（单例）。由时间系统每帧写入，
    /// 供 Job 系统或无法直接访问 SystemContext.DeltaTime 的查询读取。
    /// </summary>
    public struct WorldTime : ISingletonComponent
    {
        /// <summary>时间缩放倍率（1 为正常速度，0 为暂停）。</summary>
        public float TimeScale;

        /// <summary>本帧缩放后的时间步长（秒）。</summary>
        public float DeltaTime;

        /// <summary>本帧未缩放的时间步长（秒）。</summary>
        public float UnscaledDeltaTime;

        /// <summary>世界启动以来累计的缩放后时间（秒）。</summary>
        public float ElapsedTime;

        /// <summary>已 tick 的总帧数。</summary>
        public long FrameCount;
    }
}
