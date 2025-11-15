using UnityEngine.Networking;
using System.IO;
using Newtonsoft.Json.Linq;

public class GeoJsonDaraManager : MonoBehaviour
{
    [SerializeField] private string datasetName;

    [Header("Service Settings")] [SerializeField]
    private string baseService;

    [SerializeField] private int layerId;

    [Header("Paging")] [Tooltip("Max records per request (ArcGIS REST, default = MaxRecordCount 1000).")] [SerializeField]
    private int pageSize = 1000;

    [Header("Cache Settings")] [Tooltip("Relative cache folder under Assets/ (e.g. \"Cache/Cadastre\" or \"Cache/PlanningZones\").")] [SerializeField]
    private string cacheFolderName = "Cache/PlanningZones";

    // Events for communication with renderer or other systems
    public event Action<string> OnChunkLoaded;
    public event Action<int, int> OnDownloadProgress; // page, totalFeatures
    public event Action OnDownloadComplete;
    public event Action OnAllDataLoaded;

    private string CacheDir
    {
        get
        {
            string cacheDir = Path.Combine(Application.dataPath, cacheFolderName);
            if (!Directory.Exists(cacheDir))
                Directory.CreateDirectory(cacheDir);
            return cacheDir;
        }
    }

    private string GetChunkPath(int pageNumber) => Path.Combine(CacheDir, $"chunk_{pageNumber:D5}.geojson");

    private string ProgressFilePath => Path.Combine(CacheDir, "download_progress.json");

    [ContextMenu("ForceRefresh")]
    public void ForceRefresh() => StartCoroutine(DownloadAndCache());

    [ContextMenu("Refresh")]
    public void Refresh() => StartCoroutine(LoadOrDownload());

    private IEnumerator LoadOrDownload()
    {
        if (!IsDownloadComplete())
            yield return DownloadAndCache();

        Debug.Log("═══════════════════════════════════════════════════════════");
        Debug.Log($"{datasetName}: Loading from cached chunks...");
        yield return LoadFromChunks();
        Debug.Log($"{datasetName}: ✓ All data loaded from cache successfully!");
        Debug.Log("═══════════════════════════════════════════════════════════");
    }

    private bool IsDownloadComplete()
    {
        if (!File.Exists(ProgressFilePath))
            return false;

        try
        {
            var progressData = JObject.Parse(File.ReadAllText(ProgressFilePath));
            return (bool)(progressData["completed"] ?? false);
        }
        catch
        {
            return false;
        }
    }

    private string BuildUrl(int offset)
    {
        string service = baseService.TrimEnd('/') + "/" + layerId + "/query";

        var parts = new List<string>
        {
            "where=1%3D1",
            "outFields=*",
            "f=geojson",
            "outSR=4326", // получаем в WGS84, как и кадастр
            $"resultRecordCount={pageSize}",
            $"resultOffset={offset}"
        };

        return service + "?" + string.Join("&", parts);
    }

    private IEnumerator DownloadAndCache()
    {
        Debug.Log("═══════════════════════════════════════════════════════════");
        Debug.Log($"║ {datasetName}: STARTING DOWNLOAD ║");
        Debug.Log("═══════════════════════════════════════════════════════════");

        int startPage = GetLastDownloadedPage();
        if (startPage > 0)
        {
            Debug.Log($"║ ⚡ RESUMING from page {startPage + 1} (found {startPage} cached chunks)");
        }
        else
        {
            Debug.Log("║ 🚀 Starting fresh download...");
        }

        Debug.Log("═══════════════════════════════════════════════════════════");

        int page = startPage;
        int offset = startPage * pageSize;
        int totalFeatures = 0;
        DateTime downloadStartTime = DateTime.Now;

        while (true)
        {
            string url = BuildUrl(offset);
            DateTime pageStartTime = DateTime.Now;

            Debug.Log("┌─────────────────────────────────────────────────────────┐");
            Debug.Log($"│ 📥 DOWNLOADING PAGE {page + 1}");
            Debug.Log($"│ Offset: {offset} | Page size: {pageSize}");

            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 10000;

                yield return req.SendWebRequest();

                TimeSpan pageTime = DateTime.Now - pageStartTime;

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"│ ❌ ERROR on page {page + 1}: {req.error}");
                    Debug.Log("└─────────────────────────────────────────────────────────┘");
                    break;
                }

                var root = JObject.Parse(req.downloadHandler.text);
                var features = root["features"] as JArray;

