public class ImageMapLayer : MapLayer
{
    [SerializeField] private MeshRenderer _meshRenderer;
    
    public override void Init()
    {
        if (_init)
            return;
        
        _init = true;
    }
}