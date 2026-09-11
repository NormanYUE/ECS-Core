using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 表现层命令与同步通道（单例组件，纯非托管）。系统侧生产，桥/业务侧消费：
    /// <list type="bullet">
    /// <item><see cref="Commands"/>：Spawn/Despawn 命令队列（<see cref="PresentationCommandSystem"/> 生产）。</item>
    /// <item><see cref="Tracked"/>：已 Spawn 实体 → 存活戳（销毁清扫簿记，内部使用）。</item>
    /// <item>SyncPositions/Rotations/Scales：按 <see cref="PresentationLink.Slot"/> 下标的
    /// TRS 同步数组，由 <see cref="PresentationSyncJob"/>（Burst）写入、
    /// TransformWriteJob 批量应用到 GameObject。</item>
    /// </list>
    /// 业务侧退出前对组件 ref 调用 <see cref="Dispose"/> 释放原生容器
    /// （或经 <see cref="GameObjectPresentation.Dispose"/> 统一释放）。
    /// </summary>
    public struct PresentationCommands : ISingletonComponent, IDisposable
    {
        /// <summary>Spawn/Despawn 命令队列。</summary>
        public NativeQueue<PresentationCommand> Commands;

        /// <summary>已 Spawn 实体 → 最后存活戳（系统内部簿记）。</summary>
        public NativeParallelHashMap<Entity, int> Tracked;

        /// <summary>当前 tick 戳（系统内部簿记）。</summary>
        public int TickStamp;

        /// <summary>同步位置数组（槽位下标）。</summary>
        public NativeList<float3> SyncPositions;

        /// <summary>同步旋转数组（槽位下标）。</summary>
        public NativeList<quaternion> SyncRotations;

        /// <summary>同步缩放数组（槽位下标）。</summary>
        public NativeList<float3> SyncScales;

        /// <summary>是否已初始化（单例默认值为未初始化）。</summary>
        public readonly bool IsInitialized => Commands.IsCreated;

        /// <summary>
        /// 分配全部原生容器。仅可对未初始化实例调用一次。
        /// </summary>
        public void Initialize(Allocator allocator)
        {
            if (IsInitialized) throw new InvalidOperationException("PresentationCommands: already initialized");
            Commands = new NativeQueue<PresentationCommand>(allocator);
            Tracked = new NativeParallelHashMap<Entity, int>(64, allocator);
            TickStamp = 0;
            SyncPositions = new NativeList<float3>(64, allocator);
            SyncRotations = new NativeList<quaternion>(64, allocator);
            SyncScales = new NativeList<float3>(64, allocator);
        }

        /// <summary>
        /// 释放全部原生容器。
        /// </summary>
        public void Dispose()
        {
            if (!IsInitialized) return;
            Commands.Dispose();
            Tracked.Dispose();
            SyncPositions.Dispose();
            SyncRotations.Dispose();
            SyncScales.Dispose();
        }

        /// <summary>
        /// 保证同步数组长度覆盖 slot（桥在分配槽位时调用）。
        /// </summary>
        public void EnsureSyncCapacity(int slot)
        {
            while (SyncPositions.Length <= slot)
            {
                SyncPositions.Add(float3.zero);
                SyncRotations.Add(quaternion.identity);
                SyncScales.Add(new float3(1f));
            }
        }
    }
}
