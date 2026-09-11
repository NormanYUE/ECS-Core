using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Ember.Core
{
    /// <summary>
    /// Core 默认 GameObject 池：预制体注册表 + 按 PrefabId 分桶的失活栈。
    /// Checkout 命中栈则复用，未命中时 Instantiate（仅池冷发生）；Recycle 失活入栈。
    /// 可用 <see cref="Prewarm"/> 预热以消除运行时实例化尖峰。
    /// </summary>
    public sealed class GameObjectPool : IGameObjectPool
    {
        private readonly Dictionary<int, GameObject> m_Prefabs = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Stack<GameObject>> m_Inactive = new Dictionary<int, Stack<GameObject>>();

        /// <summary>注册预制体（Id → GameObject 模板）。</summary>
        public void RegisterPrefab(int prefabId, GameObject prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            m_Prefabs[prefabId] = prefab;
        }

        /// <summary>预热：预先实例化 count 个实例入池，消除首遇尖峰。</summary>
        public void Prewarm(int prefabId, int count)
        {
            if (!m_Prefabs.TryGetValue(prefabId, out var prefab))
                throw new InvalidOperationException($"GameObjectPool: prefab {prefabId} not registered");
            var stack = GetOrCreateStack(prefabId);
            for (int i = 0; i < count; i++)
            {
                var go = UnityEngine.Object.Instantiate(prefab);
                go.SetActive(false);
                stack.Push(go);
            }
        }

        public GameObject Checkout(int prefabId, float3 position, quaternion rotation, float3 scale)
        {
            GameObject go;
            if (m_Inactive.TryGetValue(prefabId, out var stack) && stack.Count > 0)
            {
                go = stack.Pop();
                go.SetActive(true);
            }
            else
            {
                if (!m_Prefabs.TryGetValue(prefabId, out var prefab))
                    throw new InvalidOperationException($"GameObjectPool: prefab {prefabId} not registered");
                go = UnityEngine.Object.Instantiate(prefab);
            }

            var t = go.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = scale;
            return go;
        }

        public void Recycle(int prefabId, GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            GetOrCreateStack(prefabId).Push(go);
        }

        private Stack<GameObject> GetOrCreateStack(int prefabId)
        {
            if (!m_Inactive.TryGetValue(prefabId, out var stack))
            {
                stack = new Stack<GameObject>();
                m_Inactive[prefabId] = stack;
            }
            return stack;
        }
    }
}
