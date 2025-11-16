public class MainScreen : MonoBehaviour
{
    [SerializeField] private Toggle _layerTogglePrefab;
    [SerializeField] private Button _camProjectionButton;
    [SerializeField] private InputField _addLayerInputField;
    [SerializeField] private GameObject _creatingAreaTooltip;

    // private readonly List<Toggle> _toggles = new();

    private void Start()
    {
        _layerTogglePrefab.gameObject.SetActive(false);
        _creatingAreaTooltip.gameObject.SetActive(false);

        foreach (var layer in MapLayerRenderer.Instance.GetComponentsInChildren<LayerSource>())
            CreateLayerToggle(layer);
        
        CamProjectionButtonTextUpdate();
        _camProjectionButton.onClick.AddListener(() =>
        {
            CameraController.Instance.ToggleProjection();
            CamProjectionButtonTextUpdate();
        });

        void CamProjectionButtonTextUpdate() =>
            _camProjectionButton.GetComponentInChildren<Text>()?.SetText(CameraController.Instance.Cam.orthographic ? "Ortho" : "Pers");

        _addLayerInputField.onEndEdit.AddListener(text =>
        {
            if (string.IsNullOrEmpty(text))
                return;

            var go = new GameObject(text);
            go.transform.SetParent(MapLayerRenderer.Instance.transform);

            var source = go.AddComponent<AreaLayerSource>();
            source.Id = source.DisplayName = text;
            source.Color = Color.white;

            CreateLayerToggle(source, true);

            var layer = new MapLayer(source);

            var feature = new MapFeature()
            {
                Id = source.Id,
                Name = source.DisplayName,
                Geometry = new MapGeometry
                {
                    Type = GeometryType.Polygon
                }
            };
            layer.Features.Add(feature);

            var area = Instantiate(MapLayerRenderer.Instance.AreaPrefab, source.transform).Setup(layer, feature);
            source.Areas.Add(area);

            _addLayerInputField.SetTextWithoutNotify(string.Empty);
            _addLayerInputField.gameObject.SetActive(false);
            _creatingAreaTooltip.SetActive(true);

            CameraController.Instance.StartAreaDraw(area, () =>
            {
                _addLayerInputField.gameObject.SetActive(true);
                _creatingAreaTooltip.SetActive(false);
            });
        });
    }

    private void CreateLayerToggle(LayerSource layer, bool toggled = false)
    {
        var toggle = Instantiate(_layerTogglePrefab, _layerTogglePrefab.transform.parent);
        toggle.name = $"LayerToggle {layer.Id}";
        toggle.gameObject.SetActive(true);

        toggle.GetComponentInChildren<Text>()?.SetText(layer.DisplayName);

        toggle.isOn = toggled;

        var l = layer;
        toggle.onValueChanged.AddListener(isOn => l.SetVisible(isOn));

        _addLayerInputField.transform.SetAsLastSibling();

        // _toggles.Add(toggle);
    }
}