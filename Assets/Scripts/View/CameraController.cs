public class CameraController : MonoBehaviour
{
    [Header("Targets & Layers")]
    [Tooltip("Camera that will be controlled. If null, this component's transform is used.")]
    [SerializeField] private Transform cameraTransform;

    [Tooltip("Layer mask used to raycast against the terrain/map mesh.")]
    [SerializeField] private LayerMask terrainLayerMask = ~0;

    [Tooltip("Height above the terrain from which focus raycasts start.")]
    [SerializeField] private float raycastHeight = 1000f;

    [Header("Speeds")]
    [SerializeField] private float zoomSpeed = 10f;
    [SerializeField] private float orbitSpeed = 500f;

    [Header("Zoom Factor Curve")]
    [Tooltip("Minimum multiplier applied to zoom speed when the camera is close to the map.")]
    [SerializeField] private float zoomFactorMin = 0.1f;
    [Tooltip("Maximum multiplier applied to zoom speed when the camera is far from the map.")]
    [SerializeField] private float zoomFactorMax = 10f;
    [Tooltip("Curve exponent: >1 makes zoom grow faster with distance (more non-linear).")]
    [SerializeField] private float zoomFactorPower = 2f;

    [Header("Distance & Angles")]
    [SerializeField] private float minDistance = 5f;
    [SerializeField] private float maxDistance = 200f;
    [SerializeField] private float minPitch = 10f;
    [SerializeField] private float maxPitch = 85f;

    // Point on the terrain the camera is looking at
    private Vector3 focusPoint;
    // Distance from camera to focus point
    private float distance;
    // Orbit angles around the focus point
    private float yaw;   // around Y
    private float pitch; // around X

    // Panning state
    private bool isPanning;
    private Vector3 panStartPoint;
    private Plane panPlane;

    // Cached camera reference
    private Camera cachedCamera;

    private Camera Cam
    {
        get
        {
            if (cachedCamera == null)
            {
                if (cameraTransform != null)
                    cachedCamera = cameraTransform.GetComponent<Camera>();

                if (cachedCamera == null)
                    cachedCamera = Camera.main;
            }

            return cachedCamera;
        }
    }

    private void Start()
    {
        if (cameraTransform == null)
            cameraTransform = transform;

        InitializeFocusFromCurrentCamera();
    }

    private void Update()
    {
        HandleInput();
        UpdateCameraTransform();
    }

    private void HandleInput()
    {
        var mouseDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

        // Left mouse: pan (grab the map and drag it)
        if (Input.GetMouseButtonDown(0))
        {
            BeginPan();
        }

        if (Input.GetMouseButton(0))
        {
            UpdatePan();
        }

        if (Input.GetMouseButtonUp(0))
        {
            isPanning = false;
        }

        // Right mouse: zoom in/out
        if (Input.GetMouseButton(1))
        {
            Zoom(mouseDelta);
        }

        // Middle mouse (wheel button): orbit around focus point
        if (Input.GetMouseButton(2))
        {
            Orbit(mouseDelta);
        }

        // Optional: scroll wheel zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > Mathf.Epsilon)
        {
            float factor = GetZoomFactor();
            distance = Mathf.Clamp(distance - scroll * zoomSpeed * factor * 10f, minDistance, maxDistance);
        }
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

        if (Cam == null)
            return;

        // Define a plane through the current focus point, facing the camera.
        // This approximates the local ground under the cursor and makes panning scale-independent.
        panPlane = new Plane(-cameraTransform.forward, focusPoint);

        Ray ray = Cam.ScreenPointToRay(Input.mousePosition);
        if (panPlane.Raycast(ray, out var enter))
        {
            panStartPoint = ray.GetPoint(enter);
            isPanning = true;
        }
    }

    private void UpdatePan()
    {
        if (!isPanning)
            return;

        if (Cam == null)
            return;

        Ray ray = Cam.ScreenPointToRay(Input.mousePosition);
        if (!panPlane.Raycast(ray, out var enter))
            return;

        Vector3 currentPoint = ray.GetPoint(enter);

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
        if (cameraTransform == null)
            return;

        // Rebuild camera position from spherical coordinates around the focus point
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0.0f);
        Vector3 offset = rotation * new Vector3(0.0f, 0.0f, -distance);

        cameraTransform.position = focusPoint + offset;
        cameraTransform.LookAt(focusPoint);
    }

    private void InitializeFocusFromCurrentCamera()
    {
        // Try to find focus point by casting a ray from the camera forward
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        if (Physics.Raycast(ray, out var hit, raycastHeight * 3.0f, terrainLayerMask))
        {
            focusPoint = hit.point;
        }
        else
        {
            // Fallback: cast down from camera position
            Vector3 origin = cameraTransform.position + Vector3.up * raycastHeight;
            if (Physics.Raycast(origin, Vector3.down, out hit, raycastHeight * 2.0f, terrainLayerMask))
            {
                focusPoint = hit.point;
            }
            else
            {
                // Last resort: use camera position projected onto ground plane (Y = 0)
                focusPoint = cameraTransform.position;
                focusPoint.y = 0.0f;
            }
        }

        focusPoint = SnapToTerrain(focusPoint);

        distance = Vector3.Distance(cameraTransform.position, focusPoint);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        // Extract initial yaw/pitch from current rotation
        Vector3 toFocus = (focusPoint - cameraTransform.position).normalized;
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
        if (Physics.Raycast(origin, Vector3.down, out var hit, raycastHeight * 2.0f, terrainLayerMask))
        {
            return hit.point;
        }

        return worldPos;
    }
}