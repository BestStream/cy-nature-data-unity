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
    public abstract void Init();
}