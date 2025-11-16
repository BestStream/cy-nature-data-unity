using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public class Area : MonoBehaviour
{
    [SerializeField] private LineRenderer _line;
    [SerializeField] private TextMesh _label;
    [SerializeField] private MeshFilter _meshFilter;
    [SerializeField] private MeshRenderer _meshRenderer;
    [SerializeField] private MeshCollider _meshCollider;

    [Header("Label size")] [Tooltip("Base character size for labels on very small polygons.")] [SerializeField]
    private float labelBaseSize = 0.2f;

    [Tooltip("How much the label size grows with polygon extent (per 1 Unity unit).")] [SerializeField]
    private float labelExtentFactor = 0.02f;

    [SerializeField] private float _lineWidth = 0.05f;
    [SerializeField] private Vector3 labelOffset = new Vector3(0f, 0f, 0f);

    private MapLayer _layer;
    private MapFeature _feature;
    private List<Vector3> _worldPoints = new();

    public Area Setup(MapLayer mapLayer, MapFeature feature)
    {
        _layer = mapLayer;
        _feature = feature;

        name = $"{_feature.Name} ({_feature.Id})";

        _line.loop = true;
        _line.useWorldSpace = true;
        _line.widthMultiplier = _layer.LineWidth > 0 ? _layer.LineWidth * _lineWidth : _lineWidth;
        _line.sharedMaterial = _layer.LineMaterial;
        if (_feature.Geometry.CoordinatesLonLat.Count > 0)
            CoordinatesToLine(_feature.Geometry.CoordinatesLonLat[0]); // Берём только внешнее кольцо для начала

        _label.gameObject.SetActive(false);
        _label.text = _feature.Name;
        _label.color = _layer.Color;
        RebuildLabel();

        _meshRenderer.sharedMaterial = _layer.FillMaterial;
        RebuildMesh();

        return this;
    }

    private void RebuildLabel()
    {
        _label.transform.position = MapLayerRenderer.Instance.SnapToTerrainWorld(ComputeCentroidXZ(_worldPoints)) + labelOffset;
        _label.characterSize = 0.1f;// ComputeAutoLabelSize(_worldPoints);
    }

    private float ComputeAutoLabelSize(List<Vector3> pts)
    {
        if (pts == null || pts.Count == 0)
            return labelBaseSize;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
        }

        float extentX = maxX - minX;
        float extentZ = maxZ - minZ;
        float extent = Mathf.Max(extentX, extentZ);

        // extent ~ 0 => минимальный размер
        if (extent <= 0.000001f)
            return labelBaseSize;

        // Линейная зависимость: базовый размер + рост от размера полигона
        // При необходимости можно отрегулировать labelBaseSize и labelExtentFactor в инспекторе.
        float size = labelBaseSize + extent * labelExtentFactor;

        // На всякий случай не даём уйти в ноль или отрицательные значения
        if (size < 0.00001f)
            size = 0.00001f;

        return size;
    }

    private Vector3 ComputeCentroidXZ(List<Vector3> pts)
    {
        if (pts == null || pts.Count == 0)
            return Vector3.zero;

        if (pts.Count == 1)
            return pts[0];

        double area = 0;
        double cx = 0;
        double cz = 0;

        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p0 = pts[i];
            Vector3 p1 = pts[(i + 1) % pts.Count];

            double x0 = p0.x;
            double z0 = p0.z;
            double x1 = p1.x;
            double z1 = p1.z;

            double cross = x0 * z1 - x1 * z0;
            area += cross;
            cx += (x0 + x1) * cross;
            cz += (z0 + z1) * cross;
        }

        area *= 0.5;
        if (System.Math.Abs(area) < 1e-6)
        {
            // fallback: простое среднее
            Vector3 sum = Vector3.zero;
            foreach (var p in pts)
                sum += p;
            return sum / pts.Count;
        }

        cx /= (6.0 * area);
        cz /= (6.0 * area);

        return new Vector3((float)cx, 0f, (float)cz);
    }

    private async void RebuildMesh()
    {
        if (_line.positionCount < 3)
        {
            _meshFilter.sharedMesh = _meshCollider.sharedMesh = null;
            return;
        }

        var mesh = await MeshUtilities.BuildFilledMesh(_line);
        if (mesh != null)
            _meshCollider.sharedMesh = _meshFilter.sharedMesh = mesh;
    }

    public void Highlight(bool highlighted)
    {
        if(!highlighted && _layer.ForceHighlight)
            return;
        
        _meshRenderer.enabled = highlighted;
        _label.gameObject.SetActive(highlighted && !string.IsNullOrEmpty(_label.text));
    }

    public void AddPoint(Vector3 point)
    {
        _worldPoints.Add(point);

        _line.positionCount = _worldPoints.Count;
        _line.loop = _worldPoints.Count > 2;

        for (int i = 0; i < _worldPoints.Count; i++)
            _line.SetPosition(i, _worldPoints[i]);

        if (_worldPoints.Count >= 3)
        {
            RebuildLabel();
            RebuildMesh();
        }
    }
    
    private void CoordinatesToLine(List<Vector2> ringLonLat)
    {
        _worldPoints = new List<Vector3>(ringLonLat.Count);
        _line.positionCount = ringLonLat.Count;

        for (int i = 0; i < ringLonLat.Count; i++)
        {
            var world = MapLayerRenderer.Instance.ToUnityPositionOnTerrain(ringLonLat[i].x, ringLonLat[i].y);
            _worldPoints.Add(world);
            _line.SetPosition(i, world);
        }

        if (_layer.LineSimplifyTolerance > 0f)
            _line.Simplify(_layer.LineSimplifyTolerance);
    }

    public List<Vector2> LineToCoordinates()
    {
        var coordinatesLonLat = new List<Vector2>(_line.positionCount);

        for (int i = 0; i < _line.positionCount; i++)
        {
            var lonlat = MapLayerRenderer.Instance.ToLonLat(_line.GetPosition(i));
            coordinatesLonLat.Add(lonlat);
        }

        return coordinatesLonLat;
    }
    /// <summary>
    /// Computes the geometric intersection of two or more Areas and returns the resulting polygon as a list of lon/lat coordinates (single outer ring).
    /// If there is no common intersection, returns an empty list.
    /// </summary>
    public static List<Vector2> ComputeIntersectionLonLat(IList<Area> areas)
    {
        if (areas == null || areas.Count == 0)
            return new List<Vector2>();

        // Start from the first area's polygon
        var result = new List<Vector2>(areas[0].LineToCoordinates());
        EnsureCounterClockwise(result);

        for (int i = 1; i < areas.Count && result.Count > 0; i++)
        {
            var clip = new List<Vector2>(areas[i].LineToCoordinates());
            EnsureCounterClockwise(clip);

            result = ClipPolygonWithPolygon(result, clip);
        }

        return result;
    }

    /// <summary>
    /// Sutherland–Hodgman polygon clipping: clips subject polygon by a convex clip polygon. Both polygons must be in CCW winding order.
    /// </summary>
    private static List<Vector2> ClipPolygonWithPolygon(List<Vector2> subject, List<Vector2> clip)
    {
        if (subject == null || subject.Count == 0 || clip == null || clip.Count == 0)
            return new List<Vector2>();

        var outputList = new List<Vector2>(subject);

        for (int i = 0; i < clip.Count; i++)
        {
            var clipA = clip[i];
            var clipB = clip[(i + 1) % clip.Count];

            var inputList = outputList;
            outputList = new List<Vector2>();

            if (inputList.Count == 0)
                break;

            var S = inputList[inputList.Count - 1];
            for (int j = 0; j < inputList.Count; j++)
            {
                var E = inputList[j];

                bool E_inside = IsInside(E, clipA, clipB);
                bool S_inside = IsInside(S, clipA, clipB);

                if (E_inside)
                {
                    if (!S_inside)
                    {
                        if (LineIntersection(S, E, clipA, clipB, out var inter))
                            outputList.Add(inter);
                    }
                    outputList.Add(E);
                }
                else if (S_inside)
                {
                    if (LineIntersection(S, E, clipA, clipB, out var inter))
                        outputList.Add(inter);
                }

                S = E;
            }
        }

        return outputList;
    }

    /// <summary>
    /// Returns true if point p is on the left side of the directed edge (a -> b).
    /// Assumes CCW winding for the clip polygon.
    /// </summary>
    private static bool IsInside(Vector2 p, Vector2 a, Vector2 b)
    {
        return Cross(b - a, p - a) >= 0f;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    /// <summary>
    /// Computes the intersection point between two line segments (p1->p2) and (p3->p4).
    /// Returns true if they intersect (infinite lines), result is stored in 'intersection'.
    /// </summary>
    private static bool LineIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 intersection)
    {
        intersection = Vector2.zero;

        var d1 = p2 - p1;
        var d2 = p4 - p3;

        float denom = Cross(d1, d2);
        if (Mathf.Abs(denom) < 1e-8f)
            return false; // Parallel or nearly parallel

        var diff = p3 - p1;
        float t = Cross(diff, d2) / denom;
        intersection = p1 + d1 * t;
        return true;
    }

    /// <summary>
    /// Ensures that the polygon points are in counter-clockwise order.
    /// </summary>
    private static void EnsureCounterClockwise(List<Vector2> poly)
    {
        if (poly == null || poly.Count < 3)
            return;

        if (SignedArea(poly) < 0f)
            poly.Reverse();
    }

    private static float SignedArea(List<Vector2> poly)
    {
        double area = 0.0;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i, i++)
        {
            var p0 = poly[j];
            var p1 = poly[i];
            area += (double)p0.x * p1.y - (double)p1.x * p0.y;
        }
        return (float)(area * 0.5);
    }
}