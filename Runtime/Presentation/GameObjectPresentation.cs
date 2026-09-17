using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Jobs;

namespace Ember.Core
{
    /// <summary>
    /// GameObject 表现层桥（托管，主线程）。每帧在 <c>manager.Tick(...)</c> 之后调用
    /// <see cref="Sync"/>：drain <see cref="PresentationCommands"/> 命令队列
    ///（Spawn→池取出并激活、分配同步槽位；Despawn→回收、swap-back 槽位），
    /// 然后调度 <see cref="TransformWriteJob"/>（Burst）批量回写可见 GO 的 Transform。
    ///
    /// 池经构造注入（默认 <see cref="GameObjectPool"/>）；业务侧也可完全自写消费者
    /// 直接 drain 单例命令队列。退出时调用 <see cref="Dispose"/>：
    /// 回收全部活动 GO、释放 TransformAccessArray 与命令单例的原生容器。
    /// </summary>
    public sealed class GameObjectPresentation : IDisposable
    {
        /// <summary>槽位记录（swap-back 密集复用，下标即槽位）。</summary>
        private struct SlotEntry
        {
            public Entity Entity;
            public int PrefabId;
            public GameObject Go;
        }

        private readonly World m_World;
        private readonly IGameObjectPool m_Pool;
        private readonly List<SlotEntry> m_Slots = new List<SlotEntry>(64);
        private TransformAccessArray m_Transforms;
        private bool m_TransformsCreated;

        /// <param name="world">目标 World。</param>
        /// <param name="pool">对象池；null 使用 Core 默认 <see cref="GameObjectPool"/>。</param>
        public GameObjectPresentation(World world, IGameObjectPool pool = null)
        {
            m_World = world ?? throw new ArgumentNullException(nameof(world));
            m_Pool = pool ?? new GameObjectPool();
        }

        /// <summary>当前池（默认池的预制体注册经此访问：<c>((GameObjectPool)p.Pool).RegisterPrefab(...)</c>）。</summary>
        public IGameObjectPool Pool => m_Pool;

        /// <summary>活动 GameObject 数（= 已分配同步槽位数）。</summary>
        public int ActiveCount => m_Slots.Count;

        /// <summary>
        /// 每帧调用一次（在 manager.Tick 之后）：drain 命令 + 批量回写 Transform。
        /// 命令单例未初始化（表现层系统组未注册/未运行）时为空操作。
        /// </summary>
        public void Sync()
        {
            if (!m_World.TryGetSingleton<PresentationCommands>(out var owner)) return;
            ref var cmds = ref m_World.GetComponent<PresentationCommands>(owner);
            if (!cmds.IsInitialized) return;

            DrainCommands(ref cmds);
            ApplyTransforms(ref cmds);
        }

        /// <summary>
        /// 回收全部活动 GO，释放 TransformAccessArray 与命令单例原生容器。
        /// 业务侧在销毁 ECSManager 之前调用。
        /// </summary>
        public void Dispose()
        {
            for (int i = 0; i < m_Slots.Count; i++)
                m_Pool.Recycle(m_Slots[i].PrefabId, m_Slots[i].Go);
            m_Slots.Clear();

            if (m_TransformsCreated)
            {
                m_Transforms.Dispose();
                m_TransformsCreated = false;
            }

            if (m_World.TryGetSingleton<PresentationCommands>(out var owner))
            {
                ref var cmds = ref m_World.GetComponent<PresentationCommands>(owner);
                if (cmds.IsInitialized) cmds.Dispose();
            }
        }

        private void DrainCommands(ref PresentationCommands cmds)
        {
            while (cmds.Commands.TryDequeue(out var cmd))
            {
                if (cmd.Kind == PresentationCommandKind.Spawn) HandleSpawn(cmd, ref cmds);
                else HandleDespawn(cmd, ref cmds);
            }
        }

        private void HandleSpawn(in PresentationCommand cmd, ref PresentationCommands cmds)
        {
            if (m_World.HasComponent<PresentationLink>(cmd.Entity)) return; // 已生成（边沿重复保护）

            int slot = m_Slots.Count;
            var go = m_Pool.Checkout(cmd.PrefabId, cmd.Position, cmd.Rotation, cmd.Scale);

            if (!m_TransformsCreated)
            {
                m_Transforms = new TransformAccessArray(64);
                m_TransformsCreated = true;
            }
            m_Transforms.Add(go.transform);

            m_Slots.Add(new SlotEntry { Entity = cmd.Entity, PrefabId = cmd.PrefabId, Go = go });
            cmds.EnsureSyncCapacity(slot);
            cmds.SyncPositions[slot] = cmd.Position;
            cmds.SyncRotations[slot] = cmd.Rotation;
            cmds.SyncScales[slot] = cmd.Scale;
            m_World.AddComponent(cmd.Entity, new PresentationLink(slot));
        }

        private void HandleDespawn(in PresentationCommand cmd, ref PresentationCommands cmds)
        {
            if (!m_World.HasComponent<PresentationLink>(cmd.Entity)) return; // 实体已销毁或未生成

            int slot = m_World.GetComponent<PresentationLink>(cmd.Entity).Slot;
            var entry = m_Slots[slot];
            m_Pool.Recycle(entry.PrefabId, entry.Go);

            int last = m_Slots.Count - 1;
            if (slot != last)
            {
                // swap-back：末槽记录搬到被删槽，并同步修正其实体的槽位
                var moved = m_Slots[last];
                m_Slots[slot] = moved;
                cmds.SyncPositions[slot] = cmds.SyncPositions[last];
                cmds.SyncRotations[slot] = cmds.SyncRotations[last];
                cmds.SyncScales[slot] = cmds.SyncScales[last];
                m_World.SetComponent(moved.Entity, new PresentationLink(slot));
            }
            m_Slots.RemoveAt(last);
            m_Transforms.RemoveAtSwapBack(slot);
            m_World.RemoveComponent<PresentationLink>(cmd.Entity);
        }

        private void ApplyTransforms(ref PresentationCommands cmds)
        {
            if (m_Slots.Count == 0 || !m_TransformsCreated) return;

            var job = new TransformWriteJob
            {
                Positions = cmds.SyncPositions.AsArray(),
                Rotations = cmds.SyncRotations.AsArray(),
                Scales = cmds.SyncScales.AsArray(),
            };
            job.Schedule(m_Transforms).Complete();
        }
    }
}
