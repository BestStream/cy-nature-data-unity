public class AreaLayerSource : LayerSource
{
    public readonly List<Area> Areas = new ();
    
    public override void SetVisible(bool visible)
    {
        foreach (var area in Areas)
            area.gameObject.SetActive(visible);
    }
}