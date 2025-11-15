public class CameraController : MonoBehaviour
{
    [Header("Targets & Layers")] [Tooltip("Camera that will be controlled. If null, this component's transform is used.")] [SerializeField]
    private Camera _cam;

    [Tooltip("Layer mask used to raycast against the terrain/map mesh.")] [SerializeField]
    private LayerMask _mapLayerMask = ~0;

    [Tooltip("Height above the terrain from which focus raycasts start.")] [SerializeField]
    private float raycastHeight = 1000f;

    [Header("Speeds")] [SerializeField] private float zoomSpeed = 10f;
    [SerializeField] private float orbitSpeed = 500f;

    [Header("Zoom Factor Curve")] [Tooltip("Minimum multiplier applied to zoom speed when the camera is close to the map.")] [SerializeField]
    private float zoomFactorMin = 0.1f;

    [Tooltip("Maximum multiplier applied to zoom speed when the camera is far from the map.")] [SerializeField]
    private float zoomFactorMax = 10f;

    [Tooltip("Curve exponent: >1 makes zoom grow faster with distance (more non-linear).")] [SerializeField]
    private float zoomFactorPower = 2f;

    [Header("Distance & Angles")] [SerializeField]
    private float minDistance = 5f;

    [SerializeField] private float maxDistance = 200f;
    [SerializeField] private float minPitch = 10f;
    [SerializeField] private float maxPitch = 85f;

    [Header("Hover")] [SerializeField] private LayerMask _areaLayerMask;

    // Point on the terrain the camera is looking at
    private Vector3 focusPoint;

    // Distance from camera to focus point
    private float distance;

    // Orbit angles around the focus point
    private float yaw; // around Y
    private float pitch; // around X

    // Panning state
    private bool isPanning;
    private Vector3 panStartPoint;
    private Plane panPlane;

    private Area _hoveredArea;

    private Ray _mousePositionRay;

    // Cached camera reference
    private Camera cachedCamera;

    private void Start()
    {
        InitializeFocusFromCurrentCamera();
    }

    private void Update()
    {
        _mousePositionRay = _cam.ScreenPointToRay(Input.mousePosition);

        HandleInput();
        UpdateCameraTransform();
        HandleHover();
    }

    private void HandleInput()
    {
        var mouseDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

        if (Input.GetMouseButtonDown(0)) // Left mouse: pan (grab the map and drag it)
            BeginPan();
        if (Input.GetMouseButton(0))
            UpdatePan();
        if (Input.GetMouseButtonUp(0))
            isPanning = false;

        if (Input.GetMouseButton(1)) // Right mouse: orbit around focus point
            Orbit(mouseDelta);

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > Mathf.Epsilon)
        {
            float factor = GetZoomFactor();
            distance = Mathf.Clamp(distance - scroll * zoomSpeed * factor * 10f, minDistance, maxDistance);
        }
    }

    private void HandleHover()
    {
        if (!Physics.Raycast(_mousePositionRay, out var hitInfo, Mathf.Infinity, _areaLayerMask) ||
            !hitInfo.collider.TryGetComponent(out Area area)) // No hit or hit but not to area
        {
            HoveredAreaHide();
            HoveredAreaClear();
            return;
        }

        if (area != null && area != _hoveredArea) // Hit to different area
        {
            HoveredAreaHide();
            HoveredAreaShow(area);
        }

        void HoveredAreaShow(Area area)
        {
            _hoveredArea = area;
            _hoveredArea.MeshVisibility(true);
        }

        void HoveredAreaHide() => _hoveredArea?.MeshVisibility(false);

        void HoveredAreaClear() => _hoveredArea = null;
    }

    private float GetZoomFactor()
    {
        if (maxDistance <= minDistance)
            return 1f;

        // Normalized distance 0..1 (0 = near, 1 = far)
        float t = Mathf.InverseLerp(minDistance, maxDistance, distance);
        // Apply a power curve to make it more exponential-like
        t = Mathf.Pow(t, zoomFactorPower);
        // Remap to [zoomFactorMin, zoomFactorMax]
        return Mathf.Lerp(zoomFactorMin, zoomFactorMax, t);
    }

    private void Zoom(Vector2 mouseDelta)
    {
        // Use vertical mouse movement for zoom, scaled non-linearly by current distance
        float factor = GetZoomFactor();
        float zoomDelta = -mouseDelta.y * zoomSpeed * factor * Time.deltaTime;
        distance = Mathf.Clamp(distance + zoomDelta, minDistance, maxDistance);
    }

    private void BeginPan()
    {
        isPanning = false;

        // Define a plane through the current focus point, facing the camera.
        // This approximates the local ground under the cursor and makes panning scale-independent.
        panPlane = new Plane(-_cam.transform.forward, focusPoint);

        if (panPlane.Raycast(_mousePositionRay, out var enter))
        {
            panStartPoint = _mousePositionRay.GetPoint(enter);
            isPanning = true;
        }
    }

    private void UpdatePan()
    {
        if (!isPanning)
            return;

        if (!panPlane.Raycast(_mousePositionRay, out var enter))
            return;

        Vector3 currentPoint = _mousePositionRay.GetPoint(enter);

        // We want the plane point under the cursor to remain visually fixed.
        // So we move the focus by the difference between where the cursor started and where it is now.
        Vector3 delta = panStartPoint - currentPoint;

        focusPoint += delta;
        focusPoint = SnapToTerrain(focusPoint);
    }

    private void Orbit(Vector2 mouseDelta)
    {
        yaw += mouseDelta.x * orbitSpeed * Time.deltaTime;
        pitch -= mouseDelta.y * orbitSpeed * Time.deltaTime;

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void UpdateCameraTransform()
    {
        // Rebuild camera position from spherical coordinates around the focus point
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0.0f);
        Vector3 offset = rotation * new Vector3(0.0f, 0.0f, -distance);

        _cam.transform.position = focusPoint + offset;
        _cam.transform.LookAt(focusPoint);
    }

    private void InitializeFocusFromCurrentCamera()
    {
        // Try to find focus point by casting a ray from the camera forward
        Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);
        if (Physics.Raycast(ray, out var hit, raycastHeight * 3.0f, _mapLayerMask))
        {
            focusPoint = hit.point;
        }
        else
        {
            // Fallback: cast down from camera position
            Vector3 origin = _cam.transform.position + Vector3.up * raycastHeight;
            if (Physics.Raycast(origin, Vector3.down, out hit, raycastHeight * 2.0f, _mapLayerMask))
            {
                focusPoint = hit.point;
            }
            else
            {
                // Last resort: use camera position projected onto ground plane (Y = 0)
                focusPoint = _cam.transform.position;
                focusPoint.y = 0.0f;
            }
        }

        focusPoint = SnapToTerrain(focusPoint);

        distance = Vector3.Distance(_cam.transform.position, focusPoint);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        // Extract initial yaw/pitch from current rotation
        Vector3 toFocus = (focusPoint - _cam.transform.position).normalized;
        if (toFocus.sqrMagnitude > 0.0f)
        {
            Quaternion look = Quaternion.LookRotation(toFocus, Vector3.up);
            Vector3 euler = look.eulerAngles;
            yaw = euler.y;
            pitch = euler.x;
        }
    }

    private Vector3 SnapToTerrain(Vector3 worldPos)
    {
        Vector3 origin = worldPos + Vector3.up * raycastHeight;
        if (Physics.Raycast(origin, Vector3.down, out var hit, raycastHeight * 2.0f, _mapLayerMask))
            return hit.point;

        return worldPos;
    }
}