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

    [Header("Label size")]
    [Tooltip("Base character size for labels on very small polygons.")]
    [SerializeField] private float labelBaseSize = 0.2f;

    [Tooltip("How much the label size grows with polygon extent (per 1 Unity unit).")]
    [SerializeField] private float labelExtentFactor = 0.02f;

    [SerializeField] private float _lineWidth = 0.05f;
    [SerializeField] private Vector3 labelOffset = new Vector3(0f, 0f, 0f);

    private MapLayer _layer;
    private MapFeature _feature;
    private List<Vector3> _worldPoints;

    public Area Setup(MapLayer layer, MapFeature feature)
    {
        _layer = layer;
        _feature = feature;

        name = $"{_feature.Name} ({_feature.Id})";

        SetupLine();

        SetupLabel();

        _meshRenderer.sharedMaterial = layer.FillMaterial;

        return this;
    }

    private void SetupLine()
    {
        var ringLonLat = _feature.Geometry.CoordinatesLonLat[0]; // Берём только внешнее кольцо для начала

        _line.positionCount = ringLonLat.Count;
        _line.loop = true;
        _line.useWorldSpace = true;
        _line.widthMultiplier = _layer.LineWidth > 0 ? _layer.LineWidth * _lineWidth : _lineWidth;
        _line.sharedMaterial = _layer.LineMaterial;

        _worldPoints = new List<Vector3>(ringLonLat.Count);

        for (int i = 0; i < ringLonLat.Count; i++)
        {
            var world = MapLayerRenderer.Instance.ToUnityPositionOnTerrain(ringLonLat[i].x, ringLonLat[i].y);
            _worldPoints.Add(world);
            _line.SetPosition(i, world);
        }

        if (_layer.LineSimplifyTolerance > 0f)
            _line.Simplify(_layer.LineSimplifyTolerance);
    }
    
    private void SetupLabel()
    {
        _label.gameObject.SetActive(!string.IsNullOrEmpty(_feature.Name));
        if (!string.IsNullOrEmpty(_feature.Name))
        {
            _label.transform.position = MapLayerRenderer.Instance.SnapToTerrainWorld(ComputeCentroidXZ(_worldPoints)) + labelOffset;
            _label.text = _feature.Name;
            _label.characterSize = ComputeAutoLabelSize(_worldPoints);
            _label.color = _layer.Color;
        }
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
        int n = pts.Count;
        if (n == 0)
            return Vector3.zero;
        if (n == 1)
            return pts[0];

        double area = 0;
        double cx = 0;
        double cz = 0;

        for (int i = 0; i < n; i++)
        {
            Vector3 p0 = pts[i];
            Vector3 p1 = pts[(i + 1) % n];

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
            return sum / n;
        }

        cx /= (6.0 * area);
        cz /= (6.0 * area);

        return new Vector3((float) cx, 0f, (float) cz);
    }

    [ContextMenu("Build Filled Mesh")]
    private void BuildFilledMesh()
    {
        if (MeshUtilities.BuildFilledMesh(_line, out var mesh))
            _meshCollider.sharedMesh = _meshFilter.sharedMesh = mesh;
    }
}