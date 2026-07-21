// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

namespace Teapot
{
    public class GeopositionMarker : MonoBehaviour
    {
        [SerializeField] private MeshRenderer meshRenderer;

        private CesiumGlobeAnchor _cesiumGlobeAnchor;

        private void Awake()
        {
            _cesiumGlobeAnchor = GetComponent<CesiumGlobeAnchor>();
        }

        public void UpdateGeoposition(double latitude, double longitude)
        {
            _cesiumGlobeAnchor.longitudeLatitudeHeight = new double3(longitude, latitude, 0);

            if (meshRenderer)
                meshRenderer.enabled = true;

            Logging.Log($"[{nameof(GeopositionMarker)}] Updated Geoposition: Latitude={latitude}, Longitude={longitude}");
        }
    }
}
