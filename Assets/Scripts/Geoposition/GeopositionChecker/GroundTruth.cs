// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using Hyper.PCAAprilTag;
using System;
using UnityEngine;

public class GroundTruth : MonoBehaviour
{
    public static event Action<float, float, float, float, float> OnErrorUpdated;

    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private ErrorInfo errorInfo;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private AprilTagDetector aprilTagDetector;
    [SerializeField] private Transform testMarkerTransform;
    [SerializeField] private int groundTruthTagID = 0;

    private bool _isActivated;

    private void OnEnable()
    {
        AprilTagDetector.OnHasDetectionResult += OnHasDetectionResult;
    }

    private void OnDisable()
    {
        AprilTagDetector.OnHasDetectionResult -= OnHasDetectionResult;
    }

    private void OnHasDetectionResult(DetectionResult detectionResult)
    {
        if (detectionResult.DetectedTags[0].ID != groundTruthTagID) return;

        aprilTagDetector.Deactivate();

        UpdateErrorCalculation(detectionResult.DetectedTags[0].Position);
    }

    private void UpdateErrorCalculation(Vector3 position)
    {
        var newPosition = new Vector3(position.x, 0, position.z);

        transform.position = newPosition;
        meshRenderer.enabled = true;

        lineRenderer.SetPosition(0, new Vector3(position.x, 1.5f, position.z));
        lineRenderer.SetPosition(1, new Vector3(testMarkerTransform.position.x, 1.5f, testMarkerTransform.position.z));

        var errorDistance = Vector3.Distance(newPosition, testMarkerTransform.position);

        errorInfo.UpdateErrorDistance(errorDistance);

        OnErrorUpdated?.Invoke(errorDistance, position.x, position.z, testMarkerTransform.position.x, testMarkerTransform.position.z);
    }
}
