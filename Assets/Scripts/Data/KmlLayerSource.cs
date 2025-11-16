using System.Globalization;
using System.Xml;

public class KmlMapLayer : MapLayer
{
    [SerializeField] private TextAsset _dataSource;

    public override void Init()
    {
        if (_init)
            return;

        if (_dataSource == null)
        {
            Debug.LogError("Kml is not assigned");
            return;
        }
        
        base.Init();

        Load();
        
        MapLayerRenderer.Instance.RenderLayer(this);
    }

    private void Load()
    {
        var xml = new XmlDocument();
        try
        {
            xml.LoadXml(_dataSource.text);
        }
        catch (Exception e)
        {
            Debug.LogError($"KmlLayerSource: Failed to parse KML for layer '{Id}': {e.Message}");
            return;
        }

        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("kml", "http://www.opengis.net/kml/2.2");

        var placemarks = xml.SelectNodes("//kml:Placemark", ns);
        if (placemarks == null)
            return;

        foreach (XmlNode placemark in placemarks)
        {
            var feature = ParsePlacemark(placemark, ns);
            if (feature != null)
                Features.Add(feature);
        }

        Debug.Log($"KmlLayerSource: Loaded layer '{Id}' ({DisplayName}), features: {Features.Count}");
    }

    private MapFeature ParsePlacemark(XmlNode placemark, XmlNamespaceManager ns)
    {
        var feature = new MapFeature();

        // Имя из стандартного тега <name> (fallback, если не задано через _nameProperty)
        var nameNode = placemark.SelectSingleNode("kml:name", ns);

        // ExtendedData / SimpleData
        var simpleDataNodes = placemark.SelectNodes(".//kml:ExtendedData//kml:SimpleData", ns);
        if (simpleDataNodes != null)
        {
            foreach (XmlNode sd in simpleDataNodes)
            {
                var nameAttr = sd.Attributes?["name"]?.Value;
                if (string.IsNullOrEmpty(nameAttr))
                    continue;

                var value = sd.InnerText?.Trim() ?? string.Empty;
                feature.Properties[nameAttr] = value;

                // ID берём из свойства, имя которого задано в _idProperty
                if (!string.IsNullOrEmpty(IdProperty) &&
                    nameAttr == IdProperty &&
                    string.IsNullOrEmpty(feature.Id))
                {
                    feature.Id = value;
                }

                // Name берём из свойства, имя которого задано в _nameProperty
                if (!string.IsNullOrEmpty(NameProperty) &&
                    nameAttr == NameProperty)
                {
                    feature.Name = value;
                }
            }
        }

        // Если имя ещё не задано через свойства, используем <name>
        if (string.IsNullOrEmpty(feature.Name) && nameNode != null)
            feature.Name = nameNode.InnerText.Trim();

        // Геометрия – в твоём примере Polygon внутри MultiGeometry
        // Берём внешний контур
        var coordNode = placemark.SelectSingleNode(
            ".//kml:Polygon/kml:outerBoundaryIs/kml:LinearRing/kml:coordinates", ns);

        if (coordNode == null)
        {
            // Нет полигона – пока пропускаем (можно потом добавить поддержку LineString/Point)
            return null;
        }

        var geom = new MapGeometry { Type = GeometryType.Polygon };

        var outerRing = ParseCoordinatesToRing(coordNode.InnerText);
        if (outerRing.Count == 0)
            return null;

        geom.CoordinatesLonLat.Add(outerRing);
        feature.Geometry = geom;

        // Если имени нет – подставим naturacode или id
        if (string.IsNullOrEmpty(feature.Name))
            feature.Name = !string.IsNullOrEmpty(feature.Id) ? feature.Id : "Placemark";

        return feature;
    }

    private List<Vector2> ParseCoordinatesToRing(string coordsText)
    {
        var ring = new List<Vector2>();
        if (string.IsNullOrWhiteSpace(coordsText))
            return ring;

        // Координаты вида: "lon,lat lon,lat lon,lat"
        var tokens = coordsText.Split(
            new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var parts = token.Split(',');
            if (parts.Length < 2)
                continue;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                continue;

            ring.Add(new Vector2((float)lon, (float)lat));
        }

        return ring;
    }
}