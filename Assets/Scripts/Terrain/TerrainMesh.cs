// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System;
using System.Collections.Generic;
using System.IO;
using Teapot;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainMesh : MonoBehaviour
{
    private const string TAG = "TerrainMesh";

    private static readonly int WorldToPlaneMatID = EnvironmentDepthManager.ID("_WorldToPlaneMatrix");
    private static readonly int PlaneOriginID = EnvironmentDepthManager.ID("_PlaneOrigin");
    private static readonly int PlaneNormalID = EnvironmentDepthManager.ID("_PlaneNormal");
    private static readonly int PlaneRightID = EnvironmentDepthManager.ID("_PlaneRight");
    private static readonly int PlaneForwardID = EnvironmentDepthManager.ID("_PlaneForward");

    private static readonly int AccumWID = EnvironmentDepthManager.ID("_AccumW");
    private static readonly int AccumHWID = EnvironmentDepthManager.ID("_AccumHW");
    private static readonly int AccumCosWID = EnvironmentDepthManager.ID("_AccumCosW");

    private static readonly int DbgBufID = EnvironmentDepthManager.ID("_DbgBuf");

    private static readonly int EyeID = EnvironmentDepthManager.ID("_eye");

    // Plane grid definition
    private static readonly int UMinID = EnvironmentDepthManager.ID("_uMin");
    private static readonly int VMinID = EnvironmentDepthManager.ID("_vMin");
    private static readonly int CellSizeID = EnvironmentDepthManager.ID("_cellSize");
    private static readonly int CellAreaID = EnvironmentDepthManager.ID("_cellArea");
    private static readonly int GridWID = EnvironmentDepthManager.ID("_gridW");
    private static readonly int GridHID = EnvironmentDepthManager.ID("_gridH");

    // Weighting
    private static readonly int GaussTauID = EnvironmentDepthManager.ID("_gaussTau");

    // Fixed-point scales
    private static readonly int WScaleID = EnvironmentDepthManager.ID("_wScale");
    private static readonly int HWScaleID = EnvironmentDepthManager.ID("_hwScale");

    // Resolve
    private static readonly int HeightWID = EnvironmentDepthManager.ID("_HeightW");
    private static readonly int HeightMuID = EnvironmentDepthManager.ID("_HeightMu");

    private static readonly int HeightMuPrevID = EnvironmentDepthManager.ID("_HeightMuPrev");
    private static readonly int StatsID = EnvironmentDepthManager.ID("_Stats");
    private static readonly int ConfThreshID = EnvironmentDepthManager.ID("_ConfThresh");
    private static readonly int DeltaScaleID = EnvironmentDepthManager.ID("_DeltaScale");

    // Material
    private static readonly int PlaneSizeID = EnvironmentDepthManager.ID("_PlaneSize");
    private static readonly int LODID = EnvironmentDepthManager.ID("_LOD");
    private static readonly int HeightWMinID = EnvironmentDepthManager.ID("_HeightWMin");

    [Header("Terrain Configuration")] [SerializeField, Range(PlaneGenerator.MinSize, PlaneGenerator.MaxSize)]
    private float terrainWidth = PlaneGenerator.DefaultSize;

    [SerializeField, Range(PlaneGenerator.MinSize, PlaneGenerator.MaxSize)]
    private float terrainLength = PlaneGenerator.DefaultSize;

    [SerializeField, Range(PlaneGenerator.MinResolution, PlaneGenerator.MaxResolution)]
    private int terrainWidthResolution = PlaneGenerator.DefaultResolution;

    [SerializeField, Range(PlaneGenerator.MinResolution, PlaneGenerator.MaxResolution)]
    private int terrainLengthResolution = PlaneGenerator.DefaultResolution;

    [Header("Compute Shader Configuration")] [SerializeField]
    private ComputeShader terrainShader;

    [Header("Export")] [SerializeField] private string filename = "TerrainMesh";
    [SerializeField] private float requiredCoverage = 0.99f; // fraction of texels with conf>=confThresh

    [SerializeField] private float maxDelta = 0.01f; // meters (max |Δh| allowed)

    [SerializeField] private float stableHoldSeconds = 1.0f; // must stay stable this long
    [SerializeField] private float cooldownSeconds = 10f; // minimum time between exports

    private ComputeKernel _clearAccumKernel;
    private ComputeKernel _clearPersistentKernel;
    private ComputeKernel _splatKernel;
    private ComputeKernel _resolveFuseKernel;
    private ComputeKernel _stabilityReduceKernel;
    private ComputeKernel _copyHeightMuKernel;

    private RenderTexture _accumW;
    private RenderTexture _accumHW;
    private RenderTexture _accumCosW;

    private RenderTexture _outKnown;

    private RenderTexture _heightW;
    private RenderTexture _heightMu;

    private RenderTexture _heightMuPrev;
    private ComputeBuffer _stats;

    private bool _started;
    private bool _destroyed;

    private PlaneData _planeData;

    private MeshFilter _meshFilter;

    private Material _material;
    private Mesh _mesh;

    private int _previousWidth;
    private int _previousHeight;

    private bool _clearPersistent;

    private float _stableTimer;
    private float _cooldownTimer;

    private bool _hasPrev = false;

    private float _cellWidth;
    private float _cellHeight;
    private float _cellArea;

    // “Known” threshold on _HeightW
    [SerializeField] private float wKnownMin = 0.10f;

    // scales must match HLSL
    private const float DeltaScale = 10000f; // 1 unit = 0.0001 m

    [SerializeField] private float minDispatchesPerSecond = 1f;
    [SerializeField] private float maxDispatchesPerSecond = 4f;

    [SerializeField] private float goodFrameMs = 11.5f;
    [SerializeField] private float badFrameMs = 13.0f;

    private float _smoothedFrameMs = 11.5f; // start at goodFrameMs so we don't back off immediately
    private float _currentDispatchInterval = 0.25f; // start at 4 Hz
    private float _timeSinceLastTerrain = 0f;
    private int _stableFrames = 0;

    private float _lastResolveTime = -1f;
    private float _resolveDt = 0f;
    private bool _statsPending = false;
    private int _terrainGeneration;

    private enum TerrainStage
    {
        None,
        ClearAccum,
        SplatLeft,
        SplatRight,
        Resolve
    }

    private TerrainStage _pendingStage = TerrainStage.None;

    [Header("Debug")] [SerializeField] private TMP_Text debugText;
    [SerializeField] private bool autoExport = true;

    // CSV logging
    private bool _runActive = false;
    private float _runStartTime = -1f;
    private int _resolveCount = 0;

    private float _frameMsSum = 0f;
    private int _frameMsSamples = 0;

    private bool _autoExportedThisTerrain;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        _previousWidth = 0;
        _previousHeight = 0;

        _meshFilter = GetComponent<MeshFilter>();
        var meshRenderer = GetComponent<MeshRenderer>();

        _material = meshRenderer.material;
        _material.SetVector(PlaneSizeID, new Vector2(terrainWidth, terrainLength));

        // Black (weight=0) so no cells render until compute writes real weight data
        _material.SetTexture(HeightWID, Texture2D.blackTexture);
        _material.SetFloat(HeightWMinID, wKnownMin);

        // Generate plane
        _planeData = PlaneGenerator.Generate(new Vector2(terrainWidth, terrainLength),
            new int2(terrainWidthResolution, terrainLengthResolution));

        // Initialize clear accumulation kernel
        _clearAccumKernel = new ComputeKernel(terrainShader, "ClearAccumCS");

        // Initialize clear persistent kernel
        _clearPersistentKernel = new ComputeKernel(terrainShader, "ClearPersistentCS");

        // Initialize splat kernel
        _splatKernel = new ComputeKernel(terrainShader, "SplatCS");

        // Initialize resolve fuse kernel
        _resolveFuseKernel = new ComputeKernel(terrainShader, "ResolveFuseCS");

        // Initialize stability reduce kernel
        _stabilityReduceKernel = new ComputeKernel(terrainShader, "StabilityReduceCS");
        _copyHeightMuKernel = new ComputeKernel(terrainShader, "CopyHeightMuCS");

        _stats = new ComputeBuffer(2, sizeof(uint), ComputeBufferType.Structured);

        InitMesh();

        ScanLoop();

        // _started = true;

        var subsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);
        if (subsystems.Count > 0)
        {
            var xrInputSubsystem = subsystems[0];
            xrInputSubsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
        }
    }

    private void OnEnable()
    {
        EvalTerrain.OnEvalStart += GenerateTerrain;
        TerrainReconstruction.OnTerrainMarkerUpdated += GenerateTerrain;
    }

    private void OnDisable()
    {
        EvalTerrain.OnEvalStart -= GenerateTerrain;
        TerrainReconstruction.OnTerrainMarkerUpdated -= GenerateTerrain;
    }

    private void GenerateTerrainDemo(Vector3 position, Quaternion rotation)
    {
        transform.position = new Vector3(1.23f, 0f, 0.15f);

        ResetTerrain();
    }

    private void GenerateTerrain(Vector3 position, Quaternion rotation)
    {
        Logging.Log($"[{TAG}] GenerateTerrain from {transform.position}, {transform.rotation} to {position}, {rotation}");

        // Keep only the "spin" from the incoming rotation.
        var dir = Vector3.ProjectOnPlane(rotation * Vector3.up, Vector3.up);

        // Fallback in the rare case the projected vector is too small.
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.forward;

        var finalRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

        transform.SetPositionAndRotation(position, finalRotation);
        // transform.position = Vector3.right;

        ResetTerrain();
    }

    private void OnDestroy()
    {
        _destroyed = true;

        _accumW?.Release();
        _accumHW?.Release();
        _accumCosW?.Release();

        _outKnown?.Release();

        _heightW?.Release();
        _heightMu?.Release();

        _heightMuPrev?.Release();
        _heightMuPrev = null;

        _stats?.Release();
        _stats = null;
    }

    private void OnTrackingOriginUpdated(XRInputSubsystem obj)
    {
    //     ResetTerrain();
    }

    private void InitMesh()
    {
        _mesh = new Mesh
        {
            indexFormat = IndexFormat.UInt32,
            vertices = _planeData.Vertices.ToArray(),
            normals = _planeData.Normals.ToArray(),
            uv = _planeData.UVs.ToArray(),
            triangles = _planeData.Triangles.ToArray(),
        };

        _meshFilter.mesh = _mesh;
    }

    private void ResetTerrain()
    {
        _started = true;
        _clearPersistent = true;

        // Reset dispatch pacing so we don't wait for a backed-off interval
        _timeSinceLastTerrain = 0f;
        _stableFrames = 0;
        _currentDispatchInterval = 1f / maxDispatchesPerSecond;

        _hasPrev = false;
        _lastResolveTime = -1f;
        _resolveDt = 0f;
        _statsPending = false;
        _stableTimer = 0f;
        _cooldownTimer = 0f;
        _autoExportedThisTerrain = false;

        _runActive = false;
        _runStartTime = -1f;
        _resolveCount = 0;
        _frameMsSum = 0f;
        _frameMsSamples = 0;

        _terrainGeneration++;
    }

    private void EnsureTexturesAndKernels(int width, int height)
    {
        if (_previousWidth == width && _previousHeight == height) return;

        _previousWidth = width;
        _previousHeight = height;

        // Generate render textures
        RenderTextureGenerator.EnsureRT(ref _accumW, width, height, GraphicsFormat.R32_UInt, false,
            "AccumW");
        RenderTextureGenerator.EnsureRT(ref _accumHW, width, height, GraphicsFormat.R32_SInt, false,
            "AccumHW");
        RenderTextureGenerator.EnsureRT(ref _accumCosW, width, height, GraphicsFormat.R32_UInt, false,
            "AccumCosW");

        RenderTextureGenerator.EnsureRT(ref _outKnown, width, height, GraphicsFormat.R32_SFloat, true,
            "OutKnown");

        RenderTextureGenerator.EnsureRT(ref _heightW, width, height, GraphicsFormat.R32_SFloat, true,
            "HeightW");
        RenderTextureGenerator.EnsureRT(ref _heightMu, width, height, GraphicsFormat.R32_SFloat, true,
            "HeightMu");

        RenderTextureGenerator.EnsureRT(ref _heightMuPrev, width, height, GraphicsFormat.R32_SFloat, true,
            "HeightMuPrev");

        _cellWidth = terrainWidth / width;
        _cellHeight = terrainLength / height;

        _cellArea = _cellWidth * _cellHeight;

        _clearPersistent = true;

        // Setup clear accumulation kernel
        _clearAccumKernel.Set(AccumWID, _accumW);
        _clearAccumKernel.Set(AccumHWID, _accumHW);
        _clearAccumKernel.Set(AccumCosWID, _accumCosW);

        _clearAccumKernel.Set(GridWID, width);
        _clearAccumKernel.Set(GridHID, height);

        // Setup clear persistent kernel
        _clearPersistentKernel.Set(HeightWID, _heightW);
        _clearPersistentKernel.Set(HeightMuID, _heightMu);

        _clearPersistentKernel.Set(GridWID, width);
        _clearPersistentKernel.Set(GridHID, height);

        // Setup splat kernel
        _splatKernel.Set(AccumWID, _accumW);
        _splatKernel.Set(AccumHWID, _accumHW);
        _splatKernel.Set(AccumCosWID, _accumCosW);

        _splatKernel.Set(UMinID, -0.5f * terrainWidth);
        _splatKernel.Set(VMinID, -0.5f * terrainLength);
        _splatKernel.Set(CellSizeID, new Vector2(_cellWidth, _cellHeight));
        _splatKernel.Set(CellAreaID, _cellArea);
        _splatKernel.Set(GridWID, width);
        _splatKernel.Set(GridHID, height);

        _splatKernel.Set(GaussTauID, 0.9f);

        _splatKernel.Set(WScaleID, 65536);
        _splatKernel.Set(HWScaleID, 65536);

        // Setup resolve fuse kernel
        _resolveFuseKernel.Set(AccumWID, _accumW);
        _resolveFuseKernel.Set(AccumHWID, _accumHW);
        _resolveFuseKernel.Set(AccumCosWID, _accumCosW);

        _resolveFuseKernel.Set(WScaleID, 65536);
        _resolveFuseKernel.Set(HWScaleID, 65536);

        _resolveFuseKernel.Set(HeightWID, _heightW);
        _resolveFuseKernel.Set(HeightMuID, _heightMu);

        _resolveFuseKernel.Set(GridWID, width);
        _resolveFuseKernel.Set(GridHID, height);

        // Setup stability reduce kernel
        _stabilityReduceKernel.Set(HeightWID, _heightW);
        _stabilityReduceKernel.Set(HeightMuID, _heightMu);
        _stabilityReduceKernel.Set(HeightMuPrevID, _heightMuPrev);
        _stabilityReduceKernel.Set(GridWID, width);
        _stabilityReduceKernel.Set(GridHID, height);
        _stabilityReduceKernel.Set(ConfThreshID, wKnownMin);
        _stabilityReduceKernel.Set(DeltaScaleID, DeltaScale);
        _stabilityReduceKernel.Set(StatsID, _stats);

        _copyHeightMuKernel.Set(HeightMuID, _heightMu);
        _copyHeightMuKernel.Set(HeightMuPrevID, _heightMuPrev);
        _copyHeightMuKernel.Set(GridWID, width);
        _copyHeightMuKernel.Set(GridHID, height);
    }

    private async void ScanLoop()
    {
        try
        {
            while (enabled)
            {
                await Awaitable.NextFrameAsync(destroyCancellationToken);

                var frameMs = Time.unscaledDeltaTime * 1000f;
                _smoothedFrameMs = Mathf.Lerp(_smoothedFrameMs, frameMs, 0.1f);

                if (!_started) continue;

                // Log actual frame time for the run summary
                if (_runActive)
                {
                    // _frameMsSum += _smoothedFrameMs;
                    _frameMsSum += frameMs;
                    _frameMsSamples++;
                }

                if (!EnvironmentDepthManager.DepthAvailable ||
                    !EnvironmentDepthManager.PreprocessedDepthAvailable) continue;

                // Only advance terrain scheduling when usable depth exists
                _timeSinceLastTerrain += Time.unscaledDeltaTime;

                var depthTextureSize = Shader.GetGlobalVector(EnvironmentDepthManager.DepthTexSizeID);

                var width = Mathf.CeilToInt(depthTextureSize.x);
                var height = Mathf.CeilToInt(depthTextureSize.y);

                EnsureTexturesAndKernels(width, height);

                if (_clearPersistent)
                {
                    _clearPersistent = false;
                    _clearPersistentKernel.DispatchGroups(width, height);

                    _hasPrev = false;
                    _stableTimer = 0f;
                    _cooldownTimer = 0f;

                    _pendingStage = TerrainStage.None;
                    continue;
                }

                // // Dispatch clear accumulation kernel
                // _clearAccumKernel.DispatchGroups(width, height);

                // if (_clearPersistent)
                // {
                //     _clearPersistent = false;
                //
                //     // Dispatch clear persistent kernel
                //     _clearPersistentKernel.DispatchGroups(width, height);
                //
                //     _hasPrev = false;
                //     stableTimer = 0f;
                //     cooldownTimer = 0f;
                // }

                if (transform.up.y <= 0f) continue;

                // Back off quickly if frames are bad
                if (_smoothedFrameMs > badFrameMs)
                {
                    _currentDispatchInterval = Mathf.Min(1f / minDispatchesPerSecond,
                        _currentDispatchInterval * 1.25f); // less frequent

                    _stableFrames = 0;
                    continue;
                }

                // Count stable frames
                if (_smoothedFrameMs < goodFrameMs)
                {
                    _stableFrames++;
                    _currentDispatchInterval = Mathf.Max(1f / maxDispatchesPerSecond,
                        _currentDispatchInterval * 0.8f); // symmetric with the 1.25× back-off
                }
                else
                    _stableFrames = 0;

                // Start a new terrain update only if:
                // - nothing already queued
                // - enough time has passed
                // - performance is stable
                if (_pendingStage == TerrainStage.None &&
                    _timeSinceLastTerrain >= _currentDispatchInterval &&
                    _stableFrames >= 10)
                {
                    _pendingStage = TerrainStage.ClearAccum;
                    _timeSinceLastTerrain = 0f;
                }

                // Only execute one terrain stage per frame, and only with headroom
                if (_pendingStage != TerrainStage.None && _smoothedFrameMs < goodFrameMs)
                {
                    switch (_pendingStage)
                    {
                        case TerrainStage.ClearAccum:
                            _clearAccumKernel.DispatchGroups(width, height);
                            _pendingStage = TerrainStage.SplatLeft;
                            break;

                        case TerrainStage.SplatLeft:
                            if (!_runActive)
                            {
                                _runActive = true;
                                _runStartTime = Time.unscaledTime;
                                _resolveCount = 0;
                                _frameMsSum = 0f;
                                _frameMsSamples = 0;
                            }

                            _splatKernel.Set(WorldToPlaneMatID, transform.worldToLocalMatrix);
                            _splatKernel.Set(PlaneOriginID, transform.position);
                            _splatKernel.Set(PlaneNormalID, transform.up);
                            _splatKernel.Set(PlaneRightID, transform.right);
                            _splatKernel.Set(PlaneForwardID, transform.forward);

                            _splatKernel.Set(EyeID, 0);
                            _splatKernel.DispatchGroups(width, height);
                            _pendingStage = TerrainStage.SplatRight;
                            break;

                        case TerrainStage.SplatRight:
                            _splatKernel.Set(WorldToPlaneMatID, transform.worldToLocalMatrix);
                            _splatKernel.Set(PlaneOriginID, transform.position);
                            _splatKernel.Set(PlaneNormalID, transform.up);
                            _splatKernel.Set(PlaneRightID, transform.right);
                            _splatKernel.Set(PlaneForwardID, transform.forward);

                            _splatKernel.Set(EyeID, 1);
                            _splatKernel.DispatchGroups(width, height);
                            _pendingStage = TerrainStage.Resolve;
                            _pendingStage = TerrainStage.Resolve;
                            break;

                        case TerrainStage.Resolve:
                            var now = Time.unscaledTime;
                            _resolveDt = (_lastResolveTime < 0f) ? 0f : (now - _lastResolveTime);
                            _lastResolveTime = now;

                            _resolveFuseKernel.DispatchGroups(width, height);
                            if (_runActive) _resolveCount++;

                            // Stability check (GPU-side)
                            if (!_hasPrev)
                            {
                                // First resolve: initialize prev = current
                                _copyHeightMuKernel.DispatchGroups(width, height);
                                _hasPrev = true;

                                _stableTimer = 0f;
                                _cooldownTimer = 0f;
                            }
                            else if (!_statsPending)
                            {
                                _statsPending = true;

                                // Clear stats buffer (knownCount=0, maxDelta=0)
                                _stats.SetData(new uint[2]);

                                // Compute stats comparing _HeightMu and _HeightMuPrev over "known" cells
                                _stabilityReduceKernel.DispatchGroups(width, height);

                                // Update prev to current after stats are computed
                                _copyHeightMuKernel.DispatchGroups(width, height);

                                var dtThisResolve = Mathf.Max(0.0f, _resolveDt);
                                var capturedGeneration = _terrainGeneration;

                                // Read back tiny stats buffer
                                AsyncGPUReadback.Request(_stats, req =>
                                {
                                    _statsPending = false;
                                    if (_destroyed || req.hasError || _terrainGeneration != capturedGeneration) return;

                                    var data = req.GetData<uint>();
                                    var knownCount = data[0];
                                    var maxDeltaFp = data[1];

                                    float totalCells = width * height;
                                    var coverage = (totalCells > 0f) ? (knownCount / totalCells) : 0f;

                                    var maxDeltaMeters = maxDeltaFp / DeltaScale; // DeltaScale = 10000f etc.

                                    _cooldownTimer = Mathf.Max(0f, _cooldownTimer - dtThisResolve);

                                    var stableNow = (coverage >= requiredCoverage) && (maxDeltaMeters <= maxDelta);

                                    if (stableNow) _stableTimer += dtThisResolve;
                                    else _stableTimer = 0f;

                                    if (autoExport && !_autoExportedThisTerrain && _cooldownTimer <= 0f && _stableTimer >= stableHoldSeconds)
                                    {
                                        _autoExportedThisTerrain = true;

                                        var ttsSec = (_runActive && _runStartTime > 0f) ? (Time.unscaledTime - _runStartTime) : 0f;
                                        var meanFrameMs = (_frameMsSamples > 0) ? (_frameMsSum / _frameMsSamples) : float.NaN;
                                        var avgResolveHz = (ttsSec > 1e-6f) ? (_resolveCount / ttsSec) : 0f;

                                        // AppendStabilitySummaryCsv(
                                        //     width, height,
                                        //     ttsSec,
                                        //     coverage,
                                        //     maxDeltaMeters,
                                        //     knownCount,
                                        //     _resolveCount,
                                        //     avgResolveHz,
                                        //     meanFrameMs
                                        // );

                                        // Reset run state
                                        _runActive = false;
                                        _runStartTime = -1f;

                                        //Export();
                                        _cooldownTimer = cooldownSeconds;
                                        _stableTimer = 0f;
                                    }
                                });
                            }
                            // End of stability check

                            _heightMu.GenerateMips();

                            _material.SetFloat(LODID, Mathf.Log(depthTextureSize.x / terrainWidthResolution, 2f));
                            _material.SetTexture(HeightMuID, _heightMu);
                            _material.SetTexture(HeightWID, _heightW);
                            _material.SetFloat(HeightWMinID, wKnownMin);

                            _pendingStage = TerrainStage.None;
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Export()
    {
        if (debugText) debugText.text = "Exporting...";

        var fname = $"{filename}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.ply";
        var path = Path.Combine(Application.persistentDataPath, fname);

        var colorTex = PlyExport.BuildDebugTexture(
            _heightMu.width,
            _heightMu.height,
            new Vector2(terrainWidth, terrainLength),
            centerSizeMeters: 0.075f,
            cornerSizeMeters: 0.0375f
        );

        PlyExport.ExportPointCloudBinary(
            heightMu: _heightMu,
            planeSize: new Vector2(terrainWidth, terrainLength),
            path: path,
            weightTex: null,
            weightMin: 0.01f,
            colorTex: colorTex,
            includeNormals: false,
            flipX: true // match Scaniverse FBX
        );

        Logging.Log($"[{TAG}] PLY written: {path}");

        if (debugText) debugText.text = "Exported!";

        Destroy(colorTex);
    }

    #region CSV logger
    private void AppendStabilitySummaryCsv(
        int gridW, int gridH,
        float ttsSec,
        float coverageAtStable,
        float maxDeltaAtStable,
        uint knownCount,
        int resolveCount,
        float avgResolveHz,
        float meanFrameMs
    )
    {
        var path = Path.Combine(Application.persistentDataPath, $"{filename}-stability-summary.csv");
        var exists = File.Exists(path);

        using var sw = new StreamWriter(path, append: true, System.Text.Encoding.UTF8);

        if (!exists)
        {
            sw.WriteLine(
                "Time Stamp,grid_w,grid_h,required_coverage,w_known_min,max_delta,stable_hold_seconds,tts_sec,coverage_at_stable,known_count,max_delta_at_stable,resolve_count,avg_resolve_Hz,mean_frame_ms");
        }

        sw.WriteLine(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}," +
            $"{gridW},{gridH}," +
            $"{requiredCoverage:F3}," +
            $"{wKnownMin:F3}," +
            $"{maxDelta:F3}," +
            $"{stableHoldSeconds:F3}," +
            $"{ttsSec:F5}," +
            $"{coverageAtStable:F3}," +
            $"{knownCount}," +
            $"{maxDeltaAtStable:F4}," +
            $"{resolveCount}," +
            $"{avgResolveHz:F2}," +
            $"{meanFrameMs:F3}"
        );

        Logging.Log($"[{TAG}] Stability summary appended: {path}");
    }
    #endregion
}
