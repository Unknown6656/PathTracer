using System;

using MathLibrary.LinearAlgebra;

namespace PathTracer
{
    public enum SurfaceType
    {
        DIFFUSE,
        SPECULAR,
        REFRACTIVE
    };

    public readonly struct Color
    {
        /// <summary>ARGB = XYZW</summary>
        public Vector4 Vector { get; }

        public Color(Vector4 v) => Vector = v;

        public override string ToString() => Vector.ToString();

        public static implicit operator Color(Vector4 v) => new Color(v);
        public static implicit operator Color(Vector3 v) => new Vector4(1, v.X, v.Y, v.Z);
        public static implicit operator Color(uint argb) => new Vector4((argb >> 24) & 0xff, (argb >> 16) & 0xff, (argb >> 8) & 0xff, argb & 0xff);
        public static implicit operator Color(int argb) => (uint)argb;
    }

    public sealed class Material
    {
        //Color Diffuse;
        //Color Luminance;
        //double Specular;
        //double Translucent;




        public SurfaceType SurfaceType { get; }
        public Vector3 Color { get; set; }
        public double Luminance { get; set; }


        public Material(SurfaceType tp, Vector3 color, double luminance = 0)
        {
            SurfaceType = tp;
            Luminance = luminance;
            Color = color;
        }
    }
}
