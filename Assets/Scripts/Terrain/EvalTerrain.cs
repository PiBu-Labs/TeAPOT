// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System;
using Hyper.PCAAprilTag;
using UnityEngine;

public class EvalTerrain : MonoBehaviour
{
    [SerializeField] private AprilTagDetector aprilTagDetector;

    public static event Action<Vector3, Quaternion> OnEvalStart;

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
        aprilTagDetector.Deactivate();

        OnEvalStart?.Invoke(detectionResult.DetectedTags[0].Position, detectionResult.DetectedTags[0].Rotation);
    }
}
