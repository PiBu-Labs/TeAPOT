// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using NativeWebSocket;
using System;
using System.Collections;
using UnityEngine;

namespace Teapot
{
    /// <summary>
    /// WebSocket client to connect to the SPOT app and receive geoposition updates.
    /// </summary>
    public class WebSocketClient : MonoBehaviour
    {
        private static string TAG = "WebSocketClient";

        [SerializeField] private string serverIPAddress = "172.20.10.1";
        [SerializeField] private int serverPort = 8888;

        private WebSocket _webSocket;

        [SerializeField] private int retryDelay = 2;

        private bool _isReconnecting;

        public static event Action<bool> OnConnectionUpdate;
        public static event Action<GeopositionHeading> OnGeoPositionHeadingReceived;

        public enum MessageEnum : byte
        {
            Location = 255
        }

        private void Start()
        {
            Debug.Log($"[{TAG}] Starting");

            ConnectToServer();
        }

        private void ConnectToServer()
        {
            if (_webSocket is { State: WebSocketState.Open })
            {
                Logging.Log($"[{TAG}] Already connected.");
                return;
            }

            _webSocket?.Close();

            _webSocket = new WebSocket($"ws://{serverIPAddress}:{serverPort}");

            Logging.Log($"[{TAG}] Connecting to {serverIPAddress}:{serverPort}");

            _webSocket.OnOpen += () =>
            {
                Logging.Log($"[{TAG}] Connected to server!");
                _isReconnecting = false;

                OnConnectionUpdate?.Invoke(true);
            };

            _webSocket.OnMessage += ProcessMessage;

            _webSocket.OnError += error => { Logging.LogError($"[{TAG}] Error: {error}"); };

            _webSocket.OnClose += closeCode =>
            {
                OnConnectionUpdate?.Invoke(false);

                Logging.Log($"[{TAG}] Closed: {closeCode}");
                if (!_isReconnecting)
                    RetryConnection();
            };

            StartCoroutine(Connect());
        }

        private IEnumerator Connect()
        {
            yield return _webSocket.Connect();
        }

        private void RetryConnection()
        {
            if (_isReconnecting) return;

            _isReconnecting = true;
            Logging.Log($"[{TAG}] Retrying connection");

            StartCoroutine(RetryCoroutine());
        }

        private IEnumerator RetryCoroutine()
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                yield return new WaitForSeconds(retryDelay);

                Logging.Log($"[{TAG}] Attempting to reconnect");
                ConnectToServer();
            }

            _isReconnecting = false;
        }

        private void ProcessMessage(byte[] bytes)
        {
            if (bytes.Length == 0) return;

            Logging.Log($"[{TAG}] Received {bytes.Length} bytes");

            var type = (MessageEnum)bytes[0];
            switch (type)
            {
                case MessageEnum.Location:
                    OnLocation(bytes);
                    break;
            }
        }

        private void OnLocation(byte[] bytes)
        {
            if (bytes.Length < 1 + sizeof(long) + 3 * sizeof(double)) return;

            var timestampMs = BitConverter.ToInt64(bytes, 1);
            var latitude = BitConverter.ToDouble(bytes, 1 + sizeof(long));
            var longitude = BitConverter.ToDouble(bytes, 1 + sizeof(long) + sizeof(double));
            var heading = BitConverter.ToDouble(bytes, 1 + sizeof(long) + 2 * sizeof(double));

            var timestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);

            Logging.Log($"[{TAG}] [{timestampUtc:yyyy-MM-dd HH:mm:ss.fff}] {latitude}, {longitude}, {heading}");

            OnGeoPositionHeadingReceived?.Invoke(new GeopositionHeading(timestampUtc, latitude, longitude, heading));
        }

        private async void OnApplicationQuit()
        {
            if (_webSocket is { State: WebSocketState.Open })
            {
                await _webSocket.Close();
            }
        }
    }
}
