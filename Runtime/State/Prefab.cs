namespace Ember.Core
{
    /// <summary>
    /// 预制体模板实体标记。模板实体不参与常规逻辑
    /// （常规查询可附带 None&lt;Prefab&gt; 排除），仅作为实例化的来源。
    /// </summary>
    public struct Prefab : ITagComponent
    {
    }
}
