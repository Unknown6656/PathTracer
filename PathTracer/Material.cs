using Unknown6656.Mathematics.LinearAlgebra;
using Unknown6656.Mathematics;

namespace PathTracer
{
    public readonly struct Color
    {
        /// <summary>ARGB = XYZW</summary>
        public Vector4 Vector { get; }


        public Color(Vector4 v) => Vector = v;

        public override string ToString() => Vector.ToString();

        public Vector3 GetColor(bool premult) => new Vector3(Vector.Y, Vector.Z, Vector.W) * (premult ? Vector.X.Clamp() : Scalar.One);

        public static implicit operator Color(Vector4 v) => new Color(v);
        public static implicit operator Color(Vector3 v) => new Vector4(1, v.X, v.Y, v.Z);
        public static implicit operator Color(uint argb) => 
            argb <= 0xffff ? new Vector4((argb >> 12) & 0xf, (argb >> 8) & 0xf, (argb >> 4) & 0xf, argb & 0xf) / 15d
                           : new Vector4((argb >> 24) & 0xff, (argb >> 16) & 0xff, (argb >> 8) & 0xff, argb & 0xff) / 255d;
        public static implicit operator Color(int argb) => (uint)argb;
    }

    public abstract class Material
    {
        public double Opacity { get; }
        public Vector3 Color { get; }
        public Vector3 PremultipliedColor => Color * Opacity;
        internal Color _color { get; }


        protected Material(Color color)
        {
            _color = color;
            Color = (color.Vector.Y, color.Vector.Z, color.Vector.W);
            Opacity = color.Vector.X.Clamp();
        }
    }

    public sealed class Diffuse
        : Material
    {
        public Diffuse(Color color)
            : base(color)
        {
        }

        public static implicit operator Diffuse(int color) => new Diffuse(color);
        public static implicit operator Diffuse(uint color) => new Diffuse(color);
        public static implicit operator Diffuse(Color color) => new Diffuse(color);
    }

    public sealed class Specular
        : Material
    {
        public double Specularity { get; }
        public double SpecularIndex { get; }


        public Specular(Color color, double specularity, double index)
            : base(color)
        {
            Specularity = specularity;
            SpecularIndex = index;
        }
    }

    public sealed class Glow
        : Material
    {
        public double Intensity { get; }

        public Glow(Color color, double intensity)
            : base(color) => Intensity = intensity.Clamp(0, float.MaxValue);
    }

    public sealed class Reflective
        : Material
    {
        public static Reflective PerfectMirror => new Reflective(1, new Diffuse(0xf000));
        public double Reflectiveness { get; }
        public Material UnderlyingBehaviour { get; }

        public Reflective(double reflectiveness, Material behaviour)
            : base(behaviour._color)
        {
            Reflectiveness = reflectiveness;
            UnderlyingBehaviour = behaviour;
        }
    }

    public sealed class Refractive
        : Material
    {
        public double Refractiveness { get; }
        public Vector3 RefractiveIndex { get; }
        public Material UnderlyingBehaviour { get; }

        public Refractive(double refactiveness, Vector3 index, Material behaviour)
            : base(behaviour._color)
        {
            RefractiveIndex = index;
            Refractiveness = refactiveness;
            UnderlyingBehaviour = behaviour;
        }
    }
}
