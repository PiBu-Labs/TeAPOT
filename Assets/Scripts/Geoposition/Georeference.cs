// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System;
using System.Collections.Generic;
using CesiumForUnity;
using Hyper.PCAAprilTag;
using Unity.Mathematics;
using UnityEngine;

namespace Teapot
{
    /// <summary>
    /// Georeference helper class to manage CesiumGeoreference origin setting and getting.
    /// </summary>
    public class Georeference : MonoBehaviour
    {
        public static event Action<double, double, float> OnGeoreferenceIsSet;

        [SerializeField] private GeopositionChecker geopositionChecker;

        private double _worldHeading;
        private float _cameraYRotation;

        private CesiumGeoreference _cesiumGeoreference;

        private void Awake()
        {
            _cesiumGeoreference = GetComponent<CesiumGeoreference>();
        }

        /// <summary>
        /// Set the CesiumGeoreference with Unity origin based on AprilTag detection results and geoposition headings.
        /// </summary>
        public void SetWithGeopositionHeadingOfUnityOrigin(List<DetectionResult> detectionResults,
            List<GeopositionHeading> geopositionHeadings)
        {
            // ---------------------------------------------------------------------------------------------------------
            // First, we have to fix the georeference true north heading based on the detection results

            var headingSamples = new List<GeoStats.HeadingSample>();
            var geoAndUnityPosForTags = new List<(double latitude, double longitude, Vector3 unityPosition)>();

            foreach (var detectionResult in detectionResults)
            {
                var idx = FindClosestIndex(geopositionHeadings, detectionResult.Timestamp);
                if (idx >= 0)
                {
                    var detectedTag = detectionResult.DetectedTags[0];
                    var geopositionHeading = geopositionHeadings[idx];

                    // Project the up vector onto the XZ plane to ignore vertical rotation
                    var upXZ = Vector3.ProjectOnPlane(detectedTag.Rotation * Vector3.up, Vector3.up);

                    // Calculate the Y rotation angle
                    _cameraYRotation = Quaternion.LookRotation(upXZ, Vector3.up).eulerAngles.y;
                    _cameraYRotation = 360 - _cameraYRotation;

                    // Calculate the real world heading (true north)
                    _worldHeading = _cameraYRotation + geopositionHeading.Heading;
                    _worldHeading %= 360;

                    headingSamples.Add(new GeoStats.HeadingSample(_worldHeading));

                    Logging.Log($"[{nameof(Georeference)}] World Heading: {_worldHeading}");

                    // Store geoposition and Unity position for the next loop
                    geoAndUnityPosForTags.Add((
                        geopositionHeading.Latitude,
                        geopositionHeading.Longitude,
                        detectedTag.Position));
                }
            }

            // Stop if we have no heading data
            if (headingSamples.Count == 0)
            {
                Logging.LogWarning($"[{nameof(Georeference)}] No data for headingSamples!");
                return;
            }

            // Compute mean heading from the sample list
            var headingResult = GeoStats.ComputeHeading(headingSamples);

            // Set the CesiumGeoreference true north heading
            _cesiumGeoreference.transform.localRotation =
                Quaternion.Euler(0, (float)-headingResult.MeanHeadingDeg, 0);

            // ---------------------------------------------------------------------------------------------------------
            // Second, we calculate the Unity origin geoposition based on the detection results

            var positionSamples = new List<GeoStats.PositionSample>();

            foreach (var geoAndUnityPosForTag in geoAndUnityPosForTags)
            {
                // Calculate Unity origin latitude and longitude using the detected device pose
                var (originLatitude, originLongitude) = GetUnityOriginGeoposition(
                    geoAndUnityPosForTag.latitude,
                    geoAndUnityPosForTag.longitude,
                    geoAndUnityPosForTag.unityPosition);

                Logging.Log(
                    $"[{nameof(Georeference)}] Origin latitude: {originLatitude}, longitude: {originLongitude}");

                // Add to a sample list for GeoStats computation
                positionSamples.Add(new GeoStats.PositionSample(originLatitude, originLongitude, _worldHeading));
            }

            // Stop if we have no position data
            if (positionSamples.Count == 0)
            {
                Logging.LogWarning($"[{nameof(Georeference)}] No data for positionSamples!");
                return;
            }

            // Compute mean geoposition from the sample list
            var positionResult = GeoStats.ComputePosition(positionSamples);

            // Set the CesiumGeoreference with the averaged Unity origin geoposition
            _cesiumGeoreference.SetOriginLongitudeLatitudeHeight(positionResult.MeanLonDeg,
                positionResult.MeanLatDeg, 0);

            OnGeoreferenceIsSet?.Invoke(positionResult.MeanLatDeg, positionResult.MeanLonDeg, _cesiumGeoreference.transform.rotation.eulerAngles.y);

            Logging.Log(
                $"[{nameof(Georeference)}] FINAL origin latitude: {_cesiumGeoreference.latitude}, longitude: {_cesiumGeoreference.longitude}, heading: {_cesiumGeoreference.transform.rotation.eulerAngles.y}");

            if (geopositionChecker.isActiveAndEnabled)
            {
                // Move the mobile device geoposition for quick checking
                geopositionChecker.UpdateMobileDeviceGeoposition(positionResult.MeanLatDeg,
                    positionResult.MeanLonDeg);

                // Fill with the test ground truth geoposition
                geopositionChecker.UpdateTestGeoposition();
            }
        }

        /// <summary>
        /// Find the index of the closest GeoPositionHeading in a list to the target DateTime.
        /// </summary>
        private static int FindClosestIndex(IReadOnlyList<GeopositionHeading> list, DateTime target)
        {
            var n = list.Count;
            if (n == 0) return -1;
            if (target <= list[0].Timestamp) return 0;
            if (target >= list[n - 1].Timestamp) return n - 1;

            int lo = 0, hi = n - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var midT = list[mid].Timestamp;
                if (midT == target) return mid;
                if (target < midT) hi = mid - 1;
                else lo = mid + 1;
            }

            var right = lo;
            var left = lo - 1;
            var diffRight = (list[right].Timestamp - target).Duration();
            var diffLeft = (target - list[left].Timestamp).Duration();
            return diffRight <= diffLeft ? right : left;
        }

        /// <summary>
        /// Return Unity origin geoposition given the mobile device geoposition and Unity position.
        /// Here, we do not care about the real world altitude (set to 0).
        /// </summary>
        private (double originLatitude, double originLongitude) GetUnityOriginGeoposition(double mobileDeviceLatitude,
            double mobileDeviceLongitude, Vector3 mobileDevicePosition)
        {
            // First we set the CesiumGeoreference origin to the mobile device geoposition
            _cesiumGeoreference.SetOriginLongitudeLatitudeHeight(mobileDeviceLongitude, mobileDeviceLatitude, 0);

            // In the temporary georeference (origin at the device), Unity +X ≈ East, +Z ≈ North
            // The Unity origin is at the negative of the device's Unity position
            var toOrigin = -mobileDevicePosition;
            var toOriginInversed = _cesiumGeoreference.transform.InverseTransformDirection(toOrigin);
            toOriginInversed.y = 0f; // ignore altitude

            var positionEcef = _cesiumGeoreference.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(toOriginInversed.x, toOriginInversed.y, toOriginInversed.z));
            var llh = CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(positionEcef);

            // Return the Unity origin geoposition
            return (llh.y, llh.x);
        }
    }
}
