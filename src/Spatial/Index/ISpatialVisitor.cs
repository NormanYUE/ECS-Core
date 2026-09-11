using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 空间划分树查询访问器。返回 false 立即终止查询。
    /// 以泛型 + ref 传入，struct 实现不会被装箱，热路径零分配。
    /// </summary>
    public interface ISpatialVisitor
    {
        bool Visit(Entity entity, float3 center, float3 halfExtent);
    }

    /// <summary>
    /// 把查询结果收集到调用方持有的 List 中（复用列表即零分配）。
    /// </summary>
    public struct EntityListVisitor : ISpatialVisitor
    {
        public List<Entity> Results;

        public EntityListVisitor(List<Entity> results) => Results = results;

        public bool Visit(Entity entity, float3 center, float3 halfExtent)
        {
            Results.Add(entity);
            return true;
        }
    }
}
