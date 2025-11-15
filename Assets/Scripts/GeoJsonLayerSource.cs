using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class GeoJsonLayerSource : LayerSource
{
    [SerializeField] private GeoJsonDaraManager _dataManager;

    private void Start()
    {
        if (_dataManager == null)
        {
            Debug.LogError("CadastreLayerBootstrap: dataManager is not assigned, cannot load cached cadastre chunks.");
            return;
        }

        int totalChunks = _dataManager.GetTotalCachedChunks();
        if (totalChunks <= 0)
        {
            Debug.LogWarning("CadastreLayerBootstrap: No cached cadastre chunks found.");
            return;
        }

        // Загружаем все чанки разом
        List<string> chunks = _dataManager.LoadChunkRange(0, totalChunks);
        if (chunks == null || chunks.Count == 0)
        {
            Debug.LogWarning("CadastreLayerBootstrap: LoadChunkRange returned no data.");
            return;
        }

        // Собираем единый слой "cadastre" из всех чанков
        var combinedLayer = new MapLayer
        {
            Id = Id,
            DisplayName = DisplayName,
            Visible = true,
            Color = Color,
            LineWidth = LineWidth,
            LineSimplifyTolerance = LineSimplifyTolerance
        };

        foreach (var json in chunks)
        {
            if (string.IsNullOrWhiteSpace(json))
                continue;

            MapLayer chunkLayer = LoadLayer(json);

            if (chunkLayer?.Features != null && chunkLayer.Features.Count > 0)
                combinedLayer.Features.AddRange(chunkLayer.Features);
        }

        Debug.Log($"CadastreLayerBootstrap: Rendering cadastre layer with {combinedLayer.Features.Count} features from {chunks.Count} chunks.");
    }

    public MapLayer LoadLayer(string geoJson)
    {
        var layer = new MapLayer
        {
            Id = Id,
            DisplayName = DisplayName,
            Visible = true,
            Color = Color,
            LineWidth = LineWidth,
            LineSimplifyTolerance = LineSimplifyTolerance
        };

        if (string.IsNullOrWhiteSpace(geoJson))
        {
            Debug.LogWarning("CadastreGeoJsonLayerSource: GeoJSON is null or empty.");
            return layer;
        }

        JObject root;
        try
        {
            root = JObject.Parse(geoJson);
        }
        catch (Exception e)
        {
            Debug.LogError($"CadastreGeoJsonLayerSource: Failed to parse GeoJSON: {e.Message}");
            return layer;
        }

        var features = root["features"] as JArray;
        if (features == null)
        {
            Debug.LogWarning("CadastreGeoJsonLayerSource: No 'features' array found in GeoJSON.");
            return layer;
        }

        foreach (var featureToken in features)
        {
            if (featureToken == null) continue;

            var geom = featureToken["geometry"];
            var props = featureToken["properties"] as JObject;

            if (geom == null)
                continue;

            var geomType = geom["type"]?.ToString();
            var coordsToken = geom["coordinates"];
            if (string.IsNullOrEmpty(geomType) || coordsToken == null)
                continue;

            switch (geomType)
            {
                case "Polygon":
                {
                    var feature = CreateFeatureFromPolygon(coordsToken, props);
                    if (feature != null)
                        layer.Features.Add(feature);
                    break;
                }

                case "MultiPolygon":
                {
                    // Каждое подполигональное кольцо делаем отдельным feature с теми же свойствами
                    foreach (var poly in (IEnumerable<JToken>) coordsToken)
                    {
                        var feature = CreateFeatureFromPolygon(poly, props);
                        if (feature != null)
                            layer.Features.Add(feature);
                    }

                    break;
                }

                case "LineString":
                {
                    var feature = CreateFeatureFromLineString(coordsToken, props);
                    if (feature != null)
                        layer.Features.Add(feature);
                    break;
                }

                case "MultiLineString":
                {
                    foreach (var line in (IEnumerable<JToken>) coordsToken)
                    {
                        var feature = CreateFeatureFromLineString(line, props);
                        if (feature != null)
                            layer.Features.Add(feature);
                    }

                    break;
                }

                default:
                    Debug.LogWarning($"CadastreGeoJsonLayerSource: Unsupported geometry type: {geomType}");
                    break;
            }
        }

        Debug.Log($"CadastreGeoJsonLayerSource: Loaded cadastre layer with {layer.Features.Count} features.");
        return layer;
    }

    private MapFeature CreateFeatureFromPolygon(JToken coordsToken, JObject props)
    {
        // Polygon: coordinates = [ [ [lon, lat], ... ] , [ ... inner rings ... ] ]
        if (!(coordsToken is JArray ringsArray) || ringsArray.Count == 0)
            return null;

        var geometry = new MapGeometry { Type = GeometryType.Polygon };

        foreach (var ringToken in ringsArray)
        {
            if (!(ringToken is JArray ringArray) || ringArray.Count == 0)
                continue;

            var ring = new List<Vector2>(ringArray.Count);

            foreach (var coord in ringArray)
            {
                if (coord is not JArray coordArray || coordArray.Count < 2)
                    continue;

                if (!TryParseLonLat(coordArray, out var lonLat))
                    continue;

                ring.Add(lonLat);
            }

            if (ring.Count > 0)
                geometry.CoordinatesLonLat.Add(ring);
        }

        if (geometry.CoordinatesLonLat.Count == 0)
            return null;

        var feature = new MapFeature { Geometry = geometry };

        FillFeaturePropertiesAndIds(feature, props);
        return feature;
    }

    private MapFeature CreateFeatureFromLineString(JToken coordsToken, JObject props)
    {
        // LineString: coordinates = [ [lon, lat], ... ]
        if (!(coordsToken is JArray coordsArray) || coordsArray.Count == 0)
            return null;

        var geometry = new MapGeometry { Type = GeometryType.LineString };

        var line = new List<Vector2>(coordsArray.Count);
        foreach (var coord in coordsArray)
        {
            if (coord is not JArray coordArray || coordArray.Count < 2)
                continue;

            if (!TryParseLonLat(coordArray, out var lonLat))
                continue;

            line.Add(lonLat);
        }

        if (line.Count == 0)
            return null;

        geometry.CoordinatesLonLat.Add(line);

        var feature = new MapFeature { Geometry = geometry };

        FillFeaturePropertiesAndIds(feature, props);
        return feature;
    }

    private bool TryParseLonLat(JArray coordArray, out Vector2 lonLat)
    {
        lonLat = default;

        // GeoJSON: [lon, lat, (optional) alt]
        if (coordArray.Count < 2)
            return false;

        if (!double.TryParse(coordArray[0]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            return false;

        if (!double.TryParse(coordArray[1]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
            return false;

        lonLat = new Vector2((float) lon, (float) lat);
        return true;
    }

    private void FillFeaturePropertiesAndIds(MapFeature feature, JObject props)
    {
        if (props != null)
        {
            foreach (var kv in props)
            {
                feature.Properties[kv.Key] = kv.Value?.ToString() ?? string.Empty;
            }
        }

        // Пытаемся подобрать удобные Id/Name
        string id = null;
        string name = null;

        if (props != null)
        {
            // Кандидаты для label/id (частично повторяют логику GetLabel в старом CadastreRenderer)
            string[] candidates = { "label", "parcelId", "parcel_id", "id_localId", "localId", "LOCALID", "name", "id", "OBJECTID" };

            foreach (var key in candidates)
            {
                if (id == null && props[key] != null)
                    id = props[key]?.ToString();
                if (name == null && props[key] != null)
                    name = props[key]?.ToString();
            }
        }

        feature.Id = id ?? Guid.NewGuid().ToString("N");
        feature.Name = name ?? feature.Id;
    }
}