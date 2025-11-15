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
    
    private struct MeshBuildData
    {
        public List<Vector3> Vertices;
        public List<int> Triangles;
        public List<Vector2> UVs;
    }

    public static async Task<Mesh> BuildFilledMesh(LineRenderer line)
    {
        if (line == null)
            return null;

        int count = line.positionCount;
        if (count < 3)
            return null;

        // ----- MAIN THREAD: read Unity objects -----

        // 0) Read points in world space
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

        if (worldPoints.Count < 3)
            return null;

        // Cache world→local matrix once on main thread
        Matrix4x4 worldToLocal = line.transform.worldToLocalMatrix;
        string lineName = line.name;

        MeshBuildData data;

        // ----- BACKGROUND THREAD: pure math on data -----
        try
        {
            data = await Task.Run(() =>
            {
                // Convert to local space using cached matrix
                var pts3D = new List<Vector3>(worldPoints.Count);
                for (int i = 0; i < worldPoints.Count; i++)
                    pts3D.Add(worldToLocal.MultiplyPoint3x4(worldPoints[i]));

                return BuildMeshData(pts3D);
            });
        }
        catch (Exception e)
        {
            // Back on main thread – safe to log
            Debug.LogError($"{lineName} BuildFilledMesh - {e.Message}");
            return null;
        }

        if (data.Vertices == null || data.Triangles == null || data.UVs == null)
            return null;

        // ----- MAIN THREAD: create Mesh and touch Unity API -----

        var mesh = new Mesh { name = "FilledArea" };

        mesh.SetVertices(data.Vertices);
        if (data.Vertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetTriangles(data.Triangles, 0, true);
        mesh.SetUVs(0, data.UVs);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    // Pure data method – can safely run in a background thread
    private static MeshBuildData BuildMeshData(List<Vector3> pts3D)
    {
        var result = new MeshBuildData();

        int count = pts3D.Count;
        if (count < 3)
            return result;

        // Centroid in local space – used as plane origin
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < pts3D.Count; i++)
            centroid += pts3D[i];
        centroid /= Mathf.Max(1, pts3D.Count);

        // 1) Compute polygon plane and (u,v) basis with Newell's method
        Vector3 n = ComputeNewellNormal(pts3D);
        if (n.sqrMagnitude < 1e-10f)
            throw new InvalidOperationException("Degenerate polygon normal.");

        n.Normalize();
        GetPlaneBasis(n, out Vector3 u, out Vector3 v);

        // 2) Project to 2D
        var pts2D = new List<Vector2>(pts3D.Count);
        foreach (var p in pts3D)
        {
            Vector3 pr = p - centroid; // origin-invariant projection
            pts2D.Add(new Vector2(Vector3.Dot(pr, u), Vector3.Dot(pr, v)));
        }

        // Build a 2D working copy with guaranteed CCW orientation
        bool ccw = SignedArea(pts2D) >= 0f;
        List<int> order = new List<int>(pts2D.Count);
        if (ccw)
            for (int i = 0; i < pts2D.Count; i++)
                order.Add(i);
        else
            for (int i = pts2D.Count - 1; i >= 0; i--)
                order.Add(i);

        var poly2D = new List<Vector2>(pts2D.Count);
        for (int k = 0; k < order.Count; k++)
            poly2D.Add(pts2D[order[k]]);

        // Boundary edge set (working index space)
        var boundary = new HashSet<(int, int)>();
        for (int i = 0; i < poly2D.Count; i++)
        {
            int j = (i + 1) % poly2D.Count;
            boundary.Add((i, j));
            boundary.Add((j, i));
        }

        // 3) Triangulate via ear clipping, shortest diagonal heuristic
        if (!EarClip_ShortestDiagonal(poly2D, out List<int> trianglesWork))
            throw new InvalidOperationException(
                "Triangulation failed. Check that the polygon is simple and without self-intersections.");

        // Ensure all triangles are CCW
        EnsureTrianglesCCW(poly2D, trianglesWork);

        // Map triangles back to original vertex indices
        var triangles = new List<int>(trianglesWork.Count);
        for (int t = 0; t < trianglesWork.Count; t++)
            triangles.Add(order[trianglesWork[t]]);

        if (pts3D.Count == 0)
            throw new InvalidOperationException("No points to build UVs.");

        // Simple planar UVs using 2D coords, normalized
        Vector2 min = poly2D[0], max = poly2D[0];
        for (int i = 1; i < poly2D.Count; i++)
        {
            min = Vector2.Min(min, poly2D[i]);
            max = Vector2.Max(max, poly2D[i]);
        }

        Vector2 size = max - min;
        if (size.x < 1e-6f) size.x = 1f;
        if (size.y < 1e-6f) size.y = 1f;

        var uvs = new List<Vector2>(pts3D.Count);
        for (int i = 0; i < pts3D.Count; i++)
            uvs.Add(Vector2.zero);

        for (int i = 0; i < poly2D.Count; i++)
            uvs[order[i]] = (poly2D[i] - min) / size;

        result.Vertices = pts3D;
        result.Triangles = triangles;
        result.UVs = uvs;
        return result;

        // ----- Local helpers: pure math -----

        Vector3 ComputeNewellNormal(List<Vector3> pts)
        {
            Vector3 nn = Vector3.zero;
            int c = pts.Count;
            for (int i = 0; i < c; i++)
            {
                Vector3 cur = pts[i];
                Vector3 nxt = pts[(i + 1) % c];
                nn.x += (cur.y - nxt.y) * (cur.z + nxt.z);
                nn.y += (cur.z - nxt.z) * (cur.x + nxt.x);
                nn.z += (cur.x - nxt.x) * (cur.y + nxt.y);
            }

            return nn;
        }

        void GetPlaneBasis(Vector3 normal, out Vector3 uu, out Vector3 vv)
        {
            uu = Mathf.Abs(Vector3.Dot(normal, Vector3.right)) < 0.9f ? Vector3.right : Vector3.up;
            uu = Vector3.Normalize(Vector3.Cross(normal, Vector3.Cross(uu, normal)));
            vv = Vector3.Normalize(Vector3.Cross(normal, uu));
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

                    Vector2 a = poly[i0];
                    Vector2 b = poly[i1];
                    Vector2 c = poly[i2];

                    if (!IsConvex(a, b, c)) continue;
                    if (ContainsAnyPoint(poly, V, i0, i1, i2)) continue;

                    float d2 = (a - c).sqrMagnitude;
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

        bool IsConvex(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - b) > 1e-8f;

        float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        bool ContainsAnyPoint(List<Vector2> poly, List<int> V, int i0, int i1, int i2)
        {
            Vector2 a = poly[i0];
            Vector2 b = poly[i1];
            Vector2 c = poly[i2];

            if (Mathf.Abs(Cross(b - a, c - a)) < 1e-10f)
                return true; // degenerate

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
            Vector2 v0 = c - a;
            Vector2 v1 = b - a;
            Vector2 v2 = p - a;

            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            float denom = dot00 * dot11 - dot01 * dot01;
            if (Mathf.Abs(denom) < 1e-12f) return false;

            float uu = (dot11 * dot02 - dot01 * dot12) / denom;
            float vv = (dot00 * dot12 - dot01 * dot02) / denom;

            return (uu >= -1e-6f) && (vv >= -1e-6f) && (uu + vv <= 1f + 1e-6f);
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