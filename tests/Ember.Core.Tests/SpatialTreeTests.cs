using System;
using System.Collections.Generic;
using System.Security;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Core.Tests
{
    /// <summary>
    /// 空间树算法测试。树存储在 World 托管 buffer 中，随 World.Dispose 自动释放，
    /// 测试无需任何手动清理。依赖 Unity 原生容器，CLI 下自动跳过。
    /// </summary>
    [Parallelizable(ParallelScope.None)]
    [TestFixture]
    public class SpatialTreeTests
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

        private static Entity E(int index) => new Entity(index, 1);

        private SpatialTreeView CreateTree(SpatialDimension dim, float3 center, float3 halfExtent,
            int maxDepth = 8, int nodeCapacity = 8)
            => m_World.EnsureSpatialTree(new SpatialIndexConfig
            {
                Dimension = dim,
                WorldCenter = center,
                WorldHalfExtent = halfExtent,
                MaxDepth = maxDepth,
                NodeCapacity = nodeCapacity,
            });

        private static float3 RandomPoint(System.Random r, float range)
            => new float3(
                (float)r.NextDouble() * 2f * range - range,
                (float)r.NextDouble() * 2f * range - range,
                (float)r.NextDouble() * 2f * range - range);

        private static HashSet<int> BruteForceAABB(
            Dictionary<int, (float3 c, float3 e)> items, float3 min, float3 max)
        {
            var hits = new HashSet<int>();
            foreach (var kv in items)
            {
                if (math.all(kv.Value.c - kv.Value.e <= max) && math.all(kv.Value.c + kv.Value.e >= min))
                    hits.Add(kv.Key);
            }
            return hits;
        }

        private static HashSet<int> QueryIndices(SpatialTreeView tree, float3 min, float3 max,
            NativeList<Entity> buffer)
        {
            buffer.Clear();
            tree.QueryAABB(min, max, ref buffer);
            var hits = new HashSet<int>();
            foreach (var e in buffer) hits.Add(e.Index);
            return hits;
        }

        [Test]
        public void Quadtree_Insert_TracksCountAndContains()
        {
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(100f));
            tree.Insert(E(1), new float3(10f, 10f, 0f), new float3(1f));
            tree.Insert(E(2), new float3(-50f, 20f, 0f), new float3(2f));

            Assert.That(tree.Count, Is.EqualTo(2));
            Assert.That(tree.Contains(E(1)), Is.True);
            Assert.That(tree.Contains(E(3)), Is.False);
        }

        [Test]
        public void Quadtree_InsertDuplicate_Throws()
        {
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(100f));
            tree.Insert(E(1), float3.zero, new float3(1f));

            Assert.Throws<InvalidOperationException>(() => tree.Insert(E(1), float3.zero, new float3(1f)));
        }

        [Test]
        public void Quadtree_QueryAABB_MatchesBruteForce()
        {
            var random = new System.Random(1234);
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(128f));
            var items = new Dictionary<int, (float3, float3)>();
            var buffer = new NativeList<Entity>(64, Allocator.Persistent);

            for (int i = 0; i < 1000; i++)
            {
                float3 c = RandomPoint(random, 100f);
                c.z = 0f;
                float3 e = new float3(0.5f);
                tree.Insert(E(i), c, e);
                items[i] = (c, e);
            }

            for (int q = 0; q < 200; q++)
            {
                float3 qc = RandomPoint(random, 110f);
                float size = (float)random.NextDouble() * 30f + 1f;
                float3 qs = new float3(size);
                var expected = BruteForceAABB(items, qc - qs, qc + qs);
                var actual = QueryIndices(tree, qc - qs, qc + qs, buffer);
                Assert.That(actual, Is.EquivalentTo(expected), $"query {q} mismatch");
            }
            buffer.Dispose();
        }

        [Test]
        public void Quadtree_RemoveHalf_MatchesBruteForce()
        {
            var random = new System.Random(77);
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(128f));
            var items = new Dictionary<int, (float3, float3)>();
            var buffer = new NativeList<Entity>(64, Allocator.Persistent);

            for (int i = 0; i < 500; i++)
            {
                float3 c = RandomPoint(random, 100f);
                tree.Insert(E(i), c, new float3(0.5f));
                items[i] = (c, new float3(0.5f));
            }
            for (int i = 0; i < 250; i += 2)
            {
                Assert.That(tree.Remove(E(i)), Is.True);
                items.Remove(i);
            }

            Assert.That(tree.Count, Is.EqualTo(items.Count));
            Assert.That(tree.Remove(E(0)), Is.False); // 已移除

            for (int q = 0; q < 100; q++)
            {
                float3 qc = RandomPoint(random, 110f);
                float3 qs = new float3(20f);
                Assert.That(QueryIndices(tree, qc - qs, qc + qs, buffer),
                    Is.EquivalentTo(BruteForceAABB(items, qc - qs, qc + qs)));
            }
            buffer.Dispose();
        }

        [Test]
        public void Quadtree_Update_MovesElement()
        {
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(100f));
            var buffer = new NativeList<Entity>(8, Allocator.Persistent);
            tree.Insert(E(1), new float3(-80f, -80f, 0f), new float3(1f));

            tree.Update(E(1), new float3(90f, 90f, 0f), new float3(1f));

            tree.QueryAABB(new float3(89f, 89f, -1f), new float3(91f, 91f, 1f), ref buffer);
            Assert.That(buffer.Length, Is.EqualTo(1));

            buffer.Clear();
            tree.QueryAABB(new float3(-81f, -81f, -1f), new float3(-79f, -79f, 1f), ref buffer);
            Assert.That(buffer.Length, Is.EqualTo(0));
            buffer.Dispose();
        }

        [Test]
        public void Quadtree_DeepSubdivision_HandlesDenseCluster()
        {
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(10f),
                maxDepth: 10, nodeCapacity: 1);
            var buffer = new NativeList<Entity>(256, Allocator.Persistent);
            var random = new System.Random(5);

            for (int i = 0; i < 200; i++)
            {
                float3 c = RandomPoint(random, 1f);
                c.z = 0f;
                tree.Insert(E(i), c, new float3(0.01f));
            }

            Assert.That(tree.Count, Is.EqualTo(200));
            tree.QueryAABB(new float3(-2f, -2f, -1f), new float3(2f, 2f, 1f), ref buffer);
            Assert.That(buffer.Length, Is.EqualTo(200));
            buffer.Dispose();
        }

        [Test]
        public void Quadtree_QuerySphere_MatchesBruteForce()
        {
            var random = new System.Random(42);
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(100f));
            var items = new Dictionary<int, (float3, float3)>();

            for (int i = 0; i < 300; i++)
            {
                float3 c = RandomPoint(random, 80f);
                tree.Insert(E(i), c, new float3(0.5f));
                items[i] = (c, new float3(0.5f));
            }

            var buffer = new NativeList<Entity>(64, Allocator.Persistent);
            for (int q = 0; q < 50; q++)
            {
                float3 qc = RandomPoint(random, 90f);
                float radius = (float)random.NextDouble() * 25f + 1f;
                buffer.Clear();
                tree.QuerySphere(qc, radius, ref buffer);
                var actual = new HashSet<int>();
                foreach (var e in buffer) actual.Add(e.Index);

                var expected = new HashSet<int>();
                foreach (var kv in items)
                {
                    float3 closest = math.clamp(qc, kv.Value.Item1 - kv.Value.Item2, kv.Value.Item1 + kv.Value.Item2);
                    if (math.distancesq(closest, qc) <= radius * radius)
                        expected.Add(kv.Key);
                }
                Assert.That(actual, Is.EquivalentTo(expected), $"sphere query {q} mismatch");
            }
            buffer.Dispose();
        }

        [Test]
        public void Sweep_RemovesEntitiesNotTouchedThisTick()
        {
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(100f));
            tree.Insert(E(1), new float3(10f, 10f, 0f), new float3(1f));
            tree.Insert(E(2), new float3(-10f, -10f, 0f), new float3(1f));

            // 新一轮事务：只有 E(1) 被 Update 标记存活，E(2) 应被清扫
            tree.BeginTick();
            tree.Update(E(1), new float3(10f, 10f, 0f), new float3(1f));
            tree.EndTick();

            Assert.That(tree.Contains(E(1)), Is.True);
            Assert.That(tree.Contains(E(2)), Is.False);
            Assert.That(tree.Count, Is.EqualTo(1));
        }

        [Test]
        public void Quadtree_OutOfRootBounds_StillQueryable()
        {
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(10f));
            tree.Insert(E(1), new float3(10000f, 0f, 0f), new float3(1f));

            var buffer = new NativeList<Entity>(8, Allocator.Persistent);
            tree.QueryAABB(new float3(9000f, -5f, -1f), new float3(11000f, 5f, 1f), ref buffer);
            Assert.That(buffer.Length, Is.EqualTo(1));
            buffer.Dispose();
        }

        [Test]
        public void Quadtree_XZPlane_IgnoresY()
        {
            var tree = CreateTree(SpatialDimension.QuadXZ, float3.zero, new float3(100f));
            tree.Insert(E(1), new float3(10f, 500f, 10f), new float3(1f)); // Y 很高，XZ 上仍在范围内

            var buffer = new NativeList<Entity>(8, Allocator.Persistent);
            tree.QueryAABB(new float3(9f, -1000f, 9f), new float3(11f, 1000f, 11f), ref buffer);
            Assert.That(buffer.Length, Is.EqualTo(1));
            buffer.Dispose();
        }

        [Test]
        public void Quadtree_Clear_ResetsTree()
        {
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(100f));
            for (int i = 0; i < 50; i++)
                tree.Insert(E(i), new float3(i % 10f, i / 10f, 0f), new float3(0.5f));

            tree.Clear();

            Assert.That(tree.Count, Is.EqualTo(0));
            var buffer = new NativeList<Entity>(64, Allocator.Persistent);
            tree.QueryAABB(new float3(-200f), new float3(200f), ref buffer);
            Assert.That(buffer.Length, Is.EqualTo(0));

            tree.Insert(E(1000), float3.zero, new float3(1f));
            Assert.That(tree.Contains(E(1000)), Is.True);
            buffer.Dispose();
        }

        [Test]
        public void Octree_QueryAABB_MatchesBruteForce()
        {
            var random = new System.Random(999);
            var tree = CreateTree(SpatialDimension.Octree, float3.zero, new float3(128f));
            var items = new Dictionary<int, (float3, float3)>();
            var buffer = new NativeList<Entity>(64, Allocator.Persistent);

            for (int i = 0; i < 800; i++)
            {
                float3 c = RandomPoint(random, 100f);
                float3 e = new float3(0.5f);
                tree.Insert(E(i), c, e);
                items[i] = (c, e);
            }

            for (int q = 0; q < 200; q++)
            {
                float3 qc = RandomPoint(random, 110f);
                float size = (float)random.NextDouble() * 30f + 1f;
                float3 qs = new float3(size);
                Assert.That(QueryIndices(tree, qc - qs, qc + qs, buffer),
                    Is.EquivalentTo(BruteForceAABB(items, qc - qs, qc + qs)), $"query {q} mismatch");
            }
            buffer.Dispose();
        }

        [Test]
        public void Octree_UpdateRemove_WorksAcrossBoundaries()
        {
            var tree = CreateTree(SpatialDimension.Octree, float3.zero, new float3(100f));
            var buffer = new NativeList<Entity>(8, Allocator.Persistent);
            tree.Insert(E(1), new float3(-50f, -50f, -50f), new float3(1f));

            tree.Update(E(1), new float3(50f, 50f, 50f), new float3(1f));
            tree.QueryAABB(new float3(49f), new float3(51f), ref buffer);
            Assert.That(buffer.Length, Is.EqualTo(1));

            Assert.That(tree.Remove(E(1)), Is.True);
            Assert.That(tree.Count, Is.EqualTo(0));
            buffer.Dispose();
        }

        [Test]
        public void EntitySlotReuse_StaleMappingRejected()
        {
            var tree = CreateTree(SpatialDimension.QuadXY, float3.zero, new float3(100f));
            tree.Insert(E(7), new float3(10f, 10f, 0f), new float3(1f)); // Index 7, Version 1
            tree.Remove(E(7));

            // 同 Index 不同 Version 的"新实体"不得命中旧映射
            var recycled = new Entity(7, 2);
            Assert.That(tree.Contains(recycled), Is.False);
            Assert.That(tree.Remove(recycled), Is.False);
        }
    }
}
