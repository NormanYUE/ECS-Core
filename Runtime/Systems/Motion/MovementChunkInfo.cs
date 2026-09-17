using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Ember.Core
{
    /// <summary>单个 Chunk 的运动积分输入（列指针 + 行数）。</summary>
    public struct MovementChunkInfo
    {
        /// <summary>LocalTransform 列指针。</summary>
        [NativeDisableUnsafePtrRestriction] public long TransformPtr;

        /// <summary>LinearVelocity 列指针（本块无该组件时为 0）。</summary>
        [NativeDisableUnsafePtrRestriction] public long LinearPtr;

        /// <summary>AngularVelocity 列指针（本块无该组件时为 0）。</summary>
        [NativeDisableUnsafePtrRestriction] public long AngularPtr;

        /// <summary>行数。</summary>
        public int Count;
    }
}
