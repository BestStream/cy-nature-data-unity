public class MainScreen : MonoBehaviour
{
    [SerializeField] private Toggle togglePrefab;

    [SerializeField] private List<LayerSource> layers = new();

    // private readonly List<Toggle> _toggles = new();

    private void Awake()
    {
        togglePrefab.gameObject.SetActive(false);
    }

    private void Start()
    {
        CreateToggles();
    }

    private void CreateToggles()
    {
        foreach (var layer in layers)
        {
            if(!layer.gameObject.activeSelf)
                continue;
            
            var toggle = Instantiate(togglePrefab, togglePrefab.transform.parent);
            toggle.name = $"LayerToggle {layer.Id}";
            toggle.gameObject.SetActive(true);

            toggle.GetComponentInChildren<Text>()?.SetText(layer.DisplayName);

            toggle.isOn = false;
            MapLayerRenderer.Instance.SetLayerVisible(layer.Id, toggle.isOn);

            var layerId = layer.Id; // захват в замыкание
            toggle.onValueChanged.AddListener(isOn =>
            {
                MapLayerRenderer.Instance.SetLayerVisible(layerId, isOn);
            });

            // _toggles.Add(toggle);
        }
    }
}