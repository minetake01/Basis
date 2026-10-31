using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Net.Minetake.FluxHarp
{
    /// <summary>
    /// Everything the instrument draws as light, rebuilt into one mesh each frame: ribbons (graph paths,
    /// wires, outlines) that turn their face to the viewer, and soft dots (voice dots, node knobs) that fade
    /// from a bright core to nothing. One mesh, one additive material, one draw call.
    /// </summary>
    public sealed class FluxGlow
    {
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly List<Vector3> _verts = new List<Vector3>(32768);
        private readonly List<Color> _colors = new List<Color>(32768);
        private readonly List<int> _tris = new List<int>(65536);
        private Vector3 _eye;

        private const int DotSegments = 14;

        public FluxGlow(Material material)
        {
            _material = material;
            _mesh = new Mesh { name = "Flux Glow" };
            _mesh.indexFormat = IndexFormat.UInt32;
            _mesh.MarkDynamic();
        }

        /// <summary>Starts a frame. Ribbons and dots face <paramref name="eye"/> (world space).</summary>
        public void Begin(Vector3 eye)
        {
            _eye = eye;
            _verts.Clear();
            _colors.Clear();
            _tris.Clear();
        }

        /// <summary>A soft round dot: full <paramref name="color"/> at the centre, nothing at <paramref name="radius"/>.</summary>
        public void Dot(Vector3 center, float radius, Color color, float core = 0.35f)
        {
            Vector3 toEye = (_eye - center).normalized;
            Vector3 right = Vector3.Cross(toEye, Mathf.Abs(toEye.y) > 0.95f ? Vector3.forward : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(right, toEye);
            Color clear = new Color(color.r, color.g, color.b, 0);

            int c = _verts.Count;
            _verts.Add(center);
            _colors.Add(color);
            // Inner ring at full brightness gives a solid core; the outer ring fades to nothing.
            for (int i = 0; i < DotSegments; i++)
            {
                float a = i * Mathf.PI * 2 / DotSegments;
                Vector3 dir = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                _verts.Add(center + dir * radius * core);
                _colors.Add(color);
                _verts.Add(center + dir * radius);
                _colors.Add(clear);
            }
            for (int i = 0; i < DotSegments; i++)
            {
                int inner = c + 1 + i * 2;
                int outer = inner + 1;
                int nextInner = c + 1 + ((i + 1) % DotSegments) * 2;
                int nextOuter = nextInner + 1;
                _tris.Add(c); _tris.Add(inner); _tris.Add(nextInner);
                _tris.Add(inner); _tris.Add(outer); _tris.Add(nextOuter);
                _tris.Add(inner); _tris.Add(nextOuter); _tris.Add(nextInner);
            }
        }

        /// <summary>A straight segment of light.</summary>
        public void Segment(Vector3 a, Vector3 b, float width, Color ca, Color cb)
        {
            Vector3 dir = b - a;
            if (dir.sqrMagnitude < 1e-12f) return;
            Vector3 mid = (a + b) * 0.5f;
            Vector3 side = Vector3.Cross(dir, _eye - mid).normalized * (width * 0.5f);
            int i = _verts.Count;
            _verts.Add(a - side); _colors.Add(ca);
            _verts.Add(a + side); _colors.Add(ca);
            _verts.Add(b - side); _colors.Add(cb);
            _verts.Add(b + side); _colors.Add(cb);
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
            _tris.Add(i + 1); _tris.Add(i + 3); _tris.Add(i + 2);
        }

        /// <summary>A connected strip with per-point colour. Joints share vertices so the ribbon has no gaps.</summary>
        public void Strip(List<Vector3> points, List<Color> colors, float width)
        {
            int n = points.Count;
            if (n < 2) return;
            int start = _verts.Count;
            for (int k = 0; k < n; k++)
            {
                Vector3 prev = points[Mathf.Max(0, k - 1)];
                Vector3 next = points[Mathf.Min(n - 1, k + 1)];
                Vector3 tangent = next - prev;
                Vector3 side = Vector3.Cross(tangent, _eye - points[k]);
                side = side.sqrMagnitude > 1e-12f ? side.normalized * (width * 0.5f) : Vector3.zero;
                _verts.Add(points[k] - side); _colors.Add(colors[k]);
                _verts.Add(points[k] + side); _colors.Add(colors[k]);
            }
            for (int k = 0; k < n - 1; k++)
            {
                int i = start + k * 2;
                _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
                _tris.Add(i + 1); _tris.Add(i + 3); _tris.Add(i + 2);
            }
        }

        /// <summary>A circle of light around <paramref name="center"/> in the plane facing the viewer.</summary>
        public void Ring(Vector3 center, float radius, float width, Color color, int segments = 64)
        {
            Vector3 toEye = (_eye - center).normalized;
            Vector3 right = Vector3.Cross(toEye, Mathf.Abs(toEye.y) > 0.95f ? Vector3.forward : Vector3.up).normalized;
            Vector3 up = Vector3.Cross(right, toEye);
            Circle(center, right, up, radius, width, color, segments);
        }

        /// <summary>A circle of light in the plane spanned by <paramref name="u"/> and <paramref name="v"/>.</summary>
        public void Circle(Vector3 center, Vector3 u, Vector3 v, float radius, float width, Color color, int segments = 64)
        {
            Vector3 last = center + u * radius;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                Vector3 p = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
                Segment(last, p, width, color, color);
                last = p;
            }
        }

        /// <summary>The twelve edges of a cube.</summary>
        public void Cube(Vector3 center, Quaternion rotation, float size, float width, Color color)
        {
            float h = size * 0.5f;
            Vector3 C(int x, int y, int z) => center + rotation * new Vector3(x * h, y * h, z * h);
            for (int a = -1; a <= 1; a += 2)
            {
                for (int b = -1; b <= 1; b += 2)
                {
                    Segment(C(-1, a, b), C(1, a, b), width, color, color);
                    Segment(C(a, -1, b), C(a, 1, b), width, color, color);
                    Segment(C(a, b, -1), C(a, b, 1), width, color, color);
                }
            }
        }

        public void End()
        {
            _mesh.Clear(false);
            _mesh.SetVertices(_verts);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0, false);
            // Everything is in world space and moves every frame; a huge bound keeps it from being culled.
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        }

        public void Draw(int layer)
        {
            Graphics.RenderMesh(new RenderParams(_material) { layer = layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false },
                _mesh, 0, Matrix4x4.identity);
        }

        public int VertexCount => _verts.Count;

        /// <summary>The built mesh, for drawing through a renderer instead of <see cref="Draw"/>.</summary>
        public Mesh Mesh => _mesh;
    }
}
