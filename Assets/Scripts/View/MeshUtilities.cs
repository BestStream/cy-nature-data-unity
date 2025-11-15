public class MeshUtilities
{
    public static void RecenterToCentroid(LineRenderer line)
    {
        int count = line.positionCount;
        if (count == 0) return;

        // Collect current points in WORLD space
        var worldPoints = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
            worldPoints.Add(line.transform.TransformPoint(line.GetPosition(i)));

        // Compute centroid in world space
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < worldPoints.Count; i++)
            centroid += worldPoints[i];
        centroid /= Mathf.Max(1, worldPoints.Count);

        // Move this object to the centroid
        line.transform.position = centroid;

        // Re-write positions so world points remain the same
        for (int i = 0; i < worldPoints.Count; i++)
            line.SetPosition(i, line.transform.InverseTransformPoint(worldPoints[i]));
    }

    public static Task<Mesh> BuildFilledMesh(LineRenderer line)
    {
        int count = line.positionCount;
        if (count < 3) return null;

        // 0) Read points and convert to WORLD space consistently
        var worldPoints = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
            worldPoints.Add(line.transform.TransformPoint(line.GetPosition(i)));

        // Remove duplicate closing point if present (first == last)
        if (worldPoints.Count >= 2)
        {
            Vector3 first = worldPoints[0];
            Vector3 last = worldPoints[worldPoints.Count - 1];
            if ((last - first).sqrMagnitude < 1e-10f)
                worldPoints.RemoveAt(worldPoints.Count - 1);
        }

        // 1) Convert to local space of the MeshFilter's transform so vertices match exactly where the mesh is rendered
        var pts3D = new List<Vector3>(worldPoints.Count);
        for (int i = 0; i < worldPoints.Count; i++)
            pts3D.Add(line.transform.InverseTransformPoint(worldPoints[i]));

        count = pts3D.Count;
        if (count < 3) return null;

        // Centroid in local space – used as plane origin
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < pts3D.Count; i++) centroid += pts3D[i];
        centroid /= Mathf.Max(1, pts3D.Count);

        // 1) Compute polygon plane and (u,v) basis with Newell's method
        Vector3 n = ComputeNewellNormal(pts3D);
        if (n.sqrMagnitude < 1e-10f)
        {
            Debug.LogError(line.name + "BuildFilledMesh - Degenerate polygon normal.");
            return null;
        }

        n.Normalize();
        GetPlaneBasis(n, out Vector3 u, out Vector3 v);

        // 2) Project to 2D
        var pts2D = new List<Vector2>(pts3D.Count);
        foreach (var p in pts3D)
        {
            Vector3 pr = p - centroid; // make projection origin-invariant
            pts2D.Add(new Vector2(Vector3.Dot(pr, u), Vector3.Dot(pr, v)));
        }

        // Build a 2D working copy with guaranteed CCW orientation, without touching pts3D order
        bool ccw = SignedArea(pts2D) >= 0f;
        List<int> order = new List<int>(pts2D.Count);
        if (ccw)
            for (int i = 0; i < pts2D.Count; i++)
                order.Add(i);
        else
            for (int i = pts2D.Count - 1; i >= 0; i--)
                order.Add(i);

        var poly2D = new List<Vector2>(pts2D.Count);
        for (int k = 0; k < order.Count; k++) poly2D.Add(pts2D[order[k]]);

        // Build boundary edge set in the working index space
        var boundary = new HashSet<(int, int)>();
        for (int i = 0; i < poly2D.Count; i++)
        {
            int j = (i + 1) % poly2D.Count;
            boundary.Add((i, j));
            boundary.Add((j, i));
        }

        // 3) Triangulate via ear clipping, choosing the ear with the SHORTEST diagonal each step (nearest-vertex heuristic)
        if (!EarClip_ShortestDiagonal(poly2D, out List<int> trianglesWork))
        {
            Debug.LogError(line.name +
                           "BuildFilledMesh - Triangulation failed. Check that the polygon is simple and without self-intersections.");
            return null;
        }

        // Ensure all triangles are CCW
        EnsureTrianglesCCW(poly2D, trianglesWork);

        // 5) Map triangles back to original vertex indices so mesh vertices exactly match line points order
        var triangles = new List<int>(trianglesWork.Count);
        for (int t = 0; t < trianglesWork.Count; t++)
            triangles.Add(order[trianglesWork[t]]);

        // 5) Build mesh
        var mesh = new Mesh { name = "FilledArea" };
        // Vertices are exactly the line points in mesh local space
        mesh.SetVertices(pts3D);
        if (pts3D.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetTriangles(triangles, 0, true);

        // Simple planar UVs using 2D coords, normalized, using working 2D polygon
        if (pts3D.Count == 0)
        {
            Debug.LogError(line.name + "BuildFilledMesh - No points to build UVs.");
            return null;
        }

        // Use working 2D polygon for UVs, but map back to original order
        Vector2 min = poly2D[0], max = poly2D[0];
        for (int i = 1; i < poly2D.Count; i++)
        {
            min = Vector2.Min(min, poly2D[i]);
            max = Vector2.Max(max, poly2D[i]);
        }

        Vector2 size = max - min;
        if (size.x < 1e-6f) size.x = 1f;
        if (size.y < 1e-6f) size.y = 1f;
        var uvs = new Vector2[pts3D.Count];
        for (int i = 0; i < poly2D.Count; i++)
            uvs[order[i]] = (poly2D[i] - min) / size;

        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Debug.Log(line.name + $"BuildFilledMesh - Mesh built. V:{pts3D.Count} T:{triangles.Count / 3}");
        return Task.FromResult(mesh);

        bool EarClip_ShortestDiagonal(List<Vector2> poly, out List<int> tris)
        {
            tris = new List<int>();
            int n = poly.Count;
            if (n < 3) return false;
            List<int> V = new List<int>(n);
            for (int i = 0; i < n; i++) V.Add(i);

            while (V.Count > 3)
            {
                int bestEarIndex = -1;
                float bestDiag2 = float.PositiveInfinity;

                for (int i = 0; i < V.Count; i++)
                {
                    int i0 = V[(i + V.Count - 1) % V.Count];
                    int i1 = V[i];
                    int i2 = V[(i + 1) % V.Count];
                    Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
                    if (!IsConvex(a, b, c)) continue;
                    if (ContainsAnyPoint(poly, V, i0, i1, i2)) continue;
                    float d2 = (a - c).sqrMagnitude; // diagonal length squared
                    if (d2 < bestDiag2)
                    {
                        bestDiag2 = d2;
                        bestEarIndex = i;
                    }
                }

                if (bestEarIndex == -1) return false;
                int aIdx = V[(bestEarIndex + V.Count - 1) % V.Count];
                int bIdx = V[bestEarIndex];
                int cIdx = V[(bestEarIndex + 1) % V.Count];
                tris.Add(aIdx);
                tris.Add(bIdx);
                tris.Add(cIdx);
                V.RemoveAt(bestEarIndex);
            }

            tris.Add(V[0]);
            tris.Add(V[1]);
            tris.Add(V[2]);
            return true;
        }

        Vector3 ComputeNewellNormal(List<Vector3> pts)
        {
            Vector3 n = Vector3.zero;
            int count = pts.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 cur = pts[i];
                Vector3 nxt = pts[(i + 1) % count];
                n.x += (cur.y - nxt.y) * (cur.z + nxt.z);
                n.y += (cur.z - nxt.z) * (cur.x + nxt.x);
                n.z += (cur.x - nxt.x) * (cur.y + nxt.y);
            }

            return n;
        }

        void GetPlaneBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            u = Mathf.Abs(Vector3.Dot(normal, Vector3.right)) < 0.9f ? Vector3.right : Vector3.up;
            u = Vector3.Normalize(Vector3.Cross(normal, Vector3.Cross(u, normal)));
            v = Vector3.Normalize(Vector3.Cross(normal, u));
        }

        float SignedArea(List<Vector2> p)
        {
            double a = 0;
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += (double)p[i].x * p[j].y - (double)p[j].x * p[i].y;
            }

            return (float)(0.5 * a);
        }

        bool IsConvex(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - b) > 1e-8f;

        float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        bool ContainsAnyPoint(List<Vector2> poly, List<int> V, int i0, int i1, int i2)
        {
            Vector2 a = poly[i0], b = poly[i1], c = poly[i2];
            if (Mathf.Abs(Cross(b - a, c - a)) < 1e-10f) return true; // degenerate
            for (int k = 0; k < V.Count; k++)
            {
                int idx = V[k];
                if (idx == i0 || idx == i1 || idx == i2) continue;
                if (PointInTriangle(poly[idx], a, b, c)) return true;
            }

            return false;
        }

        bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = c - a, v1 = b - a, v2 = p - a;
            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);
            float denom = dot00 * dot11 - dot01 * dot01;
            if (Mathf.Abs(denom) < 1e-12f) return false;
            float u = (dot11 * dot02 - dot01 * dot12) / denom;
            float v = (dot00 * dot12 - dot01 * dot02) / denom;

            return (u >= -1e-6f) && (v >= -1e-6f) && (u + v <= 1f + 1e-6f);
        }

        void EnsureTrianglesCCW(List<Vector2> pts, List<int> tris)
        {
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t];
                int b = tris[t + 1];
                int c = tris[t + 2];
                Vector2 A = pts[a];
                Vector2 B = pts[b];
                Vector2 C = pts[c];
                if (Cross(B - A, C - A) < 0f)
                {
                    // swap to make CCW
                    tris[t + 1] = c;
                    tris[t + 2] = b;
                }
            }
        }
    }
}