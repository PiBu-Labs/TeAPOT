// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using UnityEngine;

namespace Teapot
{
    public class GeopositionChecker : MonoBehaviour
    {
        [SerializeField] private double testLatitude;
        [SerializeField] private double testLongitude;
        [SerializeField] private GeopositionMarker mobileDeviceMarker;
        [SerializeField] private GeopositionMarker testMarker;
        [SerializeField] private GeopositionMarker groundTruthMarker;

        public void UpdateMobileDeviceGeoposition(double latitude, double longitude)
        {
            mobileDeviceMarker.UpdateGeoposition(latitude, longitude);
        }

        public void UpdateTestGeoposition()
        {
            testMarker.UpdateGeoposition(testLatitude, testLongitude);
        }
    }
}
