public enum GeometryType
{
    Point,
    LineString,
    Polygon
}

public class MapGeometry
{
    public GeometryType Type;

    /// <summary>
    /// Геометрия в широте/долготе.
    /// Для Point: первый элемент списка (первый список, первая точка).
    /// Для LineString: Coordinates[0] – список точек линии.
    /// Для Polygon:
    ///   Coordinates[0] – внешнее кольцо,
    ///   Coordinates[1..] – внутренние кольца (дыры), если есть.
    /// </summary>
    public List<List<Vector2>> CoordinatesLonLat { get; } = new();
}

public class MapFeature
{
    public string Id;
    public string Name;
    public MapGeometry Geometry;
    public Dictionary<string, string> Properties { get; } = new();
}

public class MapLayer : MonoBehaviour
{
    public string Id;
    public string DisplayName;

    public Color Color;

    public float LineWidth;
    public float LineSimplifyTolerance;

    public string IdProperty;
    public string NameProperty;

    [SerializeField] private float _fillMaterialTransparency = 0.2f;


    public List<Area> Areas = new();

    public List<MapFeature> Features { get; } = new();

    public bool ForceHighlight;

    [NonSerialized] public Material LineMaterial, FillMaterial;

    protected bool _init;

    public virtual void Init()
    {
        if (_init)
            return;
        
        LineMaterial = new Material(MapLayerRenderer.Instance.lineMaterialTemplate);
        LineMaterial.SetColor("_BaseColor", Color);

        FillMaterial = new Material(MapLayerRenderer.Instance.fillMaterialTemplate);
        FillMaterial.SetColor("_BaseColor", new Color(Color.r, Color.g, Color.b, _fillMaterialTransparency));

        foreach (var area in Areas) // if there are cached areas
        {
            var feature = new MapFeature
            {
                Id = Id,
                Name = DisplayName,
                Geometry = new MapGeometry { Type = GeometryType.Polygon }
            };
            Features.Add(feature);

            area.Setup(this, feature);
        }
        
        _init = true;
    }

    public virtual void SetVisible(bool visible)
    {
        if (visible)
            Init();

        gameObject.SetActive(visible);
    }

    public void SetForceHighlight(bool forceHighlight)
    {
        ForceHighlight = forceHighlight;

        foreach (var area in Areas)
            area.Highlight(ForceHighlight);

        // MapLayerRenderer.Instance.SetLayerVisible(Id, visible);
    }
}