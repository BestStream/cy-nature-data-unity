public class MainScreen : MonoBehaviour
{
    [SerializeField] private LayerToggle _layerTogglePrefab;
    [SerializeField] private Button _camProjectionButton;
    [SerializeField] private InputField _addLayerInputField;
    [SerializeField] private GameObject _creatingAreaTooltip;

    // private readonly List<LayerToggle> _toggles = new();

    private void Start()
    {
        _layerTogglePrefab.gameObject.SetActive(false);
        _creatingAreaTooltip.gameObject.SetActive(false);

        foreach (var layer in MapLayerRenderer.Instance.GetComponentsInChildren<MapLayer>(includeInactive: true))
            CreateLayerToggle(layer);

        CamProjectionButtonTextUpdate();
        _camProjectionButton.onClick.AddListener(() =>
        {
            CameraController.Instance.ToggleProjection();
            CamProjectionButtonTextUpdate();
        });

        void CamProjectionButtonTextUpdate() =>
            _camProjectionButton.GetComponentInChildren<Text>()?.SetText(CameraController.Instance.Cam.orthographic ? "Ortho" : "Persp");

        _addLayerInputField.onEndEdit.AddListener(text =>
        {
            if (string.IsNullOrEmpty(text))
                return;

            var go = new GameObject(text);
            go.transform.SetParent(MapLayerRenderer.Instance.transform);

            var layer = go.AddComponent<MapLayer>();
            layer.Id = layer.DisplayName = text;
            layer.Color = Color.white;
            layer.LineWidth = 4;
            
            layer.Init();

            CreateLayerToggle(layer, true);

            var feature = new MapFeature()
            {
                Id = layer.Id,
                Name = layer.DisplayName,
                Geometry = new MapGeometry { Type = GeometryType.Polygon }
            };
            layer.Features.Add(feature);

            var area = Instantiate(MapLayerRenderer.Instance.AreaPrefab, layer.transform).Setup(layer, feature);
            layer.Areas.Add(area);

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

    private void CreateLayerToggle(MapLayer mapLayer, bool toggled = false)
    {
        var layerToggle = Instantiate(_layerTogglePrefab, _layerTogglePrefab.transform.parent).Setup(mapLayer, toggled);
        // _toggles.Add(layerToggle);

        _addLayerInputField.transform.SetAsLastSibling();
    }
}