using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 表现层命令（原生缓冲元素）。由 <see cref="PresentationCommandJob"/>（并行·Burst）
    /// 与 <see cref="PresentationSweepSystem"/>（销毁清扫）生产，由表现层桥
    /// （<see cref="GameObjectPresentation"/>）或业务自写消费者 drain。
    /// </summary>
    public struct PresentationCommand
    {
        /// <summary>命令种类。</summary>
        public PresentationCommandKind Kind;

        /// <summary>目标实体。</summary>
        public Entity Entity;

        /// <summary>预制体 Id（仅 Spawn 有效）。</summary>
        public int PrefabId;

        /// <summary>初始位置（仅 Spawn 有效，生成即就位）。</summary>
        public float3 Position;

        /// <summary>初始旋转（仅 Spawn 有效）。</summary>
        public quaternion Rotation;

        /// <summary>初始缩放（仅 Spawn 有效）。</summary>
        public float3 Scale;
    }
}
