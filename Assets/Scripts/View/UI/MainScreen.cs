public class MainScreen : MonoBehaviour
{
    [SerializeField] private Toggle _layerTogglePrefab;
    [SerializeField] private Button _camProjectionButton;

    // private readonly List<Toggle> _toggles = new();

    private void Awake()
    {
        _layerTogglePrefab.gameObject.SetActive(false);
    }

    private void Start()
    {
        CreateLayerToggles();

        CamProjectionButtonTextUpdate();
        _camProjectionButton.onClick.AddListener(() =>
        {
            CameraController.Instance.ToggleProjection();
            CamProjectionButtonTextUpdate();
        });
        
        void CamProjectionButtonTextUpdate() => 
            _camProjectionButton.GetComponent<Text>()?.SetText(CameraController.Instance.Cam.orthographic ? "Ortho" : "Pers");
    }

    private void CreateLayerToggles()
    {
        foreach (var layer in MapLayerRenderer.Instance.GetComponentsInChildren<LayerSource>())
        {
            var toggle = Instantiate(_layerTogglePrefab, _layerTogglePrefab.transform.parent);
            toggle.name = $"LayerToggle {layer.Id}";
            toggle.gameObject.SetActive(true);

            toggle.GetComponentInChildren<Text>()?.SetText(layer.DisplayName);

            toggle.isOn = false;

            var l = layer;
            toggle.onValueChanged.AddListener(isOn => l.SetVisible(isOn));

            // _toggles.Add(toggle);
        }
    }
}