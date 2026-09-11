using Unity.Mathematics;
using UnityEngine;

namespace Ember.Core
{
    /// <summary>
    /// GameObject 池接口。业务侧实现此接口即可替换 Core 默认池
    /// （如接入 Addressables 异步加载、自定义激活策略），
    /// 经 <see cref="GameObjectPresentation"/> 构造注入。
    /// </summary>
    public interface IGameObjectPool
    {
        /// <summary>取出一个 GameObject 并就位（激活+设置 TRS）。池冷时可自行实例化。</summary>
        GameObject Checkout(int prefabId, float3 position, quaternion rotation, float3 scale);

        /// <summary>回收一个 GameObject（失活+归还池中）。</summary>
        void Recycle(int prefabId, GameObject go);
    }
}
