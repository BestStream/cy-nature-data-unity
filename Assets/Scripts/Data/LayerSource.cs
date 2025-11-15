public abstract class LayerSource : MonoBehaviour
{
    public string Id;
    public string DisplayName;

    public Color Color;

    public float LineWidth;
    public float LineSimplifyTolerance;

    public string IdProperty;
    public string NameProperty;


    protected bool _init;

    public virtual void Init()
    {
        _init = true;
    }

    public virtual void SetVisible(bool visible)
    {
        if (visible)
            Init();

        MapLayerRenderer.Instance.SetLayerVisible(Id, visible);
    }
}