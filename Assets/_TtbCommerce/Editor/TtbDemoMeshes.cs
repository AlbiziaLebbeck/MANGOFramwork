using System.Collections.Generic;
using UnityEngine;

namespace Mangos.Ttb.Commerce.Editor
{
    // Editable native 3D demonstrators, intentionally distinct from the HTML/product-photo assets.
    internal static class TtbDemoMeshes
    {
        internal static Mesh Torus()
        {
            const int rings = 96, sides = 32; var vertices = new List<Vector3>(); var uv = new List<Vector2>();
            var ceramic = new List<int>(); var glow = new List<int>();
            for (var i = 0; i <= rings; i++)
            {
                var theta = i * Mathf.PI * 2 / rings; var radial = new Vector3(Mathf.Cos(theta), Mathf.Sin(theta), 0);
                for (var j = 0; j <= sides; j++)
                {
                    var phi = j * Mathf.PI * 2 / sides;
                    vertices.Add(radial * (.62f + .19f * Mathf.Cos(phi)) + Vector3.forward * (.19f * Mathf.Sin(phi)));
                    uv.Add(new Vector2((float)i / rings, (float)j / sides));
                    if (i == rings || j == sides) continue;
                    var a = i * (sides + 1) + j; var b = a + sides + 1;
                    var list = j >= 16 && j < 24 ? glow : ceramic;
                    list.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }
            var mesh = new Mesh { name = "TTB Orbit Lamp torus", subMeshCount = 2 }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv);
            mesh.SetTriangles(ceramic, 0); mesh.SetTriangles(glow, 1); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        internal static Mesh Sculpture()
        {
            const int rings = 56, sides = 48; var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            for (var i = 0; i <= rings; i++)
            {
                var t = (float)i / rings; var width = .47f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI)), .65f); var twist = t * 1.7f;
                for (var j = 0; j <= sides; j++)
                {
                    var angle = j * Mathf.PI * 2 / sides; var x = width * Mathf.Cos(angle); var z = width * .6f * Mathf.Sin(angle);
                    vertices.Add(new Vector3(x * Mathf.Cos(twist) - z * Mathf.Sin(twist) + .18f * Mathf.Sin(t * Mathf.PI * 2 - .8f), t * 1.9f,
                        x * Mathf.Sin(twist) + z * Mathf.Cos(twist))); uv.Add(new Vector2((float)j / sides, t));
                    if (i == rings || j == sides) continue;
                    var a = i * (sides + 1) + j; var b = a + sides + 1; triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }
            var mesh = new Mesh { name = "TTB mango wood sculpture" }; mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        internal static Mesh Star()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var outline = new Vector3[10];
            for (var i = 0; i < 10; i++) { var angle = Mathf.PI / 2 + i * Mathf.PI / 5; var radius = i % 2 == 0 ? .8f : .35f; outline[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0); }
            void Face(Vector3 a, Vector3 b, Vector3 c) { var index = vertices.Count; vertices.AddRange(new[] { a, b, c }); triangles.AddRange(new[] { index, index + 1, index + 2 }); }
            for (var i = 0; i < 10; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % 10]; Face(new Vector3(0, 0, -.22f), b + Vector3.back * .05f, a + Vector3.back * .05f);
                Face(new Vector3(0, 0, .16f), a + Vector3.forward * .05f, b + Vector3.forward * .05f);
                Face(a + Vector3.back * .05f, b + Vector3.back * .05f, b + Vector3.forward * .05f);
                Face(a + Vector3.back * .05f, b + Vector3.forward * .05f, a + Vector3.forward * .05f);
            }
            var mesh = new Mesh { name = "TTB Mango Star faceted collectible" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
