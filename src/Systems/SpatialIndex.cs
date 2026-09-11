using System.Collections.Generic;

namespace Ember.Core
{
    /// <summary>
    /// 空间索引的进程内注册表（World → 维护系统）。框架没有系统互查 API，
    /// 这里提供与 <c>ECSManager.Active</c> 同款的静态访问入口，供游戏代码做
    /// 范围/最近邻查询：<c>SpatialIndex.GetTree(world).QuerySphere(...)</c>。
    /// 注册与注销由 <see cref="SpatialIndexSystem"/> 自动完成。
    /// </summary>
    public static class SpatialIndex
    {
        private static readonly Dictionary<World, SpatialIndexSystem> s_Systems =
            new Dictionary<World, SpatialIndexSystem>();

        internal static void Register(World world, SpatialIndexSystem system) => s_Systems[world] = system;

        internal static void Unregister(World world, SpatialIndexSystem system)
        {
            if (s_Systems.TryGetValue(world, out var existing) && existing == system)
                s_Systems.Remove(world);
        }

        /// <summary>尝试取回某 World 的空间树。未启用索引系统时返回 false。</summary>
        public static bool TryGetTree(World world, out SpatialTree tree)
        {
            if (world != null && s_Systems.TryGetValue(world, out var system))
            {
                tree = system.Tree;
                return tree != null;
            }
            tree = null;
            return false;
        }

        /// <summary>取回某 World 的空间树；未启用时返回 null。</summary>
        public static SpatialTree GetTree(World world)
            => TryGetTree(world, out var tree) ? tree : null;
    }
}
