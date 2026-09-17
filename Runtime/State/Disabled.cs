namespace Ember.Core
{
    /// <summary>
    /// 禁用标记。约定：系统查询附带 None&lt;Disabled&gt; 以跳过被禁用的实体。
    /// 相比销毁实体，可无损地临时摘除实体行为。
    /// </summary>
    public struct Disabled : ITagComponent
    {
    }
}
