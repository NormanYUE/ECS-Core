namespace Ember.Core
{
    /// <summary>空间索引维度模式。</summary>
    public enum SpatialDimension : byte
    {
        /// <summary>XY 平面四叉树（2D）。</summary>
        QuadXY = 0,

        /// <summary>XZ 平面四叉树（3D 俯视/地面场景）。</summary>
        QuadXZ = 1,

        /// <summary>三维八叉树。</summary>
        Octree = 2,
    }
}
