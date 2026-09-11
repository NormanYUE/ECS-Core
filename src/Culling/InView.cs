namespace Ember.Core
{
    /// <summary>
    /// 视口内标记。由 <see cref="FrustumCullingSystem"/> 维护：实体进入相机视锥时添加、
    /// 离开时移除（仅在进出边沿发生结构变更，避免每帧全量 Archetype 迁移）。
    /// 渲染、UI、音效等系统可按需附带或排除本标记。
    /// </summary>
    public struct InView : ITagComponent
    {
    }
}
