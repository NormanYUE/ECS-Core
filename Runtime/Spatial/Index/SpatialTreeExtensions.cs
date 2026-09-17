namespace Ember.Core
{
    /// <summary>
    /// 空间树访问扩展（扩展类，静态豁免）。空间树是 <see cref="SpatialTree"/> 单例组件
    /// （标量）+ World 托管 buffer（数据），本类提供取树/建树的便捷入口。
    /// 树无需 Dispose：全部存储随 World.Dispose 自动释放。
    /// </summary>
    public static class SpatialTreeExtensions
    {
        /// <summary>
        /// 取空间树操作视图。未启用（系统组未注册/尚未运行一帧）时抛异常——
        /// 先用 <see cref="TryGetSpatialTree"/> 判断。
        /// </summary>
        public static SpatialTreeView GetSpatialTree(this World world)
        {
            return !world.TryGetSpatialTree(out var view) ? throw new System.InvalidOperationException("空间索引未启用：请向 ticker 注册 SpatialSystemGroup（或 SpatialIndexSystem）并运行至少一帧") : view;
        }

        /// <summary>尝试取空间树操作视图。未初始化时返回 false。</summary>
        private static bool TryGetSpatialTree(this World world, out SpatialTreeView view)
        {
            view = default;
            if (!world.TryGetSingleton<SpatialTree>(out var owner)) return false;
            if (!world.GetComponent<SpatialTree>(owner).IsInitialized) return false;
            view = new SpatialTreeView(world, owner);
            return true;
        }

        /// <summary>
        /// 确保空间树存在并按配置初始化（系统首 tick 调用；业务预创建同用）。
        /// buffer 由 World 托管，重复调用为空操作。
        /// </summary>
        public static SpatialTreeView EnsureSpatialTree(this World world, in SpatialIndexConfig config)
        {
            var owner = world.GetOrCreateSingleton<SpatialTree>();
            ref var state = ref world.GetComponent<SpatialTree>(owner);
            if (!state.IsInitialized)
            {
                int childCount = config.Dimension == SpatialDimension.Octree ? 8 : 4;
                state.Nodes = world.CreateBuffer<TreeNodeElement>(childCount * 4);
                state.Elems = world.CreateBuffer<TreeElement>(64);
                state.EntityMap = world.CreateBuffer<int>(64);
                state.QueryStack = world.CreateBuffer<int>(64);
                new SpatialTreeView(world, owner).InitializeCore(config);
            }
            return new SpatialTreeView(world, owner);
        }
    }
}
