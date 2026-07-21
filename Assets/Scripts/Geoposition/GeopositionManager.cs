// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System;
using Hyper.PCAAprilTag;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Teapot
{
    public class GeopositionManager : MonoBehaviour
    {
        [SerializeField] private float scanDuration = 1f;
        [SerializeField] private AprilTagDetector aprilTagDetector;
        [SerializeField] private int poseDetectiongTagID = 101;

        private bool _isActivated;
        private float _activeTime;

        private readonly List<DetectionResult> _detectionResults = new();
        private readonly List<GeopositionHeading> _geopositionHeadings = new();

        private DateTime _activationTimestamp;

        private Georeference _georeference;
        private AudioSource _audioSource;

        private void Awake()
        {
            _georeference = GetComponentInChildren<Georeference>();
            _audioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            AprilTagDetector.OnHasDetectionResult += OnHasDetectionResult;
            WebSocketClient.OnGeoPositionHeadingReceived += OnGeoPositionHeadingReceived;
        }

        private void OnDisable()
        {
            AprilTagDetector.OnHasDetectionResult -= OnHasDetectionResult;
            WebSocketClient.OnGeoPositionHeadingReceived -= OnGeoPositionHeadingReceived;
        }

        public void Activate()
        {
            if (_isActivated) return;

            _isActivated = true;
            _activeTime = 0f;
            _activationTimestamp = DateTime.UtcNow;

            aprilTagDetector.Activate();
        }

        private void Deactivate()
        {
            _isActivated = false;

            aprilTagDetector.Deactivate();

            _audioSource.Play();

            Logging.Log($"[{nameof(GeopositionManager)}] Activation Timestamp: {_activationTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
            Logging.Log($"[{nameof(GeopositionManager)}] There are {_detectionResults.Count} detectionResults.");
            foreach (var result in _detectionResults)
            {
                Logging.Log($"[{nameof(GeopositionManager)}] Timestamp: {result.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
                Logging.Log($"[{nameof(GeopositionManager)}] Detected tag position: {result.DetectedTags[0].Position}, rotation: {result.DetectedTags[0].Rotation.eulerAngles}");
                Logging.Log($"[{nameof(GeopositionManager)}] Camera pose position: {result.CameraPose.position}, rotation: {result.CameraPose.rotation.eulerAngles}");
            }

            Logging.Log($"[{nameof(GeopositionManager)}] There are {_geopositionHeadings.Count} geopositionHeadings.");
            foreach (var geopositionHeading in _geopositionHeadings)
            {
                Logging.Log(
                    $"[{nameof(GeopositionManager)}] Timestamp: {geopositionHeading.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
                Logging.Log(
                    $"[{nameof(GeopositionManager)}] Latitude: {geopositionHeading.Latitude}, longitude: {geopositionHeading.Longitude}, heading: {geopositionHeading.Heading}");
            }

            // Set the Georeference with the collected detection results and geo position headings
            _georeference.SetWithGeopositionHeadingOfUnityOrigin(_detectionResults, _geopositionHeadings);

            _detectionResults.Clear();
            _geopositionHeadings.Clear();
        }

        private void Update()
        {
            if (!_isActivated) return;

            _activeTime += Time.deltaTime;
            if (_activeTime >= scanDuration)
                Deactivate();
        }

        /// <summary>
        /// Called when there is a new DetectionResult from AprilTagDetector.
        /// </summary>
        private void OnHasDetectionResult(DetectionResult detectionResult)
        {
            if (detectionResult.DetectedTags[0].ID != poseDetectiongTagID) return;

            _detectionResults.Add(detectionResult);
        }

        /// <summary>
        /// Called when there is a new GeoPositionHeading from WebSocketClient.
        /// </summary>
        private void OnGeoPositionHeadingReceived(GeopositionHeading geopositionHeading)
        {
            if (!_isActivated) return;

            // Only add if the timestamp is different from the last one to avoid duplicates
            if (_geopositionHeadings.Count == 0 || _geopositionHeadings.Last().Timestamp != geopositionHeading.Timestamp)
                _geopositionHeadings.Add(geopositionHeading);
        }
    }
}
