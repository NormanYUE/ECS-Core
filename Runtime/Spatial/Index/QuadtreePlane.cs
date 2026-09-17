namespace Ember.Core
{
    /// <summary>四叉树工作平面。</summary>
    public enum QuadtreePlane : byte
    {
        /// <summary>XY 平面（Unity 2D 惯例，忽略 Z）。</summary>
        XY = 0,

        /// <summary>XZ 平面（3D 俯视/地面场景，忽略 Y）。</summary>
        XZ = 1,
    }
}
