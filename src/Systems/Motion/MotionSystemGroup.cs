namespace Ember.Core
{
    /// <summary>
    /// 运动系统组：把移动积分封装为一次注册。
    /// <code>manager.GetTicker(fixedIdx).Register&lt;MotionSystemGroup&gt;();</code>
    /// </summary>
    public sealed class MotionSystemGroup : SystemGroup
    {
#pragma warning disable CS0672
        public override void Configure(SystemTicker ticker)
#pragma warning restore CS0672
        {
            ticker.Register<MovementSystem>();
        }
    }
}
