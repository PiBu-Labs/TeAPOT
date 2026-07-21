// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using TMPro;
using UnityEngine;

public class ErrorInfo : MonoBehaviour
{
    [SerializeField] private TMP_Text infoText;

    private Camera _mainCamera;

    private void Awake()
    {
        _mainCamera = Camera.main;
    }

    private void Update()
    {
        transform.LookAt(_mainCamera.transform);
    }

    public void UpdateErrorDistance(float distance)
    {
        infoText.text = $"Error: {distance:F3} m";
    }
}
