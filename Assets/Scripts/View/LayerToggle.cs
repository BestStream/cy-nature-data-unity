public class LayerToggle : MonoBehaviour
{
    public Toggle Toggle;
    [SerializeField] private Text _toggleText;
    [SerializeField] private Button _forceHighlightButton;
    [SerializeField] private Image _forceHighlightButtonImage;
    [SerializeField] private Sprite[] _forceHighlightButtonImageSprites;

    public LayerToggle Setup(MapLayer layer, bool toggled = false)
    {
        name = $"LayerToggle {layer.Id}";
        gameObject.SetActive(true);

        _toggleText.text = layer.DisplayName;

        Toggle.isOn = toggled;

        Toggle.onValueChanged.AddListener(layer.SetVisible);

        ForceHighlightButtonUpdate();
        _forceHighlightButton.onClick.AddListener(() =>
        {
            layer.SetForceHighlight(!layer.ForceHighlight);
            ForceHighlightButtonUpdate();
        });

        void ForceHighlightButtonUpdate()
        {
            _forceHighlightButtonImage.color = new Color(0.2f, 0.2f, 0.2f, layer.ForceHighlight ? 1f : 0.25f);
            _forceHighlightButtonImage.sprite = _forceHighlightButtonImageSprites[layer.ForceHighlight ? 1 : 0];
        }

        return this;
    }
}