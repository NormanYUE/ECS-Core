using System.Security;
using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Core.Tests
{
    /// <summary>
    /// 运动积分系统集成测试。依赖 Unity 原生容器，
    /// 纯 .NET CLI 下自动跳过，Unity Test Runner 中真正执行。
    /// </summary>
    [Parallelizable(ParallelScope.None)]
    [TestFixture]
    public class MovementSystemTests
    {
        private ECSManager m_Manager;
        private int m_Ticker;

        [SetUp]
        public void SetUp()
        {
            RequireNativeContainers();
            m_Manager = new ECSManager();
            m_Ticker = m_Manager.CreateTicker();
            m_Manager.GetTicker(m_Ticker).Register<MotionSystemGroup>();
            m_Manager.Start();
        }

        [TearDown]
        public void TearDown() => m_Manager?.Dispose();

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

        private void SetTime(float deltaTime, float timeScale = 1f)
        {
            var owner = World.GetOrCreateSingleton<WorldTime>();
            World.SetComponent(owner, new WorldTime
            {
                TimeScale = timeScale,
                DeltaTime = deltaTime,
                UnscaledDeltaTime = deltaTime,
            });
        }

        private Entity Spawn(float3 position, float3 linear, float3 angular)
        {
            var entity = World.CreateEntity();
            World.AddComponent(entity, new LocalTransform(position, quaternion.identity, 1f));
            World.AddComponent(entity, new LinearVelocity(linear));
            World.AddComponent(entity, new AngularVelocity(angular));
            return entity;
        }

        [Test]
        public void LinearVelocity_IntegratesIntoPosition()
        {
            SetTime(0.5f);
            var entity = Spawn(float3.zero, new float3(2f, 0f, -4f), float3.zero);
            var other = Spawn(new float3(1f, 1f, 1f), new float3(0f, 1f, 0f), float3.zero);

            m_Manager.Tick(m_Ticker, 0.5f);

            var moved = World.GetComponent<LocalTransform>(entity);
            Assert.That(moved.Position.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(moved.Position.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(moved.Position.z, Is.EqualTo(-2f).Within(1e-4f));

            // 同一次 Tick 内每个实体各自按自己的速度积分
            var otherMoved = World.GetComponent<LocalTransform>(other);
            Assert.That(otherMoved.Position.y, Is.EqualTo(1.5f).Within(1e-4f));
        }

        [Test]
        public void LinearVelocity_AccumulatesAcrossTicks()
        {
            SetTime(0.25f);
            var entity = Spawn(float3.zero, new float3(1f, 0f, 0f), float3.zero);

            m_Manager.Tick(m_Ticker, 0.25f);
            m_Manager.Tick(m_Ticker, 0.25f);
            m_Manager.Tick(m_Ticker, 0.25f);

            Assert.That(World.GetComponent<LocalTransform>(entity).Position.x, Is.EqualTo(0.75f).Within(1e-4f));
        }

        [Test]
        public void Static_And_Disabled_AreExcluded()
        {
            SetTime(1f);
            var moving = Spawn(float3.zero, new float3(1f, 0f, 0f), float3.zero);
            var stat = Spawn(float3.zero, new float3(1f, 0f, 0f), float3.zero);
            var disabled = Spawn(float3.zero, new float3(1f, 0f, 0f), float3.zero);

            World.AddComponent(stat, new Static());
            World.AddComponent(disabled, new Disabled());

            m_Manager.Tick(m_Ticker, 1f);

            Assert.That(World.GetComponent<LocalTransform>(moving).Position.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(World.GetComponent<LocalTransform>(stat).Position.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(World.GetComponent<LocalTransform>(disabled).Position.x, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void AngularVelocity_RotatesAboutAxis()
        {
            SetTime(1f);
            // 绕 Y 轴 π rad/s 积分 1 秒 = 180°，与旋转正负号约定无关：
            // 单位 +Z 必然映射到 -Z。
            var entity = Spawn(float3.zero, float3.zero, new float3(0f, math.PI, 0f));

            m_Manager.Tick(m_Ticker, 1f);

            var rotated = math.mul(World.GetComponent<LocalTransform>(entity).Rotation, new float3(0f, 0f, 1f));
            Assert.That(rotated.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(rotated.y, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(rotated.z, Is.EqualTo(-1f).Within(1e-3f));
        }

        [Test]
        public void AngularVelocity_QuarterTurn_NormalizesRotation()
        {
            SetTime(0.5f);
            // 绕 Y 轴 π/2 rad/s 积分 0.5 秒 = 90°：+Z 转到 ±X，且四元数保持单位长度。
            var entity = Spawn(float3.zero, float3.zero, new float3(0f, math.PI * 0.5f, 0f));

            m_Manager.Tick(m_Ticker, 0.5f);

            var rotation = World.GetComponent<LocalTransform>(entity).Rotation;
            Assert.That(math.length(rotation.value), Is.EqualTo(1f).Within(1e-4f));

            var rotated = math.mul(rotation, new float3(0f, 0f, 1f));
            Assert.That(math.abs(rotated.x), Is.EqualTo(1f).Within(1e-3f));
            Assert.That(rotated.y, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(math.abs(rotated.z), Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void ZeroVelocity_LeavesTransformUntouched()
        {
            SetTime(1f);
            var entity = Spawn(new float3(3f, 4f, 5f), float3.zero, float3.zero);

            m_Manager.Tick(m_Ticker, 1f);

            var transform = World.GetComponent<LocalTransform>(entity);
            Assert.That(transform.Position.x, Is.EqualTo(3f));
            Assert.That(transform.Position.y, Is.EqualTo(4f));
            Assert.That(transform.Position.z, Is.EqualTo(5f));
        }

        [Test]
        public void WithoutWorldTime_FallsBackToTickDelta()
        {
            // 不创建 WorldTime 单例：退回本次 Tick 的步长，运动不静默失效。
            var entity = Spawn(float3.zero, new float3(4f, 0f, 0f), float3.zero);

            m_Manager.Tick(m_Ticker, 0.5f);

            Assert.That(World.GetComponent<LocalTransform>(entity).Position.x, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void ZeroDeltaTime_SkipsIntegration()
        {
            SetTime(0f);
            var entity = Spawn(float3.zero, new float3(4f, 0f, 0f), float3.zero);

            Assert.DoesNotThrow(() => m_Manager.Tick(m_Ticker, 0f));
            Assert.That(World.GetComponent<LocalTransform>(entity).Position.x, Is.EqualTo(0f));
        }

        [Test]
        public void ChunksOfBothQueries_AreIntegratedInOneTick()
        {
            // 线速度与角速度分属两条查询，两者都必须在本帧被积分。
            SetTime(1f);
            var linearOnly = World.CreateEntity();
            World.AddComponent(linearOnly, new LocalTransform(float3.zero, quaternion.identity, 1f));
            World.AddComponent(linearOnly, new LinearVelocity(new float3(1f, 0f, 0f)));

            var angularOnly = World.CreateEntity();
            World.AddComponent(angularOnly, new LocalTransform(float3.zero, quaternion.identity, 1f));
            World.AddComponent(angularOnly, new AngularVelocity(new float3(0f, math.PI, 0f)));

            m_Manager.Tick(m_Ticker, 1f);

            Assert.That(World.GetComponent<LocalTransform>(linearOnly).Position.x, Is.EqualTo(1f).Within(1e-4f));
            var rotated = math.mul(World.GetComponent<LocalTransform>(angularOnly).Rotation, new float3(0f, 0f, 1f));
            Assert.That(rotated.z, Is.EqualTo(-1f).Within(1e-3f));
        }

        [Test]
        public void RepeatedTicks_RemainStable()
        {
            // 稳态不变量：连续 Tick 不抛异常且位移线性累加（Infos 仅在 Chunk 数增长时扩容）。
            SetTime(0.016f);
            var entity = Spawn(float3.zero, new float3(1f, 0f, 0f), float3.zero);

            for (int i = 0; i < 16; i++) m_Manager.Tick(m_Ticker, 0.016f);

            Assert.That(World.GetComponent<LocalTransform>(entity).Position.x, Is.EqualTo(0.256f).Within(1e-4f));
        }
    }
}