                if (features == null || features.Count == 0)
                {
                    Debug.Log("│ ✓ No more data - download complete!");
                    Debug.Log("└─────────────────────────────────────────────────────────┘");
                    Debug.Log("═══════════════════════════════════════════════════════════");
                    Debug.Log($"║ {datasetName}: ✓ DOWNLOAD COMPLETE!");
                    Debug.Log($"║ Total pages: {page}");
                    Debug.Log($"║ Total features: {totalFeatures}");
                    TimeSpan finalTime = DateTime.Now - downloadStartTime;
                    Debug.Log($"║ Total time: {finalTime.TotalMinutes:F1} minutes");
                    Debug.Log("═══════════════════════════════════════════════════════════");
                    SaveProgress(page - 1, totalFeatures, true);
                    OnDownloadComplete?.Invoke();
                    break;
                }

                totalFeatures += features.Count;

                try
                {
                    var chunkData = new JObject
                    {
                        ["type"] = "FeatureCollection",
                        ["features"] = features
                    };

                    string chunkPath = GetChunkPath(page);
                    File.WriteAllText(chunkPath, chunkData.ToString());

                    Debug.Log($"│ ✓ Received {features.Count} features");
                    Debug.Log($"│ ✓ Saved to: chunk_{page:D5}.geojson");
                    Debug.Log($"│ ⏱  Page time: {pageTime.TotalSeconds:F2}s");
                    Debug.Log($"│ 📊 Total features so far: {totalFeatures}");

                    TimeSpan totalTime = DateTime.Now - downloadStartTime;
                    double avgTimePerPage =
                        totalTime.TotalSeconds / (page - startPage + 1);
                    Debug.Log($"│ ⚡ Average: {avgTimePerPage:F2}s/page");
                    Debug.Log($"│ ⏰ Total elapsed: {totalTime.TotalMinutes:F1} minutes");

                    OnDownloadProgress?.Invoke(page, totalFeatures);
                }
                catch (Exception e)
                {
                    Debug.LogError($"│ ⚠️  Failed to save chunk {page}: {e.Message}");
                }

                Debug.Log("└─────────────────────────────────────────────────────────┘");

                SaveProgress(page, totalFeatures, false);

                offset += pageSize;
                page++;

                yield return new WaitForSeconds(0.5f);
            }
        }

        if (totalFeatures > 0)
        {
            yield return LoadFromChunks();
        }
    }

    private int GetLastDownloadedPage()
    {
        if (!File.Exists(ProgressFilePath))
            return 0;

        try
        {
            var progressData = JObject.Parse(File.ReadAllText(ProgressFilePath));
            bool completed = (bool)(progressData["completed"] ?? false);
            if (completed)
                return 0;

            int lastPage = (int)(progressData["lastPage"] ?? -1);

            if (lastPage >= 0 && File.Exists(GetChunkPath(lastPage)))
                return lastPage + 1;
        }
        catch
        {
        }

        return 0;
    }

    private void SaveProgress(int page, int totalFeatures, bool completed)
    {
        try
        {
            var progress = new JObject
            {
                ["lastPage"] = page,
                ["totalFeatures"] = totalFeatures,
                ["completed"] = completed,
                ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            File.WriteAllText(ProgressFilePath, progress.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{datasetName}: Failed to save progress: {e.Message}");
        }
    }

    private IEnumerator LoadFromChunks()
    {
        Debug.Log("═══════════════════════════════════════════════════════════");
        Debug.Log($"║ LOADING CACHED DATASET: {datasetName}");
        Debug.Log("═══════════════════════════════════════════════════════════");

        int totalChunks = 0;
        while (File.Exists(GetChunkPath(totalChunks)))
            totalChunks++;

        Debug.Log($"║ Found {totalChunks} cached chunks");
        Debug.Log("═══════════════════════════════════════════════════════════");

        for (int i = 0; i < totalChunks; i++)
        {
            string chunkPath = GetChunkPath(i);
            string chunkJson = File.ReadAllText(chunkPath);

            OnChunkLoaded?.Invoke(chunkJson);

            if ((i + 1) % 10 == 0)
            {
                Debug.Log($"Loaded {i + 1}/{totalChunks} chunks");
                yield return null;
            }
        }

        OnAllDataLoaded?.Invoke();

        Debug.Log("═══════════════════════════════════════════════════════════");
        Debug.Log($"║ ✓ ALL DATA FOR {datasetName} LOADED!");
        Debug.Log($"║ Total chunks: {totalChunks}");
        Debug.Log("═══════════════════════════════════════════════════════════");
    }

    public int GetTotalCachedChunks()
    {
        int count = 0;
        while (File.Exists(GetChunkPath(count)))
            count++;
        return count;
    }

    public string LoadChunk(int chunkIndex)
    {
        string chunkPath = GetChunkPath(chunkIndex);
        if (File.Exists(chunkPath))
            return File.ReadAllText(chunkPath);

        return null;
    }

    public List<string> LoadChunkRange(int startIndex, int count)
    {
        var chunks = new List<string>();
        for (int i = startIndex; i < startIndex + count; i++)
        {
            string chunk = LoadChunk(i);
            if (chunk != null)
                chunks.Add(chunk);
            else
                break;
        }

        return chunks;
    }
}