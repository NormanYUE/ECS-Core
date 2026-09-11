using System.Collections.Generic;
using System.Security;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Core.Tests
{
    /// <summary>
    /// 空间索引与视锥剔除系统的集成测试。依赖 Unity 原生容器，
    /// 纯 .NET CLI 下自动跳过，Unity Test Runner 中真正执行。
    /// </summary>
    [Parallelizable(ParallelScope.None)]
    [TestFixture]
    public class SpatialSystemsTests
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
            if (World.TryGetSingleton<SpatialTree>(out var treeOwner))
            {
                ref var tree = ref World.GetComponent<SpatialTree>(treeOwner);
                if (tree.IsInitialized) tree.Dispose(); // 原生容器随测试拆卸释放
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

        private Entity SpawnIndexed(float3 position, float3 extents)
        {
            var entity = World.CreateEntity();
            World.AddComponent(entity, new LocalToWorld { Value = float4x4.Translate(position) });
            World.AddComponent(entity, new BoundingVolume(float3.zero, extents));
            return entity;
        }

        [Test]
        public void SpatialIndex_TracksMovesAndDestruction()
        {
            m_Manager.GetTicker(m_Ticker).Register<SpatialSystemGroup>();
            m_Manager.Start();

            var configOwner = World.GetOrCreateSingleton<SpatialIndexConfig>();
            World.SetComponent(configOwner, new SpatialIndexConfig
            {
                Dimension = SpatialDimension.Octree,
                WorldCenter = float3.zero,
                WorldHalfExtent = new float3(200f),
                MaxDepth = 8,
                NodeCapacity = 8,
            });

            var a = SpawnIndexed(new float3(10f, 0f, 0f), new float3(1f));
            var b = SpawnIndexed(new float3(-100f, 0f, 0f), new float3(2f));
            m_Manager.Tick(m_Ticker, 0.016f);

            Assert.That(World.TryGetSingleton<SpatialTree>(out var treeOwner), Is.True);
            ref var tree = ref World.GetComponent<SpatialTree>(treeOwner);
            Assert.That(tree.IsInitialized, Is.True);
            Assert.That(tree.Contains(a), Is.True);
            Assert.That(tree.Contains(b), Is.True);
            Assert.That(tree.Count, Is.EqualTo(2));

            var buffer = new NativeList<Entity>(8, Allocator.Persistent);
            tree.QuerySphere(new float3(10f, 0f, 0f), 5f, ref buffer);
            Assert.That(buffer, Does.Contain(a));
            Assert.That(buffer, Does.Not.Contain(b));

            // 移动 a 到远处
            World.SetComponent(a, new LocalToWorld { Value = float4x4.Translate(new float3(150f, 0f, 0f)) });
            m_Manager.Tick(m_Ticker, 0.016f);

            buffer.Clear();
            tree.QuerySphere(new float3(10f, 0f, 0f), 5f, ref buffer);
            Assert.That(buffer, Is.Empty);
            buffer.Clear();
            tree.QuerySphere(new float3(150f, 0f, 0f), 5f, ref buffer);
            Assert.That(buffer, Does.Contain(a));

            // 销毁 b
            World.DestroyEntity(b);
            m_Manager.Tick(m_Ticker, 0.016f);
            Assert.That(tree.Contains(b), Is.False);
            Assert.That(tree.Count, Is.EqualTo(1));
            buffer.Dispose();
        }

        [Test]
        public void SpatialIndex_QuadXZ_ProjectsOntoPlane()
        {
            m_Manager.GetTicker(m_Ticker).Register<SpatialSystemGroup>();
            m_Manager.Start();

            var configOwner = World.GetOrCreateSingleton<SpatialIndexConfig>();
            World.SetComponent(configOwner, new SpatialIndexConfig
            {
                Dimension = SpatialDimension.QuadXZ,
                WorldCenter = float3.zero,
                WorldHalfExtent = new float3(200f),
                MaxDepth = 8,
                NodeCapacity = 8,
            });

            var entity = SpawnIndexed(new float3(10f, 300f, 10f), new float3(1f)); // 很高的实体
            m_Manager.Tick(m_Ticker, 0.016f);

            var buffer = new NativeList<Entity>(8, Allocator.Persistent);
            Assert.That(World.TryGetSingleton<SpatialTree>(out var treeOwner), Is.True);
            World.GetComponent<SpatialTree>(treeOwner).QueryAABB(
                new float3(5f, -1000f, 5f), new float3(15f, 1000f, 15f), ref buffer);
            Assert.That(buffer, Does.Contain(entity));
            buffer.Dispose();
        }

        [Test]
        public void FrustumCulling_TagsTransitionsOnly()
        {
            m_Manager.GetTicker(m_Ticker).Register<SpatialSystemGroup>();
            m_Manager.Start();

            // 正交视锥：x∈[-10,10]，y∈[-5,5]
            var cameraOwner = World.GetOrCreateSingleton<CameraFrustum>();
            World.SetComponent(cameraOwner, FrustumMath.FromViewProjection(new float4x4(
                new float4(0.1f, 0f, 0f, 0f),
                new float4(0f, 0.2f, 0f, 0f),
                new float4(0f, 0f, -0.02002f, 0f),
                new float4(0f, 0f, -1.002f, 1f))));

            var inView = SpawnIndexed(float3.zero, new float3(1f));
            var outOfView = SpawnIndexed(new float3(100f, 0f, 0f), new float3(1f));

            m_Manager.Tick(m_Ticker, 0.016f);
            Assert.That(World.HasComponent<InView>(inView), Is.True);
            Assert.That(World.HasComponent<InView>(outOfView), Is.False);

            // 出画
            World.SetComponent(inView, new LocalToWorld { Value = float4x4.Translate(new float3(50f, 0f, 0f)) });
            m_Manager.Tick(m_Ticker, 0.016f);
            Assert.That(World.HasComponent<InView>(inView), Is.False);

            // 入画
            World.SetComponent(outOfView, new LocalToWorld { Value = float4x4.Translate(float3.zero) });
            m_Manager.Tick(m_Ticker, 0.016f);
            Assert.That(World.HasComponent<InView>(outOfView), Is.True);

            // 相机单例移除后系统整帧跳过（不抛异常）
            World.RemoveComponent<CameraFrustum>(cameraOwner);
            Assert.DoesNotThrow(() => m_Manager.Tick(m_Ticker, 0.016f));
        }
    }
}
