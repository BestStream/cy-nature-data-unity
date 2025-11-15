public class ImageLayerSource : LayerSource
{
    [SerializeField] private MeshRenderer _meshRenderer;
    
    public override void SetVisible(bool visible)
    {
        _meshRenderer.enabled = visible;
    }
}