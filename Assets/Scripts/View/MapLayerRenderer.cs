public class MapLayerRenderer : MonoBehaviour
{
    public static MapLayerRenderer Instance;
    
    [Header("Material Templates")]
    public Material lineMaterialTemplate, fillMaterialTemplate; 

    [Header("Terrain Projection")] [Tooltip("Layer mask used to raycast against the terrain mesh")] [SerializeField]
    private LayerMask terrainLayerMask = ~0;

    [Tooltip("Height above the terrain from which raycasts start")] [SerializeField]
    private float raycastHeight = 1000f;

    [Header("Coordinate Projection")] [Tooltip("Central longitude of the local projection (deg)")] [SerializeField]
    private double centerLon = 33.2;

    [Tooltip("Central latitude of the local projection (deg)")] [SerializeField]
    private double centerLat = 34.9;

    [Tooltip("Meters per degree (approx for Cyprus)")] [SerializeField]
    private double metersPerDegree = 111000.0;

    [Tooltip("Scale: 1 meter in real world to Unity units")] [SerializeField]
    private float unityScale = 0.001f; // 1 km = 1 unity unit

    [Header("Rendering")] [SerializeField] private Area areaPrefab;

    private readonly Dictionary<string, GameObject> _layerRoots = new();

    private void Awake()
    {
        Instance = this;
        
        Physics.queriesHitBackfaces = true;
        
        Application.targetFrameRate = 60;
    }

    public void RenderLayer(MapLayer layer)
    {
        if (layer == null) return;

        if (layer.LineMaterial == null)
        {
            layer.LineMaterial = new Material(lineMaterialTemplate);
            layer.LineMaterial.SetColor("_BaseColor", layer.Color);
        }
        
        if (layer.FillMaterial == null)
        {
            layer.FillMaterial = new Material(fillMaterialTemplate);
            layer.FillMaterial.SetColor("_BaseColor", new Color(layer.Color.r, layer.Color.g, layer.Color.b, 0.2f));
        }
        
        if (_layerRoots.TryGetValue(layer.Id, out var existingRoot))
        {
            existingRoot.SetActive(layer.Visible);
            return;
        }

        var root = new GameObject($"Layer_{layer.Id}");
        root.transform.SetParent(transform, false);
        _layerRoots[layer.Id] = root;
        root.SetActive(layer.Visible);

        foreach (var feature in layer.Features)
        {
            if (feature.Geometry == null) continue;

            switch (feature.Geometry.Type)
            {
                case GeometryType.Polygon:
                    RenderPolygonFeature(layer, feature, root.transform);
                    break;
                // при необходимости добавим LineString / Point
            }
        }
    }

    public void SetLayerVisible(string id, bool visible)
    {
        if (_layerRoots.TryGetValue(id, out var root))
            root.SetActive(visible);
    }

    private void RenderPolygonFeature(MapLayer layer, MapFeature feature, Transform parent)
    {
        if (feature.Geometry.CoordinatesLonLat.Count == 0 || feature.Geometry.CoordinatesLonLat[0].Count < 2)
            return;
        
        var area = Instantiate(areaPrefab, parent).Setup(layer, feature);
    }

    public Vector3 SnapToTerrainWorld(Vector3 worldPos)
    {
        Vector3 origin = worldPos + Vector3.up * raycastHeight;
        if (Physics.Raycast(origin, Vector3.down, out var hit, raycastHeight * 2f, terrainLayerMask))
            return hit.point;

        return worldPos;
    }

    private Vector3 ToUnityFlatPosition(double lon, double lat)
    {
        double dLon = (lon - centerLon) * System.Math.Cos(centerLat * System.Math.PI / 180.0);
        double dLat = (lat - centerLat);
        double xMeters = dLon * metersPerDegree;
        double zMeters = dLat * metersPerDegree;

        float x = (float) (xMeters * unityScale);
        float z = (float) (zMeters * unityScale);

        return new Vector3(x, 0f, z);
    }

    public Vector3 ToUnityPositionOnTerrain(double lon, double lat)
    {
        var flatPos = ToUnityFlatPosition(lon, lat);
        var origin = flatPos + Vector3.up * raycastHeight;

        if (Physics.Raycast(origin, Vector3.down, out var hit, raycastHeight * 2f, terrainLayerMask))
            return hit.point;

        return flatPos;
    }
}