using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using NativeWebSocket;

/// <summary>
/// Unity-side WebSocket endpoint adapter for the Admin UI protocol.
/// This class connects to a WebSocket server and exchanges JSON compatible
/// with the browser client shown in the project docs.
/// </summary>
public class UnityAdminWebSocketClient : MonoBehaviour
{
  [Header("Connection")]
  [SerializeField] private string serverUrl = "ws://localhost:8085";
  [SerializeField] private bool autoConnectOnStart = true;
  [SerializeField] private bool autoReconnect = true;
  [SerializeField] private float reconnectDelaySeconds = 3f;

  [Header("Device Identity")]
  [SerializeField] private int connectionID = 1;
  [SerializeField] private string deviceId = "unity-device";
  [SerializeField] private string ipAddress = "127.0.0.1";

  [Header("Telemetry")]
  [SerializeField, Range(0f, 1f)] private float batteryLevel = 1f;
  [SerializeField] private int wifiSignalStrength = -45;
  [SerializeField] private bool sendStatusOnConnect = true;

  private WebSocket websocket;
  private bool isQuitting;

  [Serializable]
  private class DeviceConnectedMessage
  {
    public int connectionID;
    public string deviceId;
    public string ipAddress;
  }

  [Serializable]
  private class DeviceStatusMessage
  {
    public int connectionID;
    public float batteryLevel;
    public int wifiSignalStrength;
  }

  [Serializable]
  private class DisconnectMessage
  {
    public int connectionID;
  }

  [Serializable]
  private class SetGameModeMessage
  {
    public string type;
    public int gameMode;
  }

  private async void Start()
  {
    if (autoConnectOnStart)
    {
      await Connect();
    }
  }

  public async Task Connect()
  {
    if (websocket != null && websocket.State == WebSocketState.Open)
    {
      Debug.Log("[UnityAdminWS] Already connected");
      return;
    }

    websocket = new WebSocket(serverUrl);

    websocket.OnOpen += () =>
    {
      Debug.Log($"[UnityAdminWS] Connected to {serverUrl}");
      _ = SendDeviceConnected();

      if (sendStatusOnConnect)
      {
        _ = SendStatusUpdate(batteryLevel, wifiSignalStrength);
      }
    };

    websocket.OnError += error =>
    {
      Debug.LogError($"[UnityAdminWS] Error: {error}");
    };

    websocket.OnClose += code =>
    {
      Debug.Log($"[UnityAdminWS] Closed with code: {code}");

      if (!isQuitting && autoReconnect)
      {
        Invoke(nameof(TryReconnect), reconnectDelaySeconds);
      }
    };

    websocket.OnMessage += bytes =>
    {
      var text = Encoding.UTF8.GetString(bytes);
      HandleIncomingMessage(text);
    };

    await websocket.Connect();
  }

  private async void TryReconnect()
  {
    if (isQuitting) return;

    try
    {
      await Connect();
    }
    catch (Exception e)
    {
      Debug.LogWarning($"[UnityAdminWS] Reconnect failed: {e.Message}");
      Invoke(nameof(TryReconnect), reconnectDelaySeconds);
    }
  }

  private void HandleIncomingMessage(string json)
  {
    Debug.Log($"[UnityAdminWS] <= {json}");

    SetGameModeMessage gameModeMessage;

    try
    {
      gameModeMessage = JsonUtility.FromJson<SetGameModeMessage>(json);
    }
    catch (Exception e)
    {
      Debug.LogWarning($"[UnityAdminWS] Invalid JSON: {e.Message}");
      return;
    }

    if (gameModeMessage != null && gameModeMessage.type == "request/setGameMode")
    {
      ApplyGameMode(gameModeMessage.gameMode);
      return;
    }

    DisconnectMessage disconnectMessage;

    try
    {
      disconnectMessage = JsonUtility.FromJson<DisconnectMessage>(json);
    }
    catch
    {
      return;
    }

    if (disconnectMessage != null && disconnectMessage.connectionID == connectionID)
    {
      Debug.Log($"[UnityAdminWS] Disconnect requested for connectionID={connectionID}");
      _ = CloseAndNotifyDisconnected();
    }
  }

  private void ApplyGameMode(int mode)
  {
    switch (mode)
    {
      case 0:
        OnGameStart();
        break;
      case 1:
        OnGameStop();
        break;
      case 2:
        OnGamePause();
        break;
      default:
        Debug.LogWarning($"[UnityAdminWS] Unknown gameMode value: {mode}");
        break;
    }
  }

  protected virtual void OnGameStart()
  {
    Debug.Log("[UnityAdminWS] Game START command received");
  }

  protected virtual void OnGameStop()
  {
    Debug.Log("[UnityAdminWS] Game STOP command received");
  }

  protected virtual void OnGamePause()
  {
    Debug.Log("[UnityAdminWS] Game PAUSE command received");
  }

  public async Task SendDeviceConnected()
  {
    if (!IsOpen()) return;

    var message = new DeviceConnectedMessage
    {
      connectionID = connectionID,
      deviceId = deviceId,
      ipAddress = ipAddress
    };

    await SendJson(message);
  }

  public async Task SendStatusUpdate(float newBatteryLevel, int newWifiSignalStrength)
  {
    if (!IsOpen()) return;

    batteryLevel = Mathf.Clamp01(newBatteryLevel);
    wifiSignalStrength = newWifiSignalStrength;

    var message = new DeviceStatusMessage
    {
      connectionID = connectionID,
      batteryLevel = batteryLevel,
      wifiSignalStrength = wifiSignalStrength
    };

    await SendJson(message);
  }

  public async Task NotifyDisconnected()
  {
    if (!IsOpen()) return;

    var message = new DisconnectMessage
    {
      connectionID = connectionID
    };

    await SendJson(message);
  }

  private async Task CloseAndNotifyDisconnected()
  {
    await NotifyDisconnected();

    if (websocket != null)
    {
      await websocket.Close();
    }
  }

  private bool IsOpen()
  {
    return websocket != null && websocket.State == WebSocketState.Open;
  }

  private async Task SendJson(object payload)
  {
    var json = JsonUtility.ToJson(payload);
    await websocket.SendText(json);
    Debug.Log($"[UnityAdminWS] => {json}");
  }

  private void Update()
  {
    #if !UNITY_WEBGL || UNITY_EDITOR
      websocket?.DispatchMessageQueue();
    #endif
  }

  private async void OnApplicationQuit()
  {
    isQuitting = true;

    if (websocket != null && websocket.State == WebSocketState.Open)
    {
      await NotifyDisconnected();
      await websocket.Close();
    }
  }
}
