// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System;
using UnityEngine;

namespace Teapot
{
    public class TerrainReconstruction : MonoBehaviour
    {
        public static event Action<Vector3, Quaternion> OnTerrainMarkerUpdated;

        [SerializeField] private GeopositionMarker terrainMarker;

        [SerializeField] private double terrainLatitude;
        [SerializeField] private double terrainLongitude;

        private void OnEnable()
        {
            Georeference.OnGeoreferenceIsSet += OnGeoreferenceIsSet;
        }

        private void OnDisable()
        {
            Georeference.OnGeoreferenceIsSet -= OnGeoreferenceIsSet;
        }

        private void OnGeoreferenceIsSet(double originLatitude, double originLongitude, float originHeading)
        {
            terrainMarker.UpdateGeoposition(terrainLatitude, terrainLongitude);

            OnTerrainMarkerUpdated?.Invoke(terrainMarker.transform.position, terrainMarker.transform.rotation);
        }
    }
}
