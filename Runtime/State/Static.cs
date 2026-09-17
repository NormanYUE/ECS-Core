namespace Ember.Core
{
    /// <summary>
    /// 静态实体标记。约定：移动/变换类系统查询附带 None&lt;Static&gt;，
    /// 以跳过永不移动的实体（场景、建筑等），节省积分与矩阵重算开销。
    /// </summary>
    public struct Static : ITagComponent
    {
    }
}
