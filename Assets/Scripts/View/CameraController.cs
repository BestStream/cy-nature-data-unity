public class CameraController : MonoBehaviour
{
    public static CameraController Instance;

    [Header("Targets & Layers")] [Tooltip("Camera that will be controlled. If null, this component's transform is used.")] [SerializeField]
    public Camera Cam;

    [Header("Projection")] [SerializeField]
    private float orthographicMinSize = 10f;

    [SerializeField] private float orthographicMaxSize = 200f;

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

    private void Awake()
    {
        Instance = this;

        InitializeFocusFromCurrentCamera();
        SetProjection(false);
    }

    private void Update()
    {
        _mousePositionRay = Cam.ScreenPointToRay(Input.mousePosition);

        HandleInput();
        UpdateCameraTransform();

        if (_drawArea == null)
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

        // In orthographic mode we disable camera rotation/orbiting
        if (!Cam.orthographic && Input.GetMouseButton(1)) // Right mouse: orbit around focus point
            Orbit(mouseDelta);

        Zoom(Input.GetAxis("Mouse ScrollWheel"));

        if (_drawArea != null)
        {
            if (Input.GetMouseButtonDown(2)) // Add new point to area
            {
                if (Physics.Raycast(_mousePositionRay, out var hit, Mathf.Infinity, _mapLayerMask))
                    _drawArea.AddPoint(hit.point);
            }
            else if (Input.GetKeyUp(KeyCode.Escape)) // Finished building area
            {
                _drawArea = null;
            }
        }
    }

    private void Zoom(float scroll)
    {
        if (Mathf.Abs(scroll) > Mathf.Epsilon)
        {
            if (Cam.orthographic)
            {
                float size = Cam.orthographicSize;
                size -= scroll * zoomSpeed * 10f;
                size = Mathf.Clamp(size, orthographicMinSize, orthographicMaxSize);
                Cam.orthographicSize = size;
            }
            else
            {
                float factor = GetZoomFactor();
                distance = Mathf.Clamp(distance - scroll * zoomSpeed * factor * 10f, minDistance, maxDistance);
            }
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
    }

    private float GetZoomFactor()
    {
        // For orthographic mode we don't scale zoom by distance.
        if (Cam.orthographic || maxDistance <= minDistance)
            return 1f;

        // Normalized distance 0..1 (0 = near, 1 = far)
        float t = Mathf.InverseLerp(minDistance, maxDistance, distance);
        // Apply a power curve to make it more exponential-like
        t = Mathf.Pow(t, zoomFactorPower);
        // Remap to [zoomFactorMin, zoomFactorMax]
        return Mathf.Lerp(zoomFactorMin, zoomFactorMax, t);
    }

    public void ToggleProjection() => SetProjection(!Cam.orthographic);

    private void SetProjection(bool orthographic)
    {
        // Был ли режим ортографическим до переключения
        bool wasOrthographic = Cam.orthographic;

        Cam.orthographic = orthographic;

        // Половина вертикального FOV в радианах (для сопоставления масштаба)
        float halfFovRad = Cam.fieldOfView * 0.5f * Mathf.Deg2Rad;

        if (Cam.orthographic)
        {
            pitch = 90f;

            // Подгоняем ortho под текущую перспективу:
            // orthographicSize (половина высоты в юнитах) ≈ distance * tan(FOV / 2)
            float size = distance * Mathf.Tan(halfFovRad);
            Cam.orthographicSize = Mathf.Clamp(size, orthographicMinSize, orthographicMaxSize);
        }
        else
        {
            pitch = 50f;

            if (wasOrthographic)
            {
                // Возвращаемся из орто: подгоняем distance под текущий orthographicSize:
                // distance ≈ orthographicSize / tan(FOV / 2)
                float d = Cam.orthographicSize / Mathf.Tan(halfFovRad);
                distance = Mathf.Clamp(d, minDistance, maxDistance);
            }
            else
            {
                // Исходная инициализация (как у тебя было)
                distance = Vector3.Distance(Cam.transform.position, focusPoint);
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }
        }
    }

    private void BeginPan()
    {
        isPanning = false;

        // Define a plane through the current focus point, facing the camera.
        // This approximates the local ground under the cursor and makes panning scale-independent.
        panPlane = new Plane(-Cam.transform.forward, focusPoint);

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

        Cam.transform.position = focusPoint + offset;
        Cam.transform.LookAt(focusPoint);
    }

    private void InitializeFocusFromCurrentCamera()
    {
        // Try to find focus point by casting a ray from the camera forward
        Ray ray = new Ray(Cam.transform.position, Cam.transform.forward);
        if (Physics.Raycast(ray, out var hit, raycastHeight * 3.0f, _mapLayerMask))
        {
            focusPoint = hit.point;
        }
        else
        {
            // Fallback: cast down from camera position
            Vector3 origin = Cam.transform.position + Vector3.up * raycastHeight;
            if (Physics.Raycast(origin, Vector3.down, out hit, raycastHeight * 2.0f, _mapLayerMask))
            {
                focusPoint = hit.point;
            }
            else
            {
                // Last resort: use camera position projected onto ground plane (Y = 0)
                focusPoint = Cam.transform.position;
                focusPoint.y = 0.0f;
            }
        }

        focusPoint = SnapToTerrain(focusPoint);

        distance = Vector3.Distance(Cam.transform.position, focusPoint);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        // Extract initial yaw/pitch from current rotation
        Vector3 toFocus = (focusPoint - Cam.transform.position).normalized;
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

    private  void HoveredAreaShow(Area area)
    {
        _hoveredArea = area;
        _hoveredArea.Highlight(true);
    }

    private void HoveredAreaHide() => _hoveredArea?.Highlight(false);

    private void HoveredAreaClear() => _hoveredArea = null;
    
    private Area _drawArea;
    public void StartAreaDraw(Area area, Action callback = null)
    {
        HoveredAreaHide();
        HoveredAreaClear();
        
        _drawArea = area;
        _drawArea.Highlight(true);

        StartCoroutine(WaitForAreaCompleted());
        IEnumerator WaitForAreaCompleted()
        {
            while (_drawArea != null)
                yield return null;
            callback?.Invoke();
        }
    }
}