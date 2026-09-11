using System;
using System.Security;
using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Core.Tests
{
    /// <summary>
    /// 依赖 World 实体分配的集成测试。Unity.Collections 原生容器只能在
    /// Unity 运行时中分配，纯 .NET CLI 下自动 Ignore（与框架测试约定一致，
    /// 但在 Unity Test Runner 中会真正执行断言）。
    /// </summary>
    [Parallelizable(ParallelScope.None)]
    [TestFixture]
    public class WorldIntegrationTests
    {
        private World m_World;

        [SetUp]
        public void SetUp()
        {
            RequireNativeContainers();
            m_World = new World();
        }

        [TearDown]
        public void TearDown() => m_World?.Dispose();

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

        [Test]
        public void AddAndGet_LocalTransform_Roundtrips()
        {
            var entity = m_World.CreateEntity();
            m_World.AddComponent(entity, new LocalTransform(new float3(1f, 2f, 3f), quaternion.identity, 2f));

            Assert.That(m_World.HasComponent<LocalTransform>(entity), Is.True);

            ref var transform = ref m_World.GetComponent<LocalTransform>(entity);
            transform.Position += new float3(1f, 0f, 0f);

            Assert.That(m_World.GetComponent<LocalTransform>(entity).Position.x, Is.EqualTo(4f));
            Assert.That(m_World.GetComponent<LocalTransform>(entity).Scale, Is.EqualTo(2f));
        }

        [Test]
        public void Disabled_Tag_AddHasRemove()
        {
            var entity = m_World.CreateEntity();

            m_World.AddComponent(entity, new Disabled());
            Assert.That(m_World.HasComponent<Disabled>(entity), Is.True);

            m_World.RemoveComponent<Disabled>(entity);
            Assert.That(m_World.HasComponent<Disabled>(entity), Is.False);
        }

        [Test]
        public void WorldTime_Singleton_SetAndReadBack()
        {
            var owner = m_World.GetOrCreateSingleton<WorldTime>();
            m_World.SetComponent(owner, new WorldTime
            {
                TimeScale = 1f,
                DeltaTime = 0.016f,
                UnscaledDeltaTime = 0.016f,
                ElapsedTime = 12.5f,
                FrameCount = 750,
            });

            Assert.That(m_World.TryGetSingleton<WorldTime>(out var entity), Is.True);
            var time = m_World.GetComponent<WorldTime>(entity);
            Assert.That(time.ElapsedTime, Is.EqualTo(12.5f));
            Assert.That(time.FrameCount, Is.EqualTo(750));
        }

        [Test]
        public void Lifetime_And_Age_Roundtrip()
        {
            var entity = m_World.CreateEntity();
            m_World.AddComponent(entity, new Lifetime(3f));
            m_World.AddComponent(entity, new Age());

            ref var lifetime = ref m_World.GetComponent<Lifetime>(entity);
            lifetime.Remaining -= 1f;

            Assert.That(m_World.GetComponent<Lifetime>(entity).Remaining, Is.EqualTo(2f));
            Assert.That(m_World.GetComponent<Age>(entity).Elapsed, Is.EqualTo(0f));
        }

        [Test]
        public void GlobalRandom_Singleton_RefAccessAdvancesState()
        {
            var owner = m_World.GetOrCreateSingleton<GlobalRandom>();
            m_World.SetComponent(owner, new GlobalRandom(7));

            ref var random = ref m_World.GetComponent<GlobalRandom>(owner);
            var first = random.Value.NextUInt();
            var second = random.Value.NextUInt();

            Assert.That(second, Is.Not.EqualTo(first));
        }
    }
}
