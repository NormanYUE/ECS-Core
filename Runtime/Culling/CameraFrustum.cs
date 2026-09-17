using Unity.Mathematics;

namespace Ember.Core
{
    /// <summary>
    /// 相机视锥（单例）。6 个归一化平面（法线朝内，xyz=法线、w=距离项），
    /// 由 Unity 侧桥接系统每帧从相机 ViewProjection 矩阵提取写入
    /// （可用 <see cref="FrustumMath.FromViewProjection"/>）。
    /// </summary>
    public struct CameraFrustum : ISingletonComponent
    {
        /// <summary>左平面。</summary>
        public float4 Left;
        /// <summary>右平面。</summary>
        public float4 Right;
        /// <summary>下平面。</summary>
        public float4 Bottom;
        /// <summary>上平面。</summary>
        public float4 Top;
        /// <summary>近平面。</summary>
        public float4 Near;
        /// <summary>远平面。</summary>
        public float4 Far;

        /// <summary>按索引取平面（0-5：左、右、下、上、近、远）。</summary>
        public float4 GetPlane(int index)
        {
            switch (index)
            {
                case 0: return Left;
                case 1: return Right;
                case 2: return Bottom;
                case 3: return Top;
                case 4: return Near;
                default: return Far;
            }
        }

        /// <summary>平面数量。</summary>
        public const int PlaneCount = 6;
    }
}
