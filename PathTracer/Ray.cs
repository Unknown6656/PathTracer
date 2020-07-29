using System.Runtime.CompilerServices;

using Unknown6656.Mathematics.LinearAlgebra;

namespace PathTracer
{
    public readonly struct Ray
    {
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public Vector3 Eta { get; }
        public bool Inside { get; }
        public int Depth { get; }


        public static int TotalGenerationCount;

        public Ray(Vector3 origin, Vector3 dir, Vector3 eta, bool inside, int depth = 0)
        {
            TotalGenerationCount++;
            Origin = origin;
            Direction = dir;
            Inside = inside;
            Depth = depth;
            Eta = eta;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 Evaluate(double t) => Origin + Direction * t;

        public override string ToString() => $"o: {Origin}\nd: {Direction}\n";
    }
}
