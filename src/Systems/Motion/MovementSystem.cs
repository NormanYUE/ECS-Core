using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;

namespace Ember.Core
{
    /// <summary>
    /// 移动积分系统（Job·Burst）：把 <see cref="LinearVelocity"/> /
    /// <see cref="AngularVelocity"/> 积分到 <see cref="LocalTransform"/>，
    /// 时间来源 <see cref="WorldTime.DeltaTime"/>（遵守 TimeScale）。
    ///
    /// 为什么是 <see cref="SystemBase"/> 而不是 <c>JobSystem&lt;TJob&gt;</c>：
    /// 需要 <c>None&lt;Static&gt;</c> / <c>None&lt;Disabled&gt;</c> 排除，而
    /// <c>JobSystem&lt;TJob&gt;</c> 的 QueryMask 只表达 With 语义；故用自定义
    /// EntityQuery 编译 + 自调度并行 Job（模式同碰撞管线），并在 OnTick 内
    /// Complete —— 满足框架「不允许跨 tick 挂起 Job」的硬约束。
    ///
    /// 线速度与角速度分两条查询：Chunk 无法公开判定列归属（Layout 为 internal），
    /// 分开查询后每块的列指针必然存在。
    /// 行级不变量：纯 Chunk 列读写，稳态零分配（Infos 仅在 Chunk 数增长时扩容）。
    /// </summary>
    public sealed class MovementSystem : SystemBase
    {
        private readonly EntityQuery m_LinearQuery = new(
            new ComponentMask().With<LocalTransform>().With<LinearVelocity>(),
            ComponentMask.Empty,
            new ComponentMask().With<Static>().With<Disabled>());

        private readonly EntityQuery m_AngularQuery = new(
            new ComponentMask().With<LocalTransform>().With<AngularVelocity>(),
            ComponentMask.Empty,
            new ComponentMask().With<Static>().With<Disabled>());

        /// <summary>Chunk 信息列表（跨帧复用，增长才分配）。</summary>
        private NativeList<MovementChunkInfo> m_Infos;

        protected override void DeclareAccess(AccessBuilder access) => access
            .Read<WorldTime>()
            .Read<LinearVelocity>()
            .Read<AngularVelocity>()
            .Write<LocalTransform>()
            .Read<Static>()
            .Read<Disabled>();

        public override void OnDestroy()
        {
            if (m_Infos.IsCreated) m_Infos.Dispose();
            base.OnDestroy();
        }

        protected override void OnTick(SystemContext ctx)
        {
            World world = ctx.World;
            // 时间来源优先取 WorldTime 单例（遵守 TimeScale）；单例由使用方维护，
            // 未创建时退回本次 Tick 的步长，避免运动静默失效。
            float deltaTime = world.TryGetSingleton<WorldTime>(out Entity timeOwner)
                ? world.GetComponent<WorldTime>(timeOwner).DeltaTime
                : ctx.DeltaTime;
            if (deltaTime <= 0f) return;

            int linearChunks = FillInfos(world.CompileQuery(m_LinearQuery).GetChunks(), linear: true);
            int angularChunks = FillInfos(world.CompileQuery(m_AngularQuery).GetChunks(), linear: false);

            int total = linearChunks + angularChunks;
            if (total <= 0) return;

            var job = new MovementJob
            {
                Infos = m_Infos.AsArray(),
                DeltaTime = deltaTime,
            };
            job.Schedule(total, 4, default).Complete();
        }

        /// <summary>把一批 Chunk 的列指针收进 Infos；返回本批块数。</summary>
        private unsafe int FillInfos(ReadOnlyChunkList chunks, bool linear)
        {
            int chunkCount = chunks.Count;
            if (chunkCount <= 0) return 0;

            if (!m_Infos.IsCreated)
                m_Infos = new NativeList<MovementChunkInfo>(chunkCount, Allocator.Persistent);
            else if (m_Infos.Capacity < m_Infos.Length + chunkCount)
                m_Infos.Capacity = math.max(m_Infos.Capacity * 2, m_Infos.Length + chunkCount);

            int start = m_Infos.Length;
            for (int i = 0; i < chunkCount; i++)
            {
                Chunk chunk = chunks[i];
                var info = new MovementChunkInfo
                {
                    TransformPtr = (long)chunk.GetColumn<LocalTransform>().UnsafePtr,
                    Count = chunk.Count,
                };
                if (linear)
                    info.LinearPtr = (long)chunk.GetColumn<LinearVelocity>().UnsafePtr;
                else
                    info.AngularPtr = (long)chunk.GetColumn<AngularVelocity>().UnsafePtr;
                m_Infos.Add(info);
            }
            return m_Infos.Length - start;
        }
    }
}
