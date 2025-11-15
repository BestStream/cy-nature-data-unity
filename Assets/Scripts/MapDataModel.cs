public enum GeometryType { Point, LineString, Polygon }

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

public class MapLayer
{
    public string Id;
    public string DisplayName;
    public Color Color;
    public float LineWidth;
    public float LineSimplifyTolerance;
    public List<MapFeature> Features { get; } = new();

    public bool Visible = true;
    
    public Material LineMaterial, FillMaterial;
}