public class AreaLayerSource : LayerSource
{
    public List<Area> Areas = new();

    public override void Init()
    {
        if (_init)
            return;

        var layer = new MapLayer(this);

        foreach (var area in Areas)
        {
            var feature = new MapFeature()
            {
                Id = Id,
                Name = DisplayName,
                Geometry = new MapGeometry
                {
                    Type = GeometryType.Polygon
                }
            };
            layer.Features.Add(feature);

            area.Setup(layer, feature);
        }
        
        _init = true;
    }

    public override void SetVisible(bool visible)
    {
        if (visible)
            Init();
        
        foreach (var area in Areas)
            area.gameObject.SetActive(visible);
    }
}