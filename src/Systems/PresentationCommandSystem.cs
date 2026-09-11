using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 表现层命令系统（串行）。基于 <see cref="VisibilityState"/> 的边沿位
    /// （Entered/ExitedView）为带 <see cref="PresentationPrefab"/> 的实体生产
    /// Spawn/Despawn 命令；并用盖戳清扫兜底"视口内被销毁"的实体（回收延迟一帧）。
    ///
    /// 串行而非 Job 的原因：框架 chunk job 的 <c>ChunkJobMeta</c> 无 Entity 访问器，
    /// 命令必须携带实体 Id；边沿检测为轻量字节比较，串行成本可忽略。
    /// 每帧高频的 TRS 同步由 <see cref="PresentationSyncSystem"/>（Job·Burst）承担。
    /// </summary>
    public sealed class PresentationCommandSystem : SystemBase
    {
        private readonly EntityQuery m_Query = EntityQuery.With<PresentationPrefab, VisibilityState, LocalToWorld>();
        protected override void DeclareAccess(AccessBuilder access) => access.Read<PresentationPrefab>().Read<VisibilityState>().Read<LocalToWorld>().Write<PresentationCommands>().StructuralChanges();
        
        protected override void OnTick(SystemContext ctx)
        {
            var world = ctx.World;
            var owner = world.GetOrCreateSingleton<PresentationCommands>();
            ref var cmds = ref world.GetComponent<PresentationCommands>(owner);
            if (!cmds.IsInitialized) cmds.Initialize(Allocator.Persistent);

            cmds.TickStamp++;
            int stamp = cmds.TickStamp;

            foreach (var chunk in ctx.QueryChunks(m_Query))
            {
                var prefabs = chunk.Read<PresentationPrefab>();
                var states = chunk.Read<VisibilityState>();
                var l2w = chunk.Read<LocalToWorld>();

                for (int row = 0; row < chunk.Count; row++)
                {
                    var entity = chunk.EntityAt(row);
                    bool tracked = cmds.Tracked.ContainsKey(entity);
                    if (tracked) cmds.Tracked[entity] = stamp;

                    var state = states[row];
                    if (state.EnteredView && !tracked)
                    {
                        TransformDecompose.Decompose(l2w[row].Value, out float3 pos, out quaternion rot, out float3 scale);
                        cmds.Commands.Enqueue(new PresentationCommand
                        {
                            Kind = PresentationCommandKind.Spawn,
                            Entity = entity,
                            PrefabId = prefabs[row].PrefabId,
                            Position = pos,
                            Rotation = rot,
                            Scale = scale,
                        });
                        cmds.Tracked.Add(entity, stamp);
                    }
                    else if (state.ExitedView && tracked)
                    {
                        cmds.Commands.Enqueue(new PresentationCommand
                        {
                            Kind = PresentationCommandKind.Despawn,
                            Entity = entity,
                        });
                        cmds.Tracked.Remove(entity);
                    }
                }
            }
            
            var stale = new NativeList<Entity>(16, Allocator.Temp);
            foreach (var kv in cmds.Tracked)
            {
                if (kv.Value != stamp) stale.Add(kv.Key);
            }
            for (int i = 0; i < stale.Length; i++)
            {
                cmds.Commands.Enqueue(new PresentationCommand
                {
                    Kind = PresentationCommandKind.Despawn,
                    Entity = stale[i],
                });
                cmds.Tracked.Remove(stale[i]);
            }
        }
    }
}
