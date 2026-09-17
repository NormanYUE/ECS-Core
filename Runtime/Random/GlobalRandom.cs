using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 全局确定性随机源（单例）。<see cref="Unity.Mathematics.Random"/> 为值类型，
    /// 每次取数都会推进内部状态，使用时需以 ref 读写组件。
    /// 需要并行独立随机流时，应按实体拆分独立的随机组件而非共用本单例。
    /// </summary>
    public struct GlobalRandom : ISingletonComponent
    {
        /// <summary>随机发生器状态。</summary>
        public Random Value;

        public GlobalRandom(uint seed) => Value = new Random(seed);
    }
}
