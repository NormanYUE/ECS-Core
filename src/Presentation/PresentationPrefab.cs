namespace Ember.Core
{
    /// <summary>
    /// 表现层预制体标识。实体挂载后，进入视口时由表现层桥生成对应 GameObject。
    /// GameObject 引用是托管对象、不能进入组件，故以 int Id 间接寻址，
    /// 映射关系由业务侧在池（<see cref="IGameObjectPool"/>）中注册。
    /// </summary>
    public struct PresentationPrefab : IDataComponent
    {
        /// <summary>预制体 Id（业务侧注册表键）。</summary>
        public int PrefabId;

        public PresentationPrefab(int prefabId)
        {
            PrefabId = prefabId;
        }
    }
}
