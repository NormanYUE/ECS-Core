using System.Collections.Generic;
using System.Security;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Core.Tests
{
    /// <summary>
    /// 表现层命令/同步系统的集成测试（不涉及 GameObject，直接消费命令队列验证）。
    /// 依赖 Unity 原生容器，纯 .NET CLI 下自动跳过，Unity Test Runner 中真正执行。
    /// </summary>
    [Parallelizable(ParallelScope.None)]
    [TestFixture]
    public class PresentationCommandSystemTests
    {
        private ECSManager m_Manager;
        private int m_Ticker;

        [SetUp]
        public void SetUp()
        {
            RequireNativeContainers();
            m_Manager = new ECSManager();
            m_Ticker = m_Manager.CreateTicker();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Manager == null) return;
            if (World.TryGetSingleton<PresentationCommands>(out var cmdOwner))
            {
                ref var cmds = ref World.GetComponent<PresentationCommands>(cmdOwner);
                if (cmds.IsInitialized) cmds.Dispose();
            }
            m_Manager.Dispose();
        }

        private static void RequireNativeContainers()
        {
            try
            {
                using var probe = new World();
                probe.CreateEntity();
            }
            catch (SecurityException)
            {
                Assert.Ignore("Requires Unity runtime support for Unity.Collections native containers.");
            }
        }

        private World World => m_Manager.World;

        private void SetupPipeline()
        {
            m_Manager.GetTicker(m_Ticker).Register<SpatialSystemGroup>();
            m_Manager.GetTicker(m_Ticker).Register<PresentationSystemGroup>();
            m_Manager.Start();

            // 正交视锥：x∈[-10,10]，y∈[-5,5]
            var cameraOwner = World.GetOrCreateSingleton<CameraFrustum>();
            World.SetComponent(cameraOwner, FrustumMath.FromViewProjection(new float4x4(
                new float4(0.1f, 0f, 0f, 0f),
                new float4(0f, 0.2f, 0f, 0f),
                new float4(0f, 0f, -0.02002f, 0f),
                new float4(0f, 0f, -1.002f, 1f))));
        }

        private Entity SpawnPresented(float3 position, int prefabId)
        {
            var entity = World.CreateEntity();
            World.AddComponent(entity, new LocalToWorld { Value = float4x4.Translate(position) });
            World.AddComponent(entity, new BoundingVolume(float3.zero, new float3(0.5f)));
            World.AddComponent(entity, new PresentationPrefab(prefabId));
            return entity;
        }

        private List<PresentationCommand> DrainCommands()
        {
            var list = new List<PresentationCommand>();
            if (!World.TryGetSingleton<PresentationCommands>(out var owner)) return list;
            ref var cmds = ref World.GetComponent<PresentationCommands>(owner);
            if (!cmds.IsInitialized) return list;
            while (cmds.Commands.TryDequeue(out var cmd)) list.Add(cmd);
            return list;
        }

        [Test]
        public void CommandSystem_EnteredView_EmitsSpawn()
        {
            SetupPipeline();
            var entity = SpawnPresented(float3.zero, 7);

            m_Manager.Tick(m_Ticker, 0.016f);

            var commands = DrainCommands();
            Assert.That(commands.Count, Is.EqualTo(1));
            Assert.That(commands[0].Kind, Is.EqualTo(PresentationCommandKind.Spawn));
            Assert.That(commands[0].Entity, Is.EqualTo(entity));
            Assert.That(commands[0].PrefabId, Is.EqualTo(7));
            Assert.That(math.distance(commands[0].Position, float3.zero), Is.LessThan(1e-4f));

            // 持续在视口内：下一帧不应重复 Spawn
            m_Manager.Tick(m_Ticker, 0.016f);
            Assert.That(DrainCommands(), Is.Empty);
        }

        [Test]
        public void CommandSystem_ExitedView_EmitsDespawn()
        {
            SetupPipeline();
            var entity = SpawnPresented(float3.zero, 3);
            m_Manager.Tick(m_Ticker, 0.016f);
            DrainCommands();

            // 移出视口
            World.SetComponent(entity, new LocalToWorld { Value = float4x4.Translate(new float3(100f, 0f, 0f)) });
            m_Manager.Tick(m_Ticker, 0.016f);

            var commands = DrainCommands();
            Assert.That(commands.Count, Is.EqualTo(1));
            Assert.That(commands[0].Kind, Is.EqualTo(PresentationCommandKind.Despawn));
            Assert.That(commands[0].Entity, Is.EqualTo(entity));
        }

        [Test]
        public void CommandSystem_DestroyedInView_EmitsDespawnViaSweep()
        {
            SetupPipeline();
            var entity = SpawnPresented(float3.zero, 5);
            m_Manager.Tick(m_Ticker, 0.016f);
            DrainCommands();

            // 视口内直接销毁：无边沿可发，靠清扫兜底
            World.DestroyEntity(entity);
            m_Manager.Tick(m_Ticker, 0.016f);

            var commands = DrainCommands();
            Assert.That(commands.Count, Is.EqualTo(1));
            Assert.That(commands[0].Kind, Is.EqualTo(PresentationCommandKind.Despawn));
            Assert.That(commands[0].Entity, Is.EqualTo(entity));
        }

        [Test]
        public void SyncSystem_WritesTrsIntoSlot()
        {
            SetupPipeline();
            var entity = SpawnPresented(new float3(5f, 1f, 0f), 9);
            m_Manager.Tick(m_Ticker, 0.016f); // Spawn 命令产出
            DrainCommands();

            // 模拟桥的行为：分配槽位 0 并挂载链接
            var cmdOwner = World.GetSingleton<PresentationCommands>();
            ref var cmds = ref World.GetComponent<PresentationCommands>(cmdOwner);
            cmds.EnsureSyncCapacity(0);
            World.AddComponent(entity, new PresentationLink(0));

            var target = new float3(8f, 2f, 0f);
            World.SetComponent(entity, new LocalToWorld { Value = float4x4.Translate(target) });
            m_Manager.Tick(m_Ticker, 0.016f);

            Assert.That(math.distance(cmds.SyncPositions[0], target), Is.LessThan(1e-4f));
            Assert.That(math.distance(cmds.SyncScales[0], new float3(1f)), Is.LessThan(1e-4f));
        }

        [Test]
        public void CommandSystem_NoCamera_NoCommands()
        {
            m_Manager.GetTicker(m_Ticker).Register<SpatialSystemGroup>();
            m_Manager.GetTicker(m_Ticker).Register<PresentationSystemGroup>();
            m_Manager.Start();
            // 不写 CameraFrustum：无相机，一切不可见

            SpawnPresented(float3.zero, 1);
            m_Manager.Tick(m_Ticker, 0.016f);

            Assert.That(DrainCommands(), Is.Empty);
        }
    }
}
